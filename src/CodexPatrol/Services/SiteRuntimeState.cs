using System.Collections.Concurrent;
using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 单个站点的运行时状态，包含配置、额度、账号、日志等所有运行时数据。
/// </summary>
internal sealed class SiteRuntimeState
{
    /// <summary>
    /// 站点配置。
    /// </summary>
    public PatrolSiteSettings Settings { get; set; } = new();

    /// <summary>
    /// 例外账号名单（不参与巡检）。
    /// </summary>
    public HashSet<string> Exceptions { get; } = [];

    /// <summary>
    /// 账号额度快照，以账号名为键。
    /// </summary>
    public ConcurrentDictionary<string, CodexQuotaSnapshot> Quotas { get; } = new();

    /// <summary>
    /// 账号列表，以账号名为键。
    /// </summary>
    public ConcurrentDictionary<string, AuthFileItem> Accounts { get; } = new();

    /// <summary>
    /// 操作日志队列。
    /// </summary>
    public ConcurrentQueue<OperationLogEntry> OperationLogs { get; } = new();

    /// <summary>
    /// 最近一次巡检结果。
    /// </summary>
    public InspectionRunResult? LastRun { get; set; }

    /// <summary>
    /// 当前巡检进度状态。
    /// </summary>
    public RuntimeProgressState Progress { get; set; } = new();

    /// <summary>
    /// 日志自增 ID 计数器。
    /// </summary>
    public long NextLogId;

    /// <summary>
    /// 是否正在执行自动轮询。
    /// </summary>
    public bool IsPolling { get; set; }

    /// <summary>
    /// 下次计划巡检时间（UTC）。
    /// </summary>
    public DateTime NextScheduledAt { get; set; }

    /// <summary>
    /// 最近一次巡检开始时间（UTC）。
    /// </summary>
    public DateTime LastRunStartedAt { get; set; }

    /// <summary>
    /// 最近一次巡检完成时间（UTC）。
    /// </summary>
    public DateTime LastRunFinishedAt { get; set; }

    /// <summary>
    /// 自上次额度刷新后，有调用活动的 auth_index 集合。
    /// </summary>
    public HashSet<string> ActiveAuthIndices { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// usage-queue 监控是否对该站点活跃（能正常消费队列）。
    /// </summary>
    public bool UsageMonitorActive { get; set; }

    /// <summary>
    /// 标记 usage-queue 不受该站点 CPA 支持（404 后不再重试）。
    /// </summary>
    public bool UsageQueueUnsupported { get; set; }

    /// <summary>
    /// 监控统计：最近一次成功轮询时间。
    /// </summary>
    public DateTime UsageMonitorLastPollAt { get; set; }

    /// <summary>
    /// 监控统计：累计消费的队列条目总数。
    /// </summary>
    public int UsageMonitorTotalItemsPopped { get; set; }

    /// <summary>
    /// 监控统计：累计轮询次数。
    /// </summary>
    public int UsageMonitorPollCount { get; set; }

    /// <summary>
    /// 状态访问的同步锁，保护 ActiveAuthIndices、UsageMonitorActive 等字段。
    /// </summary>
    public object SyncRoot { get; } = new();

    /// <summary>
    /// 进度状态访问的独立锁，避免与 SyncRoot 产生死锁。
    /// </summary>
    public object ProgressLock { get; } = new();
}
