using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KakaoCli.Win.Data;

public sealed record WindowsDeviceInfo(
    string RegistryKeyName,
    string DevId,
    string SystemUuid,
    string DiskModel,
    string DiskSerial,
    string Pragma
);

public static class WindowsKakaoIdentity
{
    private static readonly byte[] BuiltInPragmaKey = Convert.FromHexString("9FBAE3118FDE5DEAEB8279D08F1D4C79");
    private static readonly Regex ImmediateUserIdPattern = new(@"^\d{6,15}", RegexOptions.Compiled);
    private static readonly Regex[] LikelyUserIdPatterns =
    [
        new("\"user_id\"\\s*:\\s*(\\d{5,15})", RegexOptions.Compiled),
        new("\"from\"\\s*:\\s*\"(\\d{5,15})\"", RegexOptions.Compiled),
        new(@"\bnt\s+(\d{5,15})", RegexOptions.Compiled),
        new(@"==(\d{5,15})", RegexOptions.Compiled),
    ];

    public static WindowsDeviceInfo ResolveDeviceInfo()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows KakaoTalk identity discovery requires Windows.");
        }

        using var deviceInfoRoot = Registry.CurrentUser.OpenSubKey(@"Software\Kakao\KakaoTalk\DeviceInfo")
            ?? throw new InvalidOperationException(@"HKCU\Software\Kakao\KakaoTalk\DeviceInfo was not found.");

        var last = deviceInfoRoot.GetValue("Last") as string;
        var keyNames = deviceInfoRoot.GetSubKeyNames()
            .OrderByDescending(name => string.Equals(name, last, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var keyName in keyNames)
        {
            using var key = deviceInfoRoot.OpenSubKey(keyName);
            var uuid = key?.GetValue("sys_uuid") as string;
            var model = key?.GetValue("hdd_model") as string;
            var serial = (key?.GetValue("hdd_serial_v2") as string) ?? (key?.GetValue("hdd_serial") as string);
            var devId = key?.GetValue("dev_id") as string;
            if (string.IsNullOrWhiteSpace(uuid)
                || string.IsNullOrWhiteSpace(model)
                || string.IsNullOrWhiteSpace(serial)
                || string.IsNullOrWhiteSpace(devId))
            {
                continue;
            }

            return new WindowsDeviceInfo(
                keyName,
                devId.Trim(),
                uuid.Trim(),
                model.Trim(),
                serial.Trim(),
                GeneratePragma(uuid.Trim(), model.Trim(), serial.Trim())
            );
        }

        throw new InvalidOperationException("No complete KakaoTalk DeviceInfo registry entry was found.");
    }

    public static string GeneratePragma(string systemUuid, string diskModel, string diskSerial)
    {
        var input = Encoding.UTF8.GetBytes($"{systemUuid}|{diskModel}|{diskSerial}");
        using var aes = Aes.Create();
        aes.Key = BuiltInPragmaKey;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);
        return Convert.ToBase64String(SHA512.HashData(encrypted));
    }

    public static IReadOnlyList<string> FindPragmasInKakaoTalkMemory(string devId, int maxMatches = 20)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(devId))
        {
            return [];
        }

        return ScanKakaoTalkMemory((buffer, found) =>
        {
            SearchBufferForPragmas(buffer, Encoding.ASCII.GetBytes(devId), Encoding.ASCII, devId, found, maxMatches);
            SearchBufferForPragmas(buffer, Encoding.Unicode.GetBytes(devId), Encoding.Unicode, devId, found, maxMatches);
        }, maxMatches);
    }

    public static IReadOnlyList<string> FindUserIdsInKakaoTalkMemory(string pragma, int maxMatches = 20)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(pragma))
        {
            return [];
        }

        return ScanKakaoTalkMemory((buffer, found) =>
        {
            SearchBufferForUserIds(buffer, Encoding.ASCII.GetBytes(pragma), Encoding.ASCII, found, maxMatches);
            SearchBufferForUserIds(buffer, Encoding.Unicode.GetBytes(pragma), Encoding.Unicode, found, maxMatches);
        }, maxMatches);
    }

    public static IReadOnlyList<string> FindLikelyUserIdsInKakaoTalkMemory(int maxMatches = 50)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return ScanKakaoTalkMemory((buffer, found) =>
        {
            SearchTextForLikelyUserIds(Encoding.ASCII.GetString(buffer), found, maxMatches);
            if (buffer.Length >= 2)
            {
                var evenLength = buffer.Length - buffer.Length % 2;
                SearchTextForLikelyUserIds(Encoding.Unicode.GetString(buffer, 0, evenLength), found, maxMatches);
            }
        }, maxMatches);
    }

    private static IReadOnlyList<string> ScanKakaoTalkMemory(
        Action<byte[], LinkedHashSet<string>> search,
        int maxMatches
    )
    {
        var process = Process.GetProcessesByName("KakaoTalk").FirstOrDefault();
        if (process is null)
        {
            return [];
        }

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var found = new LinkedHashSet<string>();
            long address = 0;

            while (found.Count < maxMatches
                   && NativeMethods.VirtualQueryEx(
                       handle,
                       new IntPtr(address),
                       out var memory,
                       (uint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation>()
                   ) != 0)
            {
                var baseAddress = memory.BaseAddress.ToInt64();
                var regionSize = memory.RegionSize.ToInt64();
                address = baseAddress + Math.Max(regionSize, 0x1000);

                if (!IsReadableCommittedRegion(memory) || regionSize <= 0 || regionSize > 128L * 1024 * 1024)
                {
                    continue;
                }

                var buffer = new byte[regionSize];
                if (!NativeMethods.ReadProcessMemory(handle, memory.BaseAddress, buffer, buffer.Length, out var read)
                    || read <= 0)
                {
                    continue;
                }

                if (read != buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }

                search(buffer, found);
            }

            return found.ToList();
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool IsReadableCommittedRegion(NativeMethods.MemoryBasicInformation memory)
    {
        const uint memCommit = 0x1000;
        const uint pageNoAccess = 0x01;
        const uint pageGuard = 0x100;
        return memory.State == memCommit
               && (memory.Protect & pageNoAccess) == 0
               && (memory.Protect & pageGuard) == 0;
    }

    private static void SearchBufferForUserIds(
        byte[] buffer,
        byte[] needle,
        Encoding encoding,
        LinkedHashSet<string> found,
        int maxMatches
    )
    {
        for (var i = 0; i <= buffer.Length - needle.Length && found.Count < maxMatches; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (buffer[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            var start = i + needle.Length;
            var take = Math.Min(80, buffer.Length - start);
            if (take <= 0)
            {
                continue;
            }

            if (encoding == Encoding.Unicode)
            {
                take -= take % 2;
            }

            var tail = encoding.GetString(buffer, start, take).TrimStart('\0', ' ', '\t', '\r', '\n');
            var match = ImmediateUserIdPattern.Match(tail);
            if (match.Success)
            {
                found.Add(match.Value);
            }
        }
    }

    private static void SearchBufferForPragmas(
        byte[] buffer,
        byte[] needle,
        Encoding encoding,
        string devId,
        LinkedHashSet<string> found,
        int maxMatches
    )
    {
        var charSize = encoding == Encoding.Unicode ? 2 : 1;
        var candidateChars = 88;
        var candidateBytes = candidateChars * charSize;
        var devIdChars = devId.Length;
        var maxPrefixChars = candidateChars - devIdChars;

        for (var i = 0; i <= buffer.Length - needle.Length && found.Count < maxMatches; i += charSize)
        {
            if (!BytesEqualAt(buffer, needle, i))
            {
                continue;
            }

            for (var prefixChars = 0; prefixChars <= maxPrefixChars && found.Count < maxMatches; prefixChars++)
            {
                var start = i - prefixChars * charSize;
                if (start < 0 || start + candidateBytes > buffer.Length)
                {
                    continue;
                }

                var raw = encoding.GetString(buffer, start, candidateBytes);
                if (!raw.Contains(devId, StringComparison.Ordinal)
                    || !raw.EndsWith("==", StringComparison.Ordinal)
                    || !raw.All(IsBase64Char)
                    || !IsBase64(raw))
                {
                    continue;
                }

                found.Add(raw);
            }
        }
    }

    private static void SearchTextForLikelyUserIds(string text, LinkedHashSet<string> found, int maxMatches)
    {
        foreach (var pattern in LikelyUserIdPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (found.Count >= maxMatches)
                {
                    return;
                }

                var value = match.Groups[1].Value;
                if (value.Any(ch => ch != '0'))
                {
                    found.Add(value);
                }
            }
        }
    }

    private static bool BytesEqualAt(byte[] buffer, byte[] needle, int offset)
    {
        if (offset < 0 || offset + needle.Length > buffer.Length)
        {
            return false;
        }

        for (var i = 0; i < needle.Length; i++)
        {
            if (buffer[offset + i] != needle[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBase64Char(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '+'
            or '/'
            or '=';
    }

    private static bool IsBase64(string value)
    {
        try
        {
            _ = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class LinkedHashSet<T> : List<T>
    {
        private readonly HashSet<T> _seen = [];

        public new void Add(T item)
        {
            if (_seen.Add(item))
            {
                base.Add(item);
            }
        }
    }

    private static class NativeMethods
    {
        public const uint ProcessVmRead = 0x0010;
        public const uint ProcessQueryInformation = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MemoryBasicInformation lpBuffer,
            uint dwLength
        );

        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
    }
}
