using System.Text.Json;
using System.Text.Json.Serialization;

namespace KakaoCli.Win.Core.Utilities;

public static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    public static void Write(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
    }
}
