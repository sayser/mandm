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
        if (_engine is null)
            return string.Empty;

        using var stream = new InMemoryRandomAccessStream();
        await SaveBitmapAsync(bitmap, stream);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        ct.ThrowIfCancellationRequested();
        var result = await _engine.RecognizeAsync(softwareBitmap);
        return result.Text ?? string.Empty;
    }

    public async Task<IReadOnlyList<string>> RecognizeLinesAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        if (_engine is null)
            return Array.Empty<string>();

        using var stream = new InMemoryRandomAccessStream();
        await SaveBitmapAsync(bitmap, stream);
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

    private static async Task SaveBitmapAsync(Bitmap bitmap, IRandomAccessStream stream)
    {
        // Encode via System.Drawing into a MemoryStream, then copy into WinRT stream.
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bytes = ms.ToArray();
        await stream.WriteAsync(bytes.AsBuffer());
    }
}
