using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 基于最近调用时间、额度窗口重置时间和账号级真实刷新窗口判断是否可复用缓存。
/// </summary>
public static class QuotaCachePolicy
{
    /// <summary>
    /// 周窗口的秒数常量。
    /// </summary>
    private const int WeekSeconds = 604_800;

    /// <summary>
    /// 账号最早从 8 小时开始进入分散真实刷新窗口，避免所有账号同时过期。
    /// </summary>
    private static readonly TimeSpan MinScheduledRealRefreshAge = TimeSpan.FromHours(8);

    /// <summary>
    /// 账号最晚在 9 小时 50 分进入真实刷新窗口，给 10 小时硬上限预留缓冲。
    /// </summary>
    private static readonly TimeSpan MaxScheduledRealRefreshAge = TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(50));

    /// <summary>
    /// 计算该账号下一次应执行真实请求的时间点。
    /// </summary>
    public static DateTime? GetScheduledRealRefreshAt(
        CodexQuotaSnapshot? existing,
        string siteId,
        string accountName)
    {
        if (existing is null || existing.RefreshedAt == DateTime.MinValue)
        {
            return null;
        }

        return existing.RefreshedAt + ResolveScheduledRealRefreshAge(siteId, accountName);
    }

    /// <summary>
    /// 判断该账号是否已进入分散真实刷新窗口，命中后必须走真实请求。
    /// </summary>
    public static bool HasReachedScheduledRealRefreshAt(
        CodexQuotaSnapshot? existing,
        string siteId,
        string accountName,
        DateTime nowUtc)
    {
        var scheduledAtUtc = GetScheduledRealRefreshAt(existing, siteId, accountName);
        return !scheduledAtUtc.HasValue || nowUtc >= scheduledAtUtc.Value;
    }

    /// <summary>
    /// 尝试复用已有的额度快照。满足所有条件时返回 true 并输出克隆快照和原因。
    /// 条件：存在有效快照、窗口未过期、额度未重置、无新调用。
    /// </summary>
    public static bool TryReuseQuota(
        CodexQuotaSnapshot? existing,
        string displayAccount,
        bool disabled,
        DateTime nowUtc,
        DateTime? lastUsageAtUtc,
        out CodexQuotaSnapshot? snapshot,
        out string reason)
    {
        snapshot = null;

        // 没有历史缓存则无法复用。
        if (existing is null)
        {
            reason = "无历史额度缓存";
            return false;
        }

        // 上次请求失败的快照不可复用。
        if (!existing.Success)
        {
            reason = "历史额度缓存不可用";
            return false;
        }

        // 没有窗口数据则无法判断有效期。
        if (existing.Windows.Count == 0)
        {
            reason = "历史额度窗口为空";
            return false;
        }

        // 缺少重置时间无法判断是否过期。
        if (!existing.Windows.Any(window => window.ResetAtUtc != DateTime.MinValue))
        {
            reason = "历史额度缺少重置时间";
            return false;
        }

        // 任一窗口已到期则需要重新请求。
        var expiredWindow = existing.Windows.FirstOrDefault(window => window.ResetAtUtc != DateTime.MinValue && window.ResetAtUtc <= nowUtc);
        if (expiredWindow is not null)
        {
            reason = $"额度窗口 {expiredWindow.Label} 已到重置时间";
            return false;
        }

        // 上次刷新后有新调用，说明额度可能已变化。
        if (lastUsageAtUtc.HasValue && lastUsageAtUtc.Value > existing.RefreshedAt)
        {
            reason = "上次额度刷新后存在新的调用记录";
            return false;
        }

        // 全部条件满足，克隆快照并标记缓存原因。
        snapshot = CloneQuota(existing, displayAccount, disabled, nowUtc, lastUsageAtUtc, lastUsageAtUtc.HasValue
            ? "命中调用日志缓存：上次刷新后无新调用，且未到额度重置时间"
            : "命中调用日志缓存：未发现调用记录，且未到额度重置时间");
        reason = snapshot.CacheReason;
        return true;
    }

    /// <summary>
    /// 已禁用且周额度未重置的免费号，直接沿用旧快照，避免本轮重复探测。
    /// </summary>
    public static bool TrySkipDisabledFreeQuota(
        CodexQuotaSnapshot? existing,
        string displayAccount,
        bool disabled,
        int threshold,
        DateTime nowUtc,
        out CodexQuotaSnapshot? snapshot,
        out string reason)
    {
        snapshot = null;

        if (!disabled)
        {
            reason = "账号未禁用";
            return false;
        }

        if (existing is null)
        {
            reason = "无历史额度缓存";
            return false;
        }

        if (!existing.Success)
        {
            reason = "历史额度缓存不可用";
            return false;
        }

        if (!string.Equals(existing.PlanType, "Free", StringComparison.OrdinalIgnoreCase))
        {
            reason = "不是免费套餐";
            return false;
        }

        var weeklyWindow = existing.Windows.FirstOrDefault(window => window.LimitWindowSeconds == WeekSeconds);
        if (weeklyWindow is null)
        {
            reason = "缺少周额度窗口";
            return false;
        }

        if (weeklyWindow.ResetAtUtc == DateTime.MinValue || weeklyWindow.ResetAtUtc <= nowUtc)
        {
            reason = "周额度已到重置时间";
            return false;
        }

        if (!weeklyWindow.UsedPercent.HasValue || weeklyWindow.UsedPercent.Value < threshold)
        {
            reason = "周额度未达到停用阈值";
            return false;
        }

        snapshot = CloneQuota(
            existing,
            displayAccount,
            disabled,
            nowUtc,
            existing.LastUsageAt == DateTime.MinValue ? null : existing.LastUsageAt,
            "命中禁用免费号跳过：周额度未重置，保持禁用");
        reason = snapshot.CacheReason;
        return true;
    }

    /// <summary>
    /// 克隆额度快照并标记为缓存来源，同时保留最后一次真实请求时间。
    /// </summary>
    private static CodexQuotaSnapshot CloneQuota(
        CodexQuotaSnapshot existing,
        string displayAccount,
        bool disabled,
        DateTime checkedAtUtc,
        DateTime? lastUsageAtUtc,
        string cacheReason)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = existing.AccountName,
            DisplayAccount = displayAccount,
            PlanType = existing.PlanType,
            Disabled = disabled,
            CheckedAt = checkedAtUtc,
            RefreshedAt = existing.RefreshedAt,
            StatusCode = existing.StatusCode,
            Success = existing.Success,
            ErrorMessage = existing.ErrorMessage,
            FromCache = true,
            CacheReason = cacheReason,
            LastUsageAt = lastUsageAtUtc ?? existing.LastUsageAt,
            Windows = existing.Windows.Select(window => new CodexQuotaWindowSnapshot
            {
                Id = window.Id,
                Label = window.Label,
                UsedPercent = window.UsedPercent,
                ResetLabel = window.ResetLabel,
                LimitWindowSeconds = window.LimitWindowSeconds,
                ResetAtUtc = window.ResetAtUtc,
            }).ToList(),
        };
    }

    /// <summary>
    /// 为站点内账号计算稳定的分散真实刷新时间，避免同一批账号同时过期。
    /// </summary>
    private static TimeSpan ResolveScheduledRealRefreshAge(string siteId, string accountName)
    {
        var spreadMinutes = (int)(MaxScheduledRealRefreshAge - MinScheduledRealRefreshAge).TotalMinutes;
        var stableKey = $"{siteId.Trim().ToLowerInvariant()}\n{accountName.Trim().ToLowerInvariant()}";
        var offsetMinutes = ComputeStableBucket(stableKey, spreadMinutes + 1);
        return MinScheduledRealRefreshAge + TimeSpan.FromMinutes(offsetMinutes);
    }

    /// <summary>
    /// 使用稳定哈希将账号映射到固定桶位，保证重启后分散结果不漂移。
    /// </summary>
    private static int ComputeStableBucket(string value, int modulo)
    {
        if (modulo <= 1)
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash % (uint)modulo);
        }
    }
}
