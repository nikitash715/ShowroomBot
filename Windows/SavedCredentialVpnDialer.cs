using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShowroomBot.Windows;

internal sealed class SavedCredentialVpnDialer
{
    // Native code retains the delegate until the attempt finishes.
    private static readonly ConcurrentDictionary<Guid, SavedCredentialVpnDialer> Pending = new();
    private readonly Guid _id = Guid.NewGuid();
    private readonly DialCallback _callback;
    private int _completed;

    private SavedCredentialVpnDialer() => _callback = OnStateChanged;

    public static void Start(string name)
    {
        var book = VpnPhonebook.FindConnection(name);
        var credentials = new Credentials { Size = Marshal.SizeOf<Credentials>(), Mask = 7 };
        var error = RasGetCredentials(book, name, ref credentials);
        if (error != 0) throw new Win32Exception((int)error);
        if ((credentials.Mask & 3) != 3 || string.IsNullOrWhiteSpace(credentials.UserName) ||
            credentials.UserName == "*" || string.IsNullOrEmpty(credentials.Password))
            throw new InvalidOperationException("Сохраните логин и пароль VPN в Windows.");

        var parameters = new DialParameters
        {
            Size = Marshal.SizeOf<DialParameters>(),
            EntryName = name,
            PhoneNumber = "",
            CallbackNumber = "",
            UserName = credentials.UserName,
            Password = credentials.Password,
            Domain = credentials.Domain ?? ""
        };
        // Password is the opaque handle from RasGetCredentials, never a command-line argument.
        var attempt = new SavedCredentialVpnDialer();
        Pending[attempt._id] = attempt;
        error = RasDial(IntPtr.Zero, book, ref parameters, 1, attempt._callback, out var connection);
        if (error != 0)
        {
            attempt.Complete(connection, error);
            throw new Win32Exception((int)error);
        }
        GC.KeepAlive(attempt);
    }

    private void OnStateChanged(IntPtr connection, uint message, uint state, uint error, uint extendedError)
    {
        if (error != 0 || state == 0x2000 || state == 0x2001)
            Complete(connection, error != 0 ? error : state == 0x2001 ? 628u : 0u);
    }

    private void Complete(IntPtr connection, uint error)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        if (error != 0)
        {
            Trace.WriteLine($"VPN connection failed: RAS error {error}.");
            if (connection != IntPtr.Zero) RasHangUp(connection);
        }
        Pending.TryRemove(_id, out _);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credentials
    {
        public int Size;
        public uint Mask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Domain;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DialParameters
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string EntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string PhoneNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string CallbackNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Domain;
        public uint SubEntry;
        public UIntPtr CallbackId;
        public uint InterfaceIndex;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DialCallback(IntPtr connection, uint message, uint state, uint error, uint extendedError);

    [DllImport("rasapi32.dll", EntryPoint = "RasGetCredentialsW", CharSet = CharSet.Unicode)]
    private static extern uint RasGetCredentials(string phonebook, string entry, ref Credentials credentials);

    [DllImport("rasapi32.dll", EntryPoint = "RasDialW", CharSet = CharSet.Unicode)]
    private static extern uint RasDial(IntPtr extensions, string phonebook, ref DialParameters parameters,
        uint notifierType, DialCallback notifier, out IntPtr connection);

    [DllImport("rasapi32.dll", EntryPoint = "RasHangUpW")]
    private static extern uint RasHangUp(IntPtr connection);
}
