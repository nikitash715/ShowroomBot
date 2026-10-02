using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShowroomBot.Windows;

public sealed class MouseInputSender
{
    public void ClickLeft()
    {
        SendMouseInput(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        SendMouseInput(NativeMethods.MOUSEEVENTF_LEFTUP);
    }

    public void Scroll(int notches)
    {
        SendMouseInput(NativeMethods.MOUSEEVENTF_WHEEL, unchecked((uint)(notches * 120)));
    }

    private static void SendMouseInput(uint flags, uint data = 0)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            data = new NativeMethods.INPUTUNION
            {
                mouseInput = new NativeMethods.MOUSEINPUT { dwFlags = flags, mouseData = data }
            }
        };
        if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить ввод мыши.");
        }
    }

    public async Task MoveToAsync(
        Point target,
        TimeSpan movementDuration,
        TimeSpan stepDelay,
        CancellationToken cancellationToken = default)
    {
        if (!NativeMethods.GetCursorPos(out var start))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось определить положение указателя мыши.");
        }

        if (movementDuration <= TimeSpan.Zero || stepDelay <= TimeSpan.Zero)
        {
            SendAbsoluteMove(target.X, target.Y);
            return;
        }

        var steps = Math.Max(1, (int)Math.Ceiling(movementDuration / stepDelay));
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = (double)step / steps;
            var x = (int)Math.Round(start.X + ((target.X - start.X) * progress));
            var y = (int)Math.Round(start.Y + ((target.Y - start.Y) * progress));
            SendAbsoluteMove(x, y);

            if (step < steps)
            {
                await Task.Delay(stepDelay, cancellationToken);
            }
        }
    }

    private static void SendAbsoluteMove(int screenX, int screenY)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (virtualWidth <= 1 || virtualHeight <= 1)
        {
            throw new InvalidOperationException("Не удалось определить размеры виртуального рабочего стола.");
        }

        var normalizedX = (int)Math.Round((screenX - virtualLeft) * 65535d / (virtualWidth - 1));
        var normalizedY = (int)Math.Round((screenY - virtualTop) * 65535d / (virtualHeight - 1));
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            data = new NativeMethods.INPUTUNION
            {
                mouseInput = new NativeMethods.MOUSEINPUT
                {
                    dx = Math.Clamp(normalizedX, 0, 65535),
                    dy = Math.Clamp(normalizedY, 0, 65535),
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE |
                        NativeMethods.MOUSEEVENTF_ABSOLUTE |
                        NativeMethods.MOUSEEVENTF_VIRTUALDESK
                }
            }
        };

        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось переместить указатель мыши.");
        }
    }
}
