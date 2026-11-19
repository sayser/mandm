using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MandM.Services;

public sealed class OcrService
{
    private readonly OcrEngine? _engine;
    private byte[]? _bgraScratch;
    private byte[]? _packedScratch;
    private readonly List<string> _lineScratch = new(32);

    public OcrService()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
    }

    public bool IsAvailable => _engine is not null;

    public string? UnavailableReason =>
        _engine is null
            ? "Windows OCR is unavailable. Install an English OCR language pack (Settings → Time & language → Language & region)."
            : null;

    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        var lines = await RecognizeLinesAsync(bitmap, ct);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// OCR without PNG encode — packs BGRA into a SoftwareBitmap (hot-path friendly).
    /// </summary>
    public async Task<IReadOnlyList<string>> RecognizeLinesAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        if (_engine is null)
            return Array.Empty<string>();

        try
        {
            ScreenCaptureService.CopyBgra(bitmap, ref _bgraScratch, out var width, out var height, out var stride);
            var packed = PackBgra(_bgraScratch!, width, height, stride);

            using var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            softwareBitmap.CopyFromBuffer(packed.AsBuffer(0, width * height * 4));

            ct.ThrowIfCancellationRequested();
            var result = await _engine.RecognizeAsync(softwareBitmap);
            _lineScratch.Clear();
            foreach (var line in result.Lines)
            {
                var text = line.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    _lineScratch.Add(text);
            }

            return _lineScratch.Count == 0
                ? Array.Empty<string>()
                : _lineScratch.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await RecognizeLinesViaPngAsync(bitmap, ct);
        }
    }

    private byte[] PackBgra(byte[] src, int width, int height, int stride)
    {
        var rowBytes = width * 4;
        var needed = rowBytes * height;
        if (_packedScratch is null || _packedScratch.Length < needed)
            _packedScratch = new byte[needed];

        if (stride == rowBytes)
        {
            System.Buffer.BlockCopy(src, 0, _packedScratch, 0, needed);
            return _packedScratch;
        }

        for (var y = 0; y < height; y++)
            System.Buffer.BlockCopy(src, y * stride, _packedScratch, y * rowBytes, rowBytes);
        return _packedScratch;
    }

    private async Task<IReadOnlyList<string>> RecognizeLinesViaPngAsync(Bitmap bitmap, CancellationToken ct)
    {
        if (_engine is null)
            return Array.Empty<string>();

        using var stream = new InMemoryRandomAccessStream();
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();
            await stream.WriteAsync(bytes.AsBuffer());
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        ct.ThrowIfCancellationRequested();
        var result = await _engine.RecognizeAsync(softwareBitmap);
        var lines = new List<string>();
        foreach (var line in result.Lines)
        {
            var text = line.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add(text);
        }

        return lines;
    }
}
