using ShowroomBot.Configuration;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

public sealed class MouseInputSender
{
    public void ClickLeft()
    {
        ScenarioExecution.Perform(() =>
        {
            SendMouseInputCore(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0);
            SendMouseInputCore(NativeMethods.MOUSEEVENTF_LEFTUP, 0);
        });
    }

    public void Scroll(int notches)
    {
        SendMouseInput(NativeMethods.MOUSEEVENTF_WHEEL, unchecked((uint)(notches * 120)));
    }

    private static void SendMouseInput(uint flags, uint data = 0)
    {
        ScenarioExecution.Perform(() => SendMouseInputCore(flags, data));
    }

    private static void SendMouseInputCore(uint flags, uint data)
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

    private readonly MouseSettings _settings;
    public MouseInputSender(MouseSettings? settings = null) => _settings = settings ?? new();
    public async Task MoveToAsync(Point target, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            ScenarioExecution.Current?.Token ?? CancellationToken.None);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        if (!NativeMethods.GetCursorPos(out var start))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось определить положение указателя мыши.");
        var duration = Math.Max(1, _settings.MovementDurationMilliseconds *
            (1 + (Random.Shared.NextDouble() * 2 - 1) * _settings.DurationVariation));
        var steps = Math.Max(2, (int)Math.Ceiling(duration / Math.Max(1, _settings.StepDelayMilliseconds)));
        var path = new MouseTrajectory(new Point(start.X, start.Y), target, _settings);
        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(duration / steps), token);
            token.ThrowIfCancellationRequested();
            var point = path.At((double)step / steps);
            SendAbsoluteMove(point.X, point.Y);
        }
    }
    private static void SendAbsoluteMove(int screenX, int screenY)
    {
        ScenarioExecution.Perform(() => SendAbsoluteMoveCore(screenX, screenY));
    }

    private static void SendAbsoluteMoveCore(int screenX, int screenY)
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
