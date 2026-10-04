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

    public void SendKey(ushort virtualKey) => SendInputs(
        CreateVirtualKeyInput(virtualKey, false), CreateVirtualKeyInput(virtualKey, true));

    public async Task SendKeyAsync(ushort virtualKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SendInputs(CreateVirtualKeyInput(virtualKey, false));
        try { await Task.Delay(80, token); }
        finally { SendInputsCore([CreateVirtualKeyInput(virtualKey, true)]); }
        await Task.Delay(100, token);
    }

    public async Task ClearAutoIndentAsync(CancellationToken token)
    {
        // Home twice also handles editors with a smart Home (first non-space character).
        await SendKeyAsync(0x24, token);
        await SendKeyAsync(0x24, token);
        token.ThrowIfCancellationRequested();
        SendInputs(CreateVirtualKeyInput(0x10, false));
        try
        {
            await Task.Delay(80, token);
            await SendKeyAsync(0x23, token); // Shift+End selects the new line's indentation.
        }
        finally { SendInputsCore([CreateVirtualKeyInput(0x10, true)]); }
        await Task.Delay(100, token);
        // Delete at an empty final line is harmless; Backspace would join the previous line.
        await SendKeyAsync(0x2E, token);
    }

    // Physical letter keys are independent of the active Russian/English layout.
    // Separate RDP packets give the remote Ctrl state time to arrive before A/V/C.
    public async Task SendControlShortcutAsync(ushort scanCode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SendInputs(CreateVirtualKeyInput(0x11, false));
        try
        {
            await Task.Delay(80, token);
            SendInputs(CreateScanCodeInput(scanCode, false));
            await Task.Delay(80, token);
            SendInputs(CreateScanCodeInput(scanCode, true));
        }
        finally
        {
            // Release modifiers even after emergency cancellation.
            SendInputsCore([CreateScanCodeInput(scanCode, true), CreateVirtualKeyInput(0x11, true)]);
        }
        await Task.Delay(100, token);
    }

    private static NativeMethods.INPUT CreateScanCodeInput(ushort scanCode, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        data = new NativeMethods.INPUTUNION
        {
            keyboardInput = new NativeMethods.KEYBDINPUT
            {
                wScan = scanCode,
                dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0)
            }
        }
    };

    public async Task ReleaseControlAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SendInputs(CreateVirtualKeyInput(0x11, true));
        await Task.Delay(150, token);
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
        // RDP needs physical scan codes for navigation. A VK-only Home/End
        // can arrive as the numeric keypad's 7/1 instead of navigation.
        ushort scanCode = virtualKey switch
        {
            0x24 => 0x47, // Home
            0x23 => 0x4F, // End
            0x2E => 0x53, // Delete
            0x0D => 0x1C, // Enter
            0x08 => 0x0E, // Backspace
            0x1B => 0x01, // Escape
            _ => 0
        };
        if (scanCode != 0)
        {
            var physical = CreateScanCodeInput(scanCode, keyUp);
            if (virtualKey is 0x24 or 0x23 or 0x2E)
                physical.data.keyboardInput.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            return physical;
        }
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            data = new NativeMethods.INPUTUNION
            {
                keyboardInput = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    // Navigation keys must use the dedicated extended-key block.
                    // Without this flag RDP can interpret Home/Delete as NumPad 7/decimal.
                    dwFlags = (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0) |
                        (virtualKey is (>= 0x21 and <= 0x28) or 0x2D or 0x2E
                            ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0)
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
