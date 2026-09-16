using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShowroomBot.Windows;

public sealed class IdleDetector : IIdleDetector
{
    public TimeSpan GetIdleTime()
    {
        var lastInputInfo = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        if (!NativeMethods.GetLastInputInfo(ref lastInputInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var elapsedMilliseconds = Environment.TickCount64 - lastInputInfo.dwTime;
        return TimeSpan.FromMilliseconds(Math.Max(0, elapsedMilliseconds));
    }
}
