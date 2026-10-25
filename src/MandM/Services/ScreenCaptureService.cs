using System.Drawing;
using System.Drawing.Imaging;
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
}
