using System.Text.Json;
using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Core.Utilities;

namespace KakaoCli.Win.Data;

public sealed class DryRunMessageSink : IMessageSink
{
    private readonly TextWriter _writer;

    public DryRunMessageSink(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    public Task<bool> SendAsync(MonitorPayload payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOutput.Options);
        _writer.WriteLine(json);
        return Task.FromResult(true);
    }
}
