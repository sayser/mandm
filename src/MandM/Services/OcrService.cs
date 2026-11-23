using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MandM.Services;

public sealed class OcrService : IDisposable
{
    private const int MaxFrames = 4;
    private OcrEngine? _engine;
    private int _engineThreadId = -1;
    private bool? _available;
    private byte[]? _bgraScratch;
    private byte[]? _packedScratch;
    private readonly List<string> _lineScratch = new(32);
    private readonly Dictionary<long, SoftwareBitmap> _frames = new();
    private readonly List<long> _frameOrder = new();

    public bool IsAvailable
    {
        get
        {
            if (_available is null)
            {
                // Probe only. The engine used for Recognize is created on the capture thread.
                var probe = TryCreateEngine();
                _available = probe is not null;
                Release(probe);
            }

            return _available.Value;
        }
    }

    public string? UnavailableReason =>
        IsAvailable
            ? null
            : "Windows OCR is unavailable. Install an English OCR language pack (Settings → Time & language → Language & region).";

    private OcrEngine? EngineOnThisThread()
    {
        var id = Environment.CurrentManagedThreadId;
        if (_engine is not null && _engineThreadId == id)
            return _engine;
        Release(_engine);
        _engine = TryCreateEngine();
        _engineThreadId = id;
        if (_engine is null)
            _available = false;
        return _engine;
    }

    private static OcrEngine? TryCreateEngine() =>
        OcrEngine.TryCreateFromUserProfileLanguages()
        ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

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
        var engine = EngineOnThisThread();
        if (engine is null)
            return Array.Empty<string>();

        try
        {
            ScreenCaptureService.CopyBgra(bitmap, ref _bgraScratch, out var width, out var height, out var stride);
            var packed = PackBgra(_bgraScratch!, width, height, stride);

            var softwareBitmap = Frame(width, height);
            softwareBitmap.CopyFromBuffer(packed.AsBuffer(0, width * height * 4));

            ct.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(softwareBitmap);
            try
            {
                return CopyLines(result);
            }
            finally
            {
                Release(result);
            }
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
        var engine = EngineOnThisThread();
        if (engine is null)
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
        var result = await engine.RecognizeAsync(softwareBitmap);
        try
        {
            return CopyLines(result);
        }
        finally
        {
            Release(result);
        }
    }

    private SoftwareBitmap Frame(int width, int height)
    {
        var key = ((long)width << 32) | (uint)height;
        if (_frames.TryGetValue(key, out var existing))
            return existing;

        while (_frames.Count >= MaxFrames && _frameOrder.Count > 0)
        {
            var old = _frameOrder[0];
            _frameOrder.RemoveAt(0);
            if (_frames.Remove(old, out var stale))
                stale.Dispose();
        }

        var created = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        _frames[key] = created;
        _frameOrder.Add(key);
        return created;
    }

    private IReadOnlyList<string> CopyLines(OcrResult result)
    {
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

    private static void Release(object? native)
    {
        if (native is IDisposable disposable)
            disposable.Dispose();
    }

    public void Dispose()
    {
        Release(_engine);
        _engine = null;
        foreach (var frame in _frames.Values)
            frame.Dispose();
        _frames.Clear();
        _frameOrder.Clear();
    }
}
