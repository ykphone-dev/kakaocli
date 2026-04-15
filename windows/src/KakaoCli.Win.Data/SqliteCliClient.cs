using System.Diagnostics;
using System.Text.Json;

namespace KakaoCli.Win.Data;

public sealed class SqliteCliClient
{
    private readonly string _sqlitePath;

    public SqliteCliClient(string? sqlitePath = null)
    {
        _sqlitePath = string.IsNullOrWhiteSpace(sqlitePath) ? "sqlite3" : sqlitePath;
    }

    public async Task<JsonDocument> QueryJsonAsync(
        string databasePath,
        string sql,
        CancellationToken cancellationToken = default
    )
    {
        var output = await RunAsync(databasePath, sql, cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            output = "[]";
        }

        return JsonDocument.Parse(output);
    }

    private async Task<string> RunAsync(string databasePath, string sql, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _sqlitePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-readonly");
        startInfo.ArgumentList.Add("-json");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add(sql);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "sqlite3 CLI is required for the Windows reader MVP. " +
                "Install sqlite3 or pass --sqlite PATH to sqlite3.exe.",
                error
            );
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sqlite3 failed with exit code {process.ExitCode}: {stderr.Trim()}"
            );
        }

        return stdout;
    }
}
