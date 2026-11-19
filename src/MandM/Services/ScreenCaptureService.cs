using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MandM.Models;

namespace MandM.Services;

public sealed class ScreenCaptureService
{
    /// <summary>
    /// Capture into an existing bitmap when size matches; otherwise allocate a new one.
    /// Caller owns and must dispose the returned bitmap when replacing the buffer.
    /// </summary>
    public Bitmap? CaptureInto(CaptureRegion region, Bitmap? reuse)
    {
        if (!region.IsValid) return null;

        Bitmap bmp;
        if (reuse is not null
            && reuse.Width == region.Width
            && reuse.Height == region.Height
            && reuse.PixelFormat == PixelFormat.Format32bppArgb)
        {
            bmp = reuse;
        }
        else
        {
            reuse?.Dispose();
            bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        }

        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.X, region.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public Bitmap? Capture(CaptureRegion region) => CaptureInto(region, null);

    /// <summary>
    /// Fast content fingerprint — samples every 8th pixel via unsafe pointer (no row buffer alloc).
    /// </summary>
    public static unsafe ulong ComputeContentHash(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            const int stepX = 8;
            const int stepY = 8;
            ulong hash = 14695981039346656037UL; // FNV-1a
            var height = data.Height;
            var width = data.Width;
            var stride = data.Stride;
            var scan0 = (byte*)data.Scan0;

            for (var y = 0; y < height; y += stepY)
            {
                var row = scan0 + y * stride;
                for (var x = 0; x < width; x += stepX)
                {
                    var p = row + x * 4;
                    hash ^= p[0];
                    hash *= 1099511628211UL;
                    hash ^= p[1];
                    hash *= 1099511628211UL;
                    hash ^= p[2];
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

    /// <summary>Copy BGRA pixels into a reused buffer (stride may include padding).</summary>
    public static void CopyBgra(Bitmap bitmap, ref byte[]? buffer, out int width, out int height, out int stride)
    {
        width = bitmap.Width;
        height = bitmap.Height;
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            stride = Math.Abs(data.Stride);
            var needed = stride * height;
            if (buffer is null || buffer.Length < needed)
                buffer = new byte[needed];
            Marshal.Copy(data.Scan0, buffer, 0, needed);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
