using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MandM.Models;

namespace MandM.Services;

public sealed class ScreenCaptureService
{
    public Bitmap? Capture(CaptureRegion region)
    {
        if (!region.IsValid) return null;

        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.X, region.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>
    /// Fast content fingerprint for skip-OCR-when-unchanged.
    /// Samples every Nth pixel so busy frames stay cheap.
    /// </summary>
    public static ulong ComputeContentHash(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var height = data.Height;
            var width = data.Width;
            // Sample ~every 8th pixel on each axis (~1/64 of pixels).
            const int stepX = 8;
            const int stepY = 8;
            ulong hash = 14695981039346656037UL; // FNV-1a offset
            var row = new byte[Math.Abs(stride)];

            for (var y = 0; y < height; y += stepY)
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, row.Length);
                for (var x = 0; x < width; x += stepX)
                {
                    var i = x * 4;
                    if (i + 3 >= row.Length) break;
                    hash ^= row[i];
                    hash *= 1099511628211UL;
                    hash ^= row[i + 1];
                    hash *= 1099511628211UL;
                    hash ^= row[i + 2];
                    hash *= 1099511628211UL;
                }
            }

            hash ^= (uint)width;
            hash *= 1099511628211UL;
            hash ^= (uint)height;
            hash *= 1099511628211UL;
            return hash;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
