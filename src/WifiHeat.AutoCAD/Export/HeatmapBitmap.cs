using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WifiHeat.Core.Simulation;

namespace WifiHeat.AutoCAD.Export;

/// <summary>Pasa la imagen de Core a un bitmap de GDI+ con canal alfa.</summary>
public static class HeatmapBitmap
{
    public static Bitmap ToBitmap(HeatmapImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.Bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public static void Save(HeatmapImage image, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        using Bitmap bitmap = ToBitmap(image);
        bitmap.Save(path, ImageFormat.Png);
    }
}
