using System.Security.Cryptography;
using System.Text;

namespace KakaoCli.Win.Data;

public sealed record DecryptedChatLog(
    string SourcePath,
    string DecryptedPath,
    string UserId,
    string KeyMode
);

public sealed record ChatLogHeaderAttempt(
    string Pragma,
    string UserId,
    string KeyMode,
    bool MatchesSqliteHeader,
    string DecryptedHeaderHex
);

public static class WindowsChatLogCrypto
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    public static DecryptedChatLog DecryptToTemp(
        string encryptedPath,
        string pragma,
        string userId,
        string? outputPath = null
    )
    {
        if (!File.Exists(encryptedPath))
        {
            throw new FileNotFoundException("Encrypted chat log was not found.", encryptedPath);
        }

        var key = FindWorkingKey(encryptedPath, pragma, userId);
        var destination = outputPath ?? BuildTempPath(encryptedPath, key.UserId, key.Mode);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(destination)
            || File.GetLastWriteTimeUtc(destination) < File.GetLastWriteTimeUtc(encryptedPath))
        {
            DecryptDatabase(encryptedPath, destination, key.Key, key.Iv);
        }

        return new DecryptedChatLog(encryptedPath, destination, key.UserId, key.Mode);
    }

    public static string ResolveChatLogPath(long chatId, string? explicitPath = null, string? userDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var fileName = $"chatLogs_{chatId}.edb";
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(userDir))
        {
            roots.Add(userDir);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var usersRoot = Path.Combine(localAppData, "Kakao", "KakaoTalk", "users");
                if (Directory.Exists(usersRoot))
                {
                    roots.AddRange(Directory.GetDirectories(usersRoot));
                }
            }
        }

        var candidates = roots
            .Select(root => Path.Combine(root, "chat_data", fileName))
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new FileNotFoundException(
                $"Could not find {fileName}. Pass --edb PATH or --user-dir PATH.",
                fileName
            );
        }

        return candidates[0].FullName;
    }

    public static string FindUserIdByHeader(
        string encryptedPath,
        string pragma,
        long startInclusive,
        long endInclusive,
        CancellationToken cancellationToken = default
    )
    {
        if (startInclusive < 0 || endInclusive < startInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(startInclusive), "Invalid user id search range.");
        }

        var header = ReadPrefix(encryptedPath, 16);
        for (var current = startInclusive; current <= endInclusive; current++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var userId = current.ToString("D9");
            foreach (var mode in new[] { KeyMode.RepeatTo512, KeyMode.DirectConcat })
            {
                var (key, iv) = DeriveKey(pragma, userId, mode);
                var decrypted = DecryptBlock(header, key, iv);
                if (decrypted.AsSpan().SequenceEqual(SqliteHeader))
                {
                    return userId;
                }
            }
        }

        throw new InvalidOperationException(
            $"No user id in range {startInclusive}..{endInclusive} decrypted the SQLite header."
        );
    }

    public static bool CanDecryptHeader(string encryptedPath, string pragma, string userId)
    {
        try
        {
            _ = FindWorkingKey(encryptedPath, pragma, userId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<ChatLogHeaderAttempt> DescribeHeaderAttempts(
        string encryptedPath,
        IEnumerable<string> pragmas,
        IEnumerable<string> userIds
    )
    {
        var header = ReadPrefix(encryptedPath, 16);
        var attempts = new List<ChatLogHeaderAttempt>();

        foreach (var pragma in pragmas.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
        {
            foreach (var userId in userIds.SelectMany(ExpandUserIdCandidates).Distinct())
            {
                foreach (var mode in new[] { KeyMode.RepeatTo512, KeyMode.DirectConcat })
                {
                    var (key, iv) = DeriveKey(pragma, userId, mode);
                    var decrypted = DecryptBlock(header, key, iv);
                    attempts.Add(new ChatLogHeaderAttempt(
                        pragma,
                        userId,
                        mode.ToString(),
                        decrypted.AsSpan().SequenceEqual(SqliteHeader),
                        Convert.ToHexString(decrypted)
                    ));
                }
            }
        }

        return attempts;
    }

    public static IEnumerable<string> ExpandUserIdCandidates(string userId)
    {
        var trimmed = userId.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        yield return trimmed;

        if (trimmed.All(char.IsDigit) && trimmed.Length < 9)
        {
            var padded = trimmed.PadLeft(9, '0');
            if (!string.Equals(trimmed, padded, StringComparison.Ordinal))
            {
                yield return padded;
            }
        }
    }

    private static WorkingKey FindWorkingKey(string encryptedPath, string pragma, string userId)
    {
        var header = ReadPrefix(encryptedPath, 16);
        foreach (var candidateUserId in ExpandUserIdCandidates(userId))
        {
            foreach (var mode in new[] { KeyMode.RepeatTo512, KeyMode.DirectConcat })
            {
                var (key, iv) = DeriveKey(pragma, candidateUserId, mode);
                var decrypted = DecryptBlock(header, key, iv);
                if (decrypted.AsSpan().SequenceEqual(SqliteHeader))
                {
                    return new WorkingKey(candidateUserId, mode.ToString(), key, iv);
                }
            }
        }

        throw new InvalidOperationException(
            "Could not decrypt chat log header. Check --pragma and --user-id. " +
            "If the KakaoTalk encryption format changed, this MVP needs a new key derivation mode."
        );
    }

    private static (byte[] Key, byte[] Iv) DeriveKey(string pragma, string userId, KeyMode mode)
    {
        var material = pragma + userId;
        if (mode == KeyMode.RepeatTo512)
        {
            var builder = new StringBuilder(512);
            while (builder.Length < 512)
            {
                builder.Append(material);
            }
            material = builder.ToString(0, 512);
        }

        var key = MD5.HashData(Encoding.UTF8.GetBytes(material));
        var iv = MD5.HashData(Encoding.ASCII.GetBytes(Convert.ToBase64String(key)));
        return (key, iv);
    }

    private static void DecryptDatabase(string sourcePath, string destinationPath, byte[] key, byte[] iv)
    {
        var tempPath = $"{destinationPath}.tmp";
        var buffer = new byte[4096];

        using var input = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using (var output = File.Create(tempPath))
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (read % 16 != 0)
                {
                    throw new InvalidOperationException(
                        $"Encrypted database block length must be a multiple of 16 bytes; got {read}."
                    );
                }

                var decrypted = DecryptBlock(buffer.AsSpan(0, read), key, iv);
                output.Write(decrypted, 0, decrypted.Length);
            }
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private static byte[] DecryptBlock(ReadOnlySpan<byte> block, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(block.ToArray(), 0, block.Length);
    }

    private static byte[] ReadPrefix(string path, int length)
    {
        var buffer = new byte[length];
        using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var read = input.Read(buffer, 0, buffer.Length);
        if (read != length)
        {
            throw new InvalidOperationException($"Could not read {length} bytes from {path}.");
        }
        return buffer;
    }

    private static string BuildTempPath(string encryptedPath, string userId, string mode)
    {
        var file = new FileInfo(encryptedPath);
        var stamp = $"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}-{userId}-{mode}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.FullName + stamp)))[..16];
        return Path.Combine(Path.GetTempPath(), "kakaocli-win", $"{Path.GetFileNameWithoutExtension(file.Name)}-{hash}.sqlite");
    }

    private sealed record WorkingKey(string UserId, string Mode, byte[] Key, byte[] Iv);

    private enum KeyMode
    {
        RepeatTo512,
        DirectConcat,
    }
}
