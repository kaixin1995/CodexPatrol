using System.Text.Json;
using CodexPatrol.Models;
using CodexPatrol.Serialization;

namespace CodexPatrol.Services;

/// <summary>
/// Codex 额度解析器，迁移自 src/utils/quota/codexQuota.ts
/// 区分 5 小时窗口 (18000s) 和周窗口 (604800s)
/// </summary>
public static class CodexQuotaParser
{
    /// <summary>
    /// 5 小时窗口的秒数常量（18000 秒）。
    /// </summary>
    private const int FiveHourSeconds = 18_000;

    /// <summary>
    /// 周窗口的秒数常量（604800 秒）。
    /// </summary>
    private const int WeekSeconds = 604_800;

    /// <summary>
    /// 从窗口对象中读取 limit_window_seconds 字段。
    /// </summary>
    private static double? GetWindowSeconds(CodexUsageWindow? window)
    {
        if (window is null) return null;
        return NormalizeDouble(window.Limit_Window_Seconds ?? window.LimitWindowSeconds);
    }

    /// <summary>
    /// 从窗口对象中读取 used_percent 字段。
    /// </summary>
    private static double? GetUsedPercent(CodexUsageWindow? window)
    {
        if (window is null) return null;
        return NormalizeDouble(window.Used_Percent ?? window.UsedPercent);
    }

    /// <summary>
    /// 将 NaN/Infinity 等非法浮点值规范化为 null。
    /// </summary>
    private static double? NormalizeDouble(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value : null;

    /// <summary>
    /// 分类 primary/secondary 窗口为 5 小时和周窗口
    /// </summary>
    private static (CodexUsageWindow? fiveHour, CodexUsageWindow? weekly) ClassifyWindows(
        CodexRateLimitInfo? limitInfo)
    {
        var primary = limitInfo?.Primary_Window ?? limitInfo?.PrimaryWindow;
        var secondary = limitInfo?.Secondary_Window ?? limitInfo?.SecondaryWindow;

        CodexUsageWindow? fiveHour = null;
        CodexUsageWindow? weekly = null;

        foreach (var window in new[] { primary, secondary })
        {
            if (window is null) continue;
            var seconds = GetWindowSeconds(window);
            if (seconds == FiveHourSeconds && fiveHour is null)
                fiveHour = window;
            else if (seconds == WeekSeconds && weekly is null)
                weekly = window;
        }

        // 回退：按顺序假设 primary=5h, secondary=weekly
        if (fiveHour is null && primary is not null && primary != weekly)
            fiveHour = primary;
        if (weekly is null && secondary is not null && secondary != fiveHour)
            weekly = secondary;

        return (fiveHour, weekly);
    }

    /// <summary>
    /// 构建重置时间标签
    /// </summary>
    private static string BuildResetLabel(CodexUsageWindow? window)
    {
        if (window is null) return "-";

        var resetAtUtc = ResolveResetAtUtc(window);
        if (resetAtUtc == DateTime.MinValue)
        {
            return "-";
        }

        var remaining = resetAtUtc - DateTime.UtcNow;
        if (remaining.TotalSeconds > 0)
            return FormatDuration(remaining.TotalSeconds);
        return "已重置";
    }

    /// <summary>
    /// 将秒数格式化为中文可读的时长文本，如 "2天3小时后重置"。
    /// </summary>
    private static string FormatDuration(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days}天");
        if (span.Hours > 0) parts.Add($"{span.Hours}小时");
        if (span.Minutes > 0) parts.Add($"{span.Minutes}分");
        return parts.Count > 0 ? string.Join("", parts) + "后重置" : "<1分钟后重置";
    }

    /// <summary>
    /// 根据窗口的 reset_at 或 reset_after_seconds 计算重置时间（UTC）。
    /// 优先使用绝对时间戳，其次使用相对秒数。
    /// </summary>
    private static DateTime ResolveResetAtUtc(CodexUsageWindow? window)
    {
        if (window is null) return DateTime.MinValue;

        var resetAt = NormalizeDouble(window.Reset_At ?? window.ResetAt);
        if (resetAt.HasValue && resetAt.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)resetAt.Value).UtcDateTime;
        }

        var resetAfter = NormalizeDouble(window.Reset_After_Seconds ?? window.ResetAfterSeconds);
        if (resetAfter.HasValue && resetAfter.Value > 0)
        {
            return DateTime.UtcNow.AddSeconds(resetAfter.Value);
        }

        return DateTime.MinValue;
    }

    /// <summary>
    /// 根据单个窗口数据构建窗口快照。
    /// </summary>
    private static CodexQuotaWindowSnapshot BuildWindowSnapshot(
        string id, string label, CodexUsageWindow? window,
        bool? limitReached, bool? allowed)
    {
        if (window is null)
        {
            return new CodexQuotaWindowSnapshot { Id = id, Label = label };
        }

        var isLimitReached = limitReached == true || allowed == false;
        var usedPercent = GetUsedPercent(window)
                          ?? (isLimitReached ? 100 : (double?)null);

        return new CodexQuotaWindowSnapshot
        {
            Id = id,
            Label = label,
            UsedPercent = usedPercent,
            ResetLabel = BuildResetLabel(window),
            LimitWindowSeconds = GetWindowSeconds(window),
            ResetAtUtc = ResolveResetAtUtc(window),
        };
    }

    /// <summary>
    /// 从 Codex usage 响应体解析额度快照
    /// </summary>
    public static CodexQuotaSnapshot ParseQuotaSnapshot(
        string accountName,
        string displayAccount,
        bool disabled,
        int statusCode,
        string rawBody)
    {
        var snapshot = new CodexQuotaSnapshot
        {
            AccountName = accountName,
            DisplayAccount = displayAccount,
            Disabled = disabled,
            StatusCode = statusCode,
            RefreshedAt = DateTime.UtcNow,
            Success = statusCode is >= 200 and < 300,
        };

        if (statusCode is < 200 or >= 300)
        {
            snapshot.ErrorMessage = ExtractErrorMessage(rawBody);
            return snapshot;
        }

        CodexUsagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize(rawBody, AppJsonContext.Default.CodexUsagePayload);
        }
        catch
        {
            snapshot.Success = false;
            snapshot.ErrorMessage = "响应 JSON 解析失败";
            return snapshot;
        }

        if (payload is null)
        {
            snapshot.Success = false;
            snapshot.ErrorMessage = "响应为空";
            return snapshot;
        }

        // 套餐类型
        snapshot.PlanType = NormalizePlanType(payload.Plan_Type ?? payload.PlanType);

        // 主额度窗口
        var rateLimit = payload.Rate_Limit ?? payload.RateLimit;
        if (rateLimit is not null)
        {
            var classified = ClassifyWindows(rateLimit);
            var limitReached = rateLimit.Limit_Reached ?? rateLimit.LimitReached;
            var allowed = rateLimit.Allowed;

            if (classified.fiveHour is not null)
                snapshot.Windows.Add(BuildWindowSnapshot("five-hour", "5 小时限额", classified.fiveHour, limitReached, allowed));
            if (classified.weekly is not null)
                snapshot.Windows.Add(BuildWindowSnapshot("weekly", "周限额", classified.weekly, limitReached, allowed));
        }

        // 代码审查额度
        var codeReviewLimit = payload.Code_Review_Rate_Limit ?? payload.CodeReviewRateLimit;
        if (codeReviewLimit is not null)
        {
            var classified = ClassifyWindows(codeReviewLimit);
            var limitReached = codeReviewLimit.Limit_Reached ?? codeReviewLimit.LimitReached;
            var allowed = codeReviewLimit.Allowed;

            if (classified.fiveHour is not null)
                snapshot.Windows.Add(BuildWindowSnapshot("code-review-five-hour", "代码审查 5 小时限额", classified.fiveHour, limitReached, allowed));
            if (classified.weekly is not null)
                snapshot.Windows.Add(BuildWindowSnapshot("code-review-weekly", "代码审查周限额", classified.weekly, limitReached, allowed));
        }

        // 额外限额
        var additional = payload.Additional_Rate_Limits ?? payload.AdditionalRateLimits;
        if (additional is { Count: > 0 })
        {
            for (var i = 0; i < additional.Count; i++)
            {
                var item = additional[i];
                var rateInfo = item?.Rate_Limit ?? item?.RateLimit;
                if (rateInfo is null) continue;

                var limitName = item?.Limit_Name ?? item?.LimitName
                                ?? item?.Metered_Feature ?? item?.MeteredFeature
                                ?? $"additional-{i + 1}";

                var classified = ClassifyWindows(rateInfo);
                var limitReached = rateInfo.Limit_Reached ?? rateInfo.LimitReached;
                var allowed = rateInfo.Allowed;

                if (classified.fiveHour is not null)
                    snapshot.Windows.Add(BuildWindowSnapshot($"additional-five-hour-{i}", $"{limitName} 5 小时限额", classified.fiveHour, limitReached, allowed));
                if (classified.weekly is not null)
                    snapshot.Windows.Add(BuildWindowSnapshot($"additional-weekly-{i}", $"{limitName} 周限额", classified.weekly, limitReached, allowed));
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 获取周额度的使用百分比
    /// </summary>
    public static double? GetWeeklyUsedPercent(CodexQuotaSnapshot snapshot)
    {
        return snapshot.Windows
            .Where(w => w.LimitWindowSeconds == WeekSeconds)
            .Select(w => w.UsedPercent)
            .FirstOrDefault();
    }

    /// <summary>
    /// 获取 5 小时额度的使用百分比
    /// </summary>
    public static double? GetFiveHourUsedPercent(CodexQuotaSnapshot snapshot)
    {
        return snapshot.Windows
            .Where(w => w.LimitWindowSeconds == FiveHourSeconds)
            .Select(w => w.UsedPercent)
            .FirstOrDefault();
    }

    /// <summary>
    /// 判断额度是否已达到限制
    /// </summary>
    public static bool IsQuotaReached(CodexQuotaSnapshot snapshot)
    {
        return snapshot.Windows.Any(w => w.UsedPercent >= 100);
    }

    /// <summary>
    /// 将套餐类型字符串统一为首字母大写的标准格式。
    /// </summary>
    private static string NormalizePlanType(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "plus" => "Plus",
            "team" => "Team",
            "free" => "Free",
            "pro" => "Pro",
            "prolite" or "pro_lite" or "pro-lite" => "ProLite",
            _ => raw?.Trim() ?? "Unknown"
        };
    }

    /// <summary>
    /// 从 JSON 响应体中提取错误信息，优先取 error.message 字段。
    /// </summary>
    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorEl))
            {
                if (errorEl.ValueKind == JsonValueKind.String)
                    return errorEl.GetString() ?? body;
                if (errorEl.ValueKind == JsonValueKind.Object && errorEl.TryGetProperty("message", out var msgEl))
                    return msgEl.GetString() ?? body;
            }

            if (root.TryGetProperty("message", out var messageEl))
                return messageEl.GetString() ?? body;
        }
        catch { }

        return body.Length > 200 ? body[..200] : body;
    }
}
