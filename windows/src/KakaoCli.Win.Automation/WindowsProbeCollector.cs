using System.Runtime.InteropServices;
using System.Text.Json;
using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Data;

namespace KakaoCli.Win.Automation;

public sealed class WindowsProbeCollector
{
    private readonly ProbeArtifactStore _artifacts;

    public WindowsProbeCollector(ProbeArtifactStore artifacts)
    {
        _artifacts = artifacts;
    }

    public ProbeSummary CollectSummary()
    {
        var notes = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            notes.Add("Probe collector is running outside Windows; returned statuses are advisory only.");
        }

        var installPath = DetectInstallPath(notes);
        var dataPath = DetectDataPath(notes);
        var dbPath = DetectDatabasePath(notes);
        var uiAutomation = OperatingSystem.IsWindows() ? ProbeStatus.Unknown : ProbeStatus.Missing;

        return new ProbeSummary(installPath, dataPath, dbPath, uiAutomation, notes);
    }

    public void WriteArtifacts()
    {
        Directory.CreateDirectory(_artifacts.ProbeDirectory);
        var summary = CollectSummary();

        var environmentPath = Path.Combine(_artifacts.ProbeDirectory, "environment.md");
        var pathsPath = Path.Combine(_artifacts.ProbeDirectory, "paths.json");
        var verdictPath = Path.Combine(_artifacts.ProbeDirectory, "probe-verdict.md");

        File.WriteAllText(environmentPath, BuildEnvironmentMarkdown(summary));
        File.WriteAllText(pathsPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        if (!File.Exists(verdictPath))
        {
            File.WriteAllText(verdictPath, """
# Probe Verdict

- recommended_path: undecided
- db_access: unverified
- login_automation: unverified
- send_automation: unverified
- harvest_automation: unverified

## Evidence

- Fill in after running the probe on a real Windows KakaoTalk installation.
""");
        }
    }

    private static ProbeStatus DetectInstallPath(List<string> notes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ProbeStatus.Missing;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Kakao", "KakaoTalk", "KakaoTalk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Kakao", "KakaoTalk", "KakaoTalk.exe"),
        };

        if (candidates.Any(File.Exists))
        {
            return ProbeStatus.Detected;
        }

        notes.Add("KakaoTalk.exe was not found in standard Program Files locations.");
        return ProbeStatus.Unknown;
    }

    private static ProbeStatus DetectDataPath(List<string> notes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ProbeStatus.Missing;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            notes.Add("LocalApplicationData was empty.");
            return ProbeStatus.Unknown;
        }

        return ProbeStatus.Detected;
    }

    private static ProbeStatus DetectDatabasePath(List<string> notes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ProbeStatus.Missing;
        }

        notes.Add("Database path detection is still heuristic and must be filled in by live probe evidence.");
        return ProbeStatus.Unknown;
    }

    private static string BuildEnvironmentMarkdown(ProbeSummary summary)
    {
        return $"""
# Windows Probe Environment

- generated_at_utc: {DateTimeOffset.UtcNow:O}
- process_architecture: {RuntimeInformation.ProcessArchitecture}
- os_description: {RuntimeInformation.OSDescription}
- install_path_status: {summary.InstallPath}
- data_path_status: {summary.DataPath}
- database_path_status: {summary.DatabasePath}
- ui_automation_status: {summary.UiAutomation}

## Notes

{string.Join(Environment.NewLine, summary.Notes.Select(static note => $"- {note}"))}
""";
    }
}
