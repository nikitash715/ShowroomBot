using System.Runtime.InteropServices;

namespace ShowroomBot.Windows;

public static class VpnPhonebook
{
    public static string FindConnection(string name, IEnumerable<string>? phonebooks = null)
    {
        phonebooks ??=
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk")
        ];
        foreach (var path in phonebooks)
        {
            if (!File.Exists(path)) continue;
            var section = new char[32768];
            var length = GetPrivateProfileSection(name, section, (uint)section.Length, path);
            if (length == 0) continue;
            if (length >= section.Length - 2)
                throw new InvalidOperationException("VPN-профиль слишком большой.");

            return path;
        }
        throw new InvalidOperationException("VPN-профиль не найден в телефонной книге Windows.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileSection(string section, [Out] char[] buffer, uint size, string path);

}
