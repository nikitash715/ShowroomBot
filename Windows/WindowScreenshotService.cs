using System.ComponentModel;
using System.Drawing.Imaging;

namespace ShowroomBot.Windows;

public sealed class WindowScreenshotService
{
    public string CaptureClientArea(IntPtr windowHandle, string outputDirectory)
    {
        if (!NativeMethods.GetClientRect(windowHandle, out var clientRect))
        {
            throw new Win32Exception("Не удалось определить клиентскую область окна RDP.");
        }

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Клиентская область окна RDP имеет нулевой размер.");
        }

        var screenOrigin = new NativeMethods.POINT { X = clientRect.Left, Y = clientRect.Top };
        if (!NativeMethods.ClientToScreen(windowHandle, ref screenOrigin))
        {
            throw new Win32Exception("Не удалось определить положение окна RDP на экране.");
        }

        Directory.CreateDirectory(outputDirectory);
        var filePath = Path.Combine(
            outputDirectory,
            $"rdp-test-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                screenOrigin.X,
                screenOrigin.Y,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }
}
