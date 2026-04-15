using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace KakaoCli.Win.Data;

public sealed record MemoryTextHit(
    string EncodingName,
    string Address,
    string Context
);

public static class WindowsKakaoMemoryReader
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<MemoryTextHit> ScanForText(
        string needle,
        int limit = 20,
        int contextChars = 320
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("KakaoTalk memory scanning requires Windows.");
        }

        if (string.IsNullOrWhiteSpace(needle))
        {
            throw new ArgumentException("Needle must not be empty.", nameof(needle));
        }

        var process = Process.GetProcessesByName("KakaoTalk").FirstOrDefault()
            ?? throw new InvalidOperationException("KakaoTalk.exe is not running.");

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not open KakaoTalk.exe for memory read.");
        }

        try
        {
            var found = new List<MemoryTextHit>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var asciiNeedle = Encoding.UTF8.GetBytes(needle);
            var unicodeNeedle = Encoding.Unicode.GetBytes(needle);
            long address = 0;

            while (found.Count < limit
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

                SearchBuffer(buffer, asciiNeedle, Encoding.UTF8, "utf8", baseAddress, contextChars, found, seen, limit);
                SearchBuffer(buffer, unicodeNeedle, Encoding.Unicode, "utf16le", baseAddress, contextChars, found, seen, limit);
            }

            return found;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static void SearchBuffer(
        byte[] buffer,
        byte[] needle,
        Encoding encoding,
        string encodingName,
        long baseAddress,
        int contextChars,
        List<MemoryTextHit> found,
        HashSet<string> seen,
        int limit
    )
    {
        var charWidth = encoding == Encoding.Unicode ? 2 : 1;
        var contextBytes = Math.Max(64, contextChars * charWidth);
        for (var i = 0; i <= buffer.Length - needle.Length && found.Count < limit; i += charWidth)
        {
            if (!BytesEqualAt(buffer, needle, i))
            {
                continue;
            }

            var start = Math.Max(0, i - contextBytes);
            var end = Math.Min(buffer.Length, i + needle.Length + contextBytes);
            if (encoding == Encoding.Unicode)
            {
                start -= start % 2;
                end -= end % 2;
            }

            var text = encoding.GetString(buffer, start, end - start);
            var cleaned = CleanContext(text);
            if (cleaned.Length == 0 || !seen.Add($"{encodingName}:{cleaned}"))
            {
                continue;
            }

            found.Add(new MemoryTextHit(
                encodingName,
                $"0x{baseAddress + i:X}",
                cleaned
            ));
        }
    }

    private static string CleanContext(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\0')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(ch) || ch is '\r' or '\n' or '\t')
            {
                builder.Append(ch);
            }
        }

        var cleaned = Whitespace.Replace(builder.ToString(), " ").Trim();
        return cleaned.Length <= 2000 ? cleaned : cleaned[..2000];
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

    private static bool IsReadableCommittedRegion(NativeMethods.MemoryBasicInformation memory)
    {
        const uint memCommit = 0x1000;
        const uint pageNoAccess = 0x01;
        const uint pageGuard = 0x100;
        return memory.State == memCommit
               && (memory.Protect & pageNoAccess) == 0
               && (memory.Protect & pageGuard) == 0;
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
