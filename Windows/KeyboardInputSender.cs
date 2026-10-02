using System.ComponentModel;
using System.Runtime.InteropServices;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

public sealed class KeyboardInputSender
{
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyR = 0x52;
    private const ushort VirtualKeyLeftWindows = 0x5B;

    public void SendWindowsRun()
    {
        SendInputs(
            CreateVirtualKeyInput(VirtualKeyLeftWindows, keyUp: false),
            CreateVirtualKeyInput(VirtualKeyR, keyUp: false),
            CreateVirtualKeyInput(VirtualKeyR, keyUp: true),
            CreateVirtualKeyInput(VirtualKeyLeftWindows, keyUp: true));
    }

    public void SendEnter()
    {
        SendInputs(
            CreateVirtualKeyInput(VirtualKeyEnter, keyUp: false),
            CreateVirtualKeyInput(VirtualKeyEnter, keyUp: true));
    }

    // Hold Alt across all Tab presses to select an MRU index instead of toggling two windows.
    // mstsc must forward Windows key combinations to the remote computer.
    public void SelectRemoteWindow(int index)
    {
        const ushort alt = 0x12;
        const ushort tab = 0x09;
        SendInputs(CreateVirtualKeyInput(alt, false));
        try
        {
            for (var i = 0; i < index; i++)
                SendInputs(CreateVirtualKeyInput(tab, false), CreateVirtualKeyInput(tab, true));
        }
        finally
        {
            // Release our held modifier even when cancellation has blocked all further input.
            SendInputsCore([CreateVirtualKeyInput(alt, true)]);
        }
    }

    public void SendText(string text)
    {
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        SendInputs(inputs.ToArray());
    }

    public async Task SendTextAsync(
        string text,
        TimeSpan characterDelay,
        CancellationToken cancellationToken = default)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendInputs(
                CreateUnicodeInput(character, keyUp: false),
                CreateUnicodeInput(character, keyUp: true));

            if (characterDelay > TimeSpan.Zero)
            {
                await Task.Delay(characterDelay, cancellationToken);
            }
        }
    }

    private static NativeMethods.INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            data = new NativeMethods.INPUTUNION
            {
                keyboardInput = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0
                }
            }
        };
    }

    private static NativeMethods.INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            data = new NativeMethods.INPUTUNION
            {
                keyboardInput = new NativeMethods.KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE |
                        (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0)
                }
            }
        };
    }

    private static void SendInputs(params NativeMethods.INPUT[] inputs)
    {
        ScenarioExecution.Perform(() => SendInputsCore(inputs));
    }

    private static void SendInputsCore(NativeMethods.INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить клавиатурный ввод.");
        }
    }
}
