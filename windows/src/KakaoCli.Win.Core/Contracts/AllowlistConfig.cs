using System.Text.Json;

namespace KakaoCli.Win.Core.Contracts;

public sealed record AllowlistedChat(long ChatId, string Label);

public sealed record SlackMonitorConfig(
    IReadOnlyList<AllowlistedChat> Allowlist,
    string SlackWebhookUrlEnv,
    string CursorPath,
    int PollIntervalSeconds,
    int MaxTextChars
);

public sealed record ConfigValidationResult(
    bool IsValid,
    SlackMonitorConfig? Config,
    IReadOnlyList<string> Errors
);

public static class AllowlistConfig
{
    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "allowlist",
        "slack",
        "cursor_path",
        "poll_interval_seconds",
        "max_text_chars",
    };

    private static readonly HashSet<string> AllowedAllowlistProperties = new(StringComparer.Ordinal)
    {
        "chat_id",
        "label",
    };

    private static readonly HashSet<string> AllowedSlackProperties = new(StringComparer.Ordinal)
    {
        "webhook_url_env",
    };

    public static ConfigValidationResult LoadAndValidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid("Missing --config path.");
        }

        if (!File.Exists(path))
        {
            return Invalid($"Config file not found: {path}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Validate(document.RootElement);
        }
        catch (JsonException error)
        {
            return Invalid($"Malformed JSON: {error.Message}");
        }
        catch (IOException error)
        {
            return Invalid($"Could not read config: {error.Message}");
        }
    }

    public static ConfigValidationResult Validate(JsonElement root)
    {
        var errors = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Config root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!AllowedRootProperties.Contains(property.Name))
            {
                errors.Add($"Unknown root property '{property.Name}' is not allowed.");
            }
        }

        if (!root.TryGetProperty("allowlist", out var allowlistElement))
        {
            errors.Add("Missing required 'allowlist' array.");
        }
        else if (allowlistElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'allowlist' must be an array.");
        }

        var chats = new List<AllowlistedChat>();
        var seenIds = new HashSet<long>();
        if (allowlistElement.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var entry in allowlistElement.EnumerateArray())
            {
                ValidateAllowlistEntry(entry, index, chats, seenIds, errors);
                index++;
            }

            if (index == 0)
            {
                errors.Add("'allowlist' must contain at least one chat.");
            }
        }

        var slackEnv = "KAKAOCLI_SLACK_WEBHOOK_URL";
        if (root.TryGetProperty("slack", out var slackElement))
        {
            if (slackElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add("'slack' must be an object.");
            }
            else
            {
                foreach (var property in slackElement.EnumerateObject())
                {
                    if (!AllowedSlackProperties.Contains(property.Name))
                    {
                        errors.Add($"Unknown slack property '{property.Name}' is not allowed.");
                    }
                }

                if (slackElement.TryGetProperty("webhook_url_env", out var envElement))
                {
                    if (envElement.ValueKind != JsonValueKind.String)
                    {
                        errors.Add("'slack.webhook_url_env' must be a string.");
                    }
                    else if (!string.IsNullOrWhiteSpace(envElement.GetString()))
                    {
                        slackEnv = envElement.GetString()!.Trim();
                    }
                }
            }
        }

        var cursorPath = ".kakaocli/windows-slack-cursors.json";
        if (root.TryGetProperty("cursor_path", out var cursorElement))
        {
            if (cursorElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cursorElement.GetString()))
            {
                errors.Add("'cursor_path' must be a nonblank string.");
            }
            else
            {
                cursorPath = cursorElement.GetString()!.Trim();
            }
        }

        var pollIntervalSeconds = 2;
        if (root.TryGetProperty("poll_interval_seconds", out var intervalElement))
        {
            if (!intervalElement.TryGetInt32(out pollIntervalSeconds) || pollIntervalSeconds <= 0)
            {
                errors.Add("'poll_interval_seconds' must be a positive integer.");
            }
        }

        var maxTextChars = 160;
        if (root.TryGetProperty("max_text_chars", out var maxTextElement))
        {
            if (!maxTextElement.TryGetInt32(out maxTextChars) || maxTextChars <= 0)
            {
                errors.Add("'max_text_chars' must be a positive integer.");
            }
        }

        if (errors.Count > 0)
        {
            return new ConfigValidationResult(false, null, errors);
        }

        return new ConfigValidationResult(
            true,
            new SlackMonitorConfig(chats, slackEnv, cursorPath, pollIntervalSeconds, maxTextChars),
            []
        );
    }

    private static void ValidateAllowlistEntry(
        JsonElement entry,
        int index,
        List<AllowlistedChat> chats,
        HashSet<long> seenIds,
        List<string> errors
    )
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"allowlist[{index}] must be an object.");
            return;
        }

        foreach (var property in entry.EnumerateObject())
        {
            if (!AllowedAllowlistProperties.Contains(property.Name))
            {
                errors.Add($"allowlist[{index}] has unknown property '{property.Name}'. Only chat_id and label are allowed.");
            }
        }

        if (!entry.TryGetProperty("chat_id", out var chatIdElement))
        {
            errors.Add($"allowlist[{index}] is missing required chat_id.");
            return;
        }

        if (chatIdElement.ValueKind != JsonValueKind.Number ||
            !chatIdElement.TryGetInt64(out var chatId) ||
            chatId <= 0)
        {
            errors.Add($"allowlist[{index}].chat_id must be a positive number.");
            return;
        }

        if (!entry.TryGetProperty("label", out var labelElement) ||
            labelElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(labelElement.GetString()))
        {
            errors.Add($"allowlist[{index}].label must be a nonblank string.");
            return;
        }

        if (!seenIds.Add(chatId))
        {
            errors.Add($"allowlist[{index}].chat_id duplicates {chatId}.");
            return;
        }

        chats.Add(new AllowlistedChat(chatId, labelElement.GetString()!.Trim()));
    }

    private static ConfigValidationResult Invalid(string error)
    {
        return new ConfigValidationResult(false, null, [error]);
    }
}
