using System.ComponentModel;
using System.Drawing.Imaging;

namespace ShowroomBot.Windows;

public sealed class WindowScreenshotService
{
    public static Rectangle GetClientScreenBounds(IntPtr windowHandle)
    {
        if (!NativeMethods.GetClientRect(windowHandle, out var client))
            throw new Win32Exception("Не удалось определить клиентскую область RDP.");
        var origin = new NativeMethods.POINT { X = client.Left, Y = client.Top };
        if (!NativeMethods.ClientToScreen(windowHandle, ref origin))
            throw new Win32Exception("Не удалось определить положение клиентской области RDP.");
        if (client.Right <= client.Left || client.Bottom <= client.Top)
            throw new InvalidOperationException("Клиентская область RDP пуста.");
        return new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }
    public string CaptureDesktop(string outputDirectory)
    {
        var bounds = SystemInformation.VirtualScreen;
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"error-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        using var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        image.Save(path, ImageFormat.Png);
        return path;
    }

    public Point ClientToScreen(IntPtr windowHandle, Point point)
    {
        var nativePoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
        if (!NativeMethods.ClientToScreen(windowHandle, ref nativePoint))
        {
            throw new Win32Exception("Не удалось определить положение окна RDP на экране.");
        }
        return new Point(nativePoint.X, nativePoint.Y);
    }

    public string CaptureClientArea(IntPtr windowHandle, string outputDirectory)
    {
        var initialBounds = GetClientScreenBounds(windowHandle);
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
        var finalBounds = GetClientScreenBounds(windowHandle);
        File.WriteAllText(Path.ChangeExtension(filePath, ".window.json"),
            System.Text.Json.JsonSerializer.Serialize(new { Window = windowHandle.ToInt64(),
                Before = initialBounds, After = finalBounds, Width = width, Height = height }));
        if (initialBounds != finalBounds || initialBounds.Size != bitmap.Size ||
            initialBounds.Location != new Point(screenOrigin.X, screenOrigin.Y))
            throw new InvalidOperationException("RDP изменил размер или положение во время снимка; координаты не используются.");
        return filePath;
    }
}
