using System.Text.Json;
using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class ProbeArtifactStore
{
    private readonly string _repoRoot;
    private readonly string _probeDir;
    private readonly string _fixtureDir;

    public ProbeArtifactStore(string? repoRoot = null)
    {
        _repoRoot = repoRoot ?? ResolveRepositoryRoot();
        _probeDir = Path.Combine(_repoRoot, ".omx", "probes", "windows-standalone");
        _fixtureDir = Path.Combine(_repoRoot, ".omx", "fixtures", "windows-parity");
    }

    public string ProbeDirectory => _probeDir;
    public string FixtureDirectory => _fixtureDir;

    public bool ProbeExists(string fileName) => File.Exists(Path.Combine(_probeDir, fileName));

    public string? ReadProbeText(string fileName)
    {
        var path = Path.Combine(_probeDir, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<ChatRecord> LoadChatsFixture()
    {
        return LoadFixture<List<ChatRecord>>("chats.json") ?? [];
    }

    public IReadOnlyList<MessageRecord> LoadMessagesFixture()
    {
        return LoadFixture<List<MessageRecord>>("messages.json") ?? [];
    }

    public IReadOnlyList<MessageRecord> LoadSearchFixture()
    {
        return LoadFixture<List<MessageRecord>>("search.json") ?? [];
    }

    public IReadOnlyList<SyncEvent> LoadSyncFixture()
    {
        return LoadFixture<List<SyncEvent>>("sync.ndjson.json") ?? [];
    }

    private T? LoadFixture<T>(string fileName)
    {
        var path = Path.Combine(_fixtureDir, fileName);
        if (!File.Exists(path))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }

    private static string ResolveRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
