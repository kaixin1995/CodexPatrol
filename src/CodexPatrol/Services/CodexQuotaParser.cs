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
        var directWindows = limitInfo?.Windows ?? [];

        CodexUsageWindow? fiveHour = null;
        CodexUsageWindow? weekly = null;

        foreach (var window in new[] { primary, secondary }.Concat(directWindows))
        {
            if (window is null) continue;
            var seconds = GetWindowSeconds(window);
            if (seconds == FiveHourSeconds && fiveHour is null)
                fiveHour = window;
            else if (seconds == WeekSeconds && weekly is null)
                weekly = window;
        }

        // 仅在缺少 limit_window_seconds 时，才按顺序回退推断窗口类型，避免把长周期窗口误标为 5 小时窗口。
        if (fiveHour is null && primary is not null && primary != weekly && !GetWindowSeconds(primary).HasValue)
            fiveHour = primary;
        if (weekly is null && secondary is not null && secondary != fiveHour && !GetWindowSeconds(secondary).HasValue)
            weekly = secondary;

        // 兜底：如果只有直接 windows 且仍未识别出标准窗口，则保留第一个有效窗口为主窗口，避免成功响应却完全无窗口可展示。
        if (fiveHour is null && weekly is null)
        {
            var fallbackWindow = directWindows.FirstOrDefault(window => window is not null);
            if (fallbackWindow is not null)
            {
                fiveHour = fallbackWindow;
            }
        }

        return (fiveHour, weekly);
    }

    /// <summary>
    /// 将窗口秒数格式化为可读标签。
    /// </summary>
    private static string FormatWindowLabel(double? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0)
        {
            return "额度窗口";
        }

        if (Math.Abs(seconds.Value - FiveHourSeconds) < 0.1)
        {
            return "5 小时限额";
        }

        if (Math.Abs(seconds.Value - WeekSeconds) < 0.1)
        {
            return "周限额";
        }

        if (Math.Abs(seconds.Value - 2_592_000) < 0.1)
        {
            return "月限额";
        }

        var span = TimeSpan.FromSeconds(seconds.Value);
        if (span.TotalDays >= 1)
        {
            var days = Math.Round(span.TotalDays, 1);
            return days % 1 == 0 ? $"{days:0} 天限额" : $"{days:0.#} 天限额";
        }

        if (span.TotalHours >= 1)
        {
            var hours = Math.Round(span.TotalHours, 1);
            return hours % 1 == 0 ? $"{hours:0} 小时限额" : $"{hours:0.#} 小时限额";
        }

        return $"{Math.Round(seconds.Value)} 秒限额";
    }

    /// <summary>
    /// 构建重置时间标签
    /// </summary>
    private static string BuildResetLabel(CodexUsageWindow? window)
    {
        if (window is null) return "-";
        return FormatResetLabel(ResolveResetAtUtc(window));
    }

    /// <summary>
    /// 根据绝对重置时间生成可读标签，供运行时输出和前端展示复用。
    /// </summary>
    public static string FormatResetLabel(DateTime resetAtUtc, DateTime? nowUtc = null)
    {
        if (resetAtUtc == DateTime.MinValue)
        {
            return "-";
        }

        var remaining = resetAtUtc - (nowUtc ?? DateTime.UtcNow);
        if (remaining.TotalSeconds > 0)
        {
            return FormatDuration(remaining.TotalSeconds);
        }

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
    /// 将限额信息中的所有有效窗口转成快照。
    /// </summary>
    private static List<CodexQuotaWindowSnapshot> BuildAllWindowSnapshots(
        string idPrefix,
        string labelPrefix,
        CodexRateLimitInfo limitInfo)
    {
        var windows = new List<CodexQuotaWindowSnapshot>();
        var limitReached = limitInfo.Limit_Reached ?? limitInfo.LimitReached;
        var allowed = limitInfo.Allowed;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddWindow(string slotName, CodexUsageWindow? window)
        {
            if (window is null)
            {
                return;
            }

            var seconds = GetWindowSeconds(window);
            var usedPercent = GetUsedPercent(window);
            var resetAtUtc = ResolveResetAtUtc(window);
            var key = $"{seconds:0.###}|{usedPercent:0.###}|{resetAtUtc:o}";
            if (!seenKeys.Add(key))
            {
                return;
            }

            var label = string.IsNullOrWhiteSpace(labelPrefix)
                ? FormatWindowLabel(seconds)
                : $"{labelPrefix} {FormatWindowLabel(seconds)}";
            windows.Add(BuildWindowSnapshot($"{idPrefix}-{slotName}-{windows.Count}", label, window, limitReached, allowed));
        }

        TryAddWindow("primary", limitInfo.Primary_Window ?? limitInfo.PrimaryWindow);
        TryAddWindow("secondary", limitInfo.Secondary_Window ?? limitInfo.SecondaryWindow);
        foreach (var window in limitInfo.Windows ?? [])
        {
            TryAddWindow("list", window);
        }

        return windows;
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
        var nowUtc = DateTime.UtcNow;
        var snapshot = new CodexQuotaSnapshot
        {
            AccountName = accountName,
            DisplayAccount = displayAccount,
            Disabled = disabled,
            StatusCode = statusCode,
            CheckedAt = nowUtc,
            RefreshedAt = nowUtc,
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

        // 主额度窗口：只要是有效窗口都收进快照，标签按窗口时长动态生成。
        var rateLimit = payload.Rate_Limit ?? payload.RateLimit;
        if (rateLimit is not null)
        {
            snapshot.Windows.AddRange(BuildAllWindowSnapshots("rate-limit", string.Empty, rateLimit));
        }

        // 代码审查额度
        var codeReviewLimit = payload.Code_Review_Rate_Limit ?? payload.CodeReviewRateLimit;
        if (codeReviewLimit is not null)
        {
            snapshot.Windows.AddRange(BuildAllWindowSnapshots("code-review", "代码审查", codeReviewLimit));
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

                snapshot.Windows.AddRange(BuildAllWindowSnapshots($"additional-{i}", limitName, rateInfo));
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 获取动态生效的额度窗口，按窗口时长从长到短排序。
    /// </summary>
    public static List<CodexQuotaWindowSnapshot> GetEffectiveWindows(CodexQuotaSnapshot snapshot)
    {
        return snapshot.Windows
            .Where(window => window.UsedPercent.HasValue && window.LimitWindowSeconds > 0)
            .OrderByDescending(window => window.LimitWindowSeconds)
            .ThenBy(window => window.Label ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取动态生效额度窗口中的最高使用率。
    /// </summary>
    public static double? GetPrimaryUsedPercent(CodexQuotaSnapshot snapshot)
    {
        return GetEffectiveWindows(snapshot)
            .Select(window => window.UsedPercent)
            .LastOrDefault();
    }

    /// <summary>
    /// 判断当前额度窗口中是否有任意一个达到阈值。
    /// </summary>
    public static bool HasAnyWindowReachedThreshold(CodexQuotaSnapshot snapshot, int threshold)
    {
        return GetEffectiveWindows(snapshot)
            .Any(window => window.UsedPercent.HasValue && window.UsedPercent.Value >= threshold);
    }

    /// <summary>
    /// 判断当前额度窗口中是否所有窗口都低于阈值。
    /// </summary>
    public static bool AreAllEffectiveWindowsBelowThreshold(CodexQuotaSnapshot snapshot, int threshold)
    {
        var windows = GetEffectiveWindows(snapshot);
        return windows.Count > 0 && windows.All(window => window.UsedPercent!.Value < threshold);
    }

    /// <summary>
    /// 获取当前达到阈值且尚未到重置时间的额度窗口。
    /// </summary>
    public static List<CodexQuotaWindowSnapshot> GetReachedWindows(CodexQuotaSnapshot snapshot, int threshold, DateTime nowUtc)
    {
        return GetEffectiveWindows(snapshot)
            .Where(window => window.ResetAtUtc != DateTime.MinValue
                && window.ResetAtUtc > nowUtc
                && window.UsedPercent!.Value >= threshold)
            .ToList();
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
