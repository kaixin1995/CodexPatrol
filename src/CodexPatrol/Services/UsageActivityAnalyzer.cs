using System.Text.Json;

namespace CodexPatrol.Services;

/// <summary>
/// 从 CPA usage 数据中提取各账号最近一次调用时间。
/// </summary>
public static class UsageActivityAnalyzer
{
    /// <summary>
    /// 从原始 usage JSON 中提取每个 auth_index 对应的最近一次调用时间。
    /// </summary>
    public static Dictionary<string, DateTime> BuildLastUsageByAuthIndex(string rawJson)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            CollectLastUsage(document.RootElement, result);
        }
        catch
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// 递归遍历 JSON 结构，收集所有包含 auth_index 和 timestamp 的记录。
    /// 对于同一个 auth_index，保留最晚的时间戳。
    /// </summary>
    private static void CollectLastUsage(JsonElement element, Dictionary<string, DateTime> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var authIndex = ReadString(element, "auth_index", "authIndex", "AuthIndex").Trim();
                var timestamp = ReadString(element, "timestamp").Trim();
                if (!string.IsNullOrWhiteSpace(authIndex)
                    && !string.IsNullOrWhiteSpace(timestamp)
                    && DateTimeOffset.TryParse(timestamp, out var occurredAt))
                {
                    var occurredAtUtc = occurredAt.UtcDateTime;
                    if (!result.TryGetValue(authIndex, out var existing) || occurredAtUtc > existing)
                    {
                        result[authIndex] = occurredAtUtc;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectLastUsage(property.Value, result);
                }

                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectLastUsage(item, result);
                }
                break;
        }
    }

    /// <summary>
    /// 按多个候选键名依次尝试读取字符串值，兼容不同命名风格。
    /// </summary>
    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
            }
        }

        return "";
    }
}
