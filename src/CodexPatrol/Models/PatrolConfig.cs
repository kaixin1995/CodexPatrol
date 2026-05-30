using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 本地持久化的业务配置，不包含敏感连接信息。
/// 兼容旧单站点格式，并支持新的多站点结构。
/// </summary>
public sealed class PatrolConfig
{
    /// <summary>
    /// 多站点配置列表。
    /// </summary>
    [JsonPropertyName("sites")]
    public List<PatrolSiteConfig> Sites { get; set; } = [];

    /// <summary>
    /// 旧版全局例外账号列表，兼容旧格式。
    /// </summary>
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = [];

    /// <summary>
    /// 旧版全局业务配置，兼容旧格式。
    /// </summary>
    [JsonPropertyName("settings")]
    public PersistedPatrolSettings? Settings { get; set; }
}

/// <summary>
/// 可持久化的巡检业务设置。
/// </summary>
public sealed class PersistedPatrolSettings
{
    /// <summary>
    /// 是否启用自动轮询巡检。
    /// </summary>
    [JsonPropertyName("autoPollingEnabled")]
    public bool AutoPollingEnabled { get; set; }

    /// <summary>
    /// 轮询间隔（分钟）。
    /// </summary>
    [JsonPropertyName("pollIntervalMinutes")]
    public int PollIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// 随机延迟下限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMinMinutes")]
    public int PollRandomDelayMinMinutes { get; set; } = 1;

    /// <summary>
    /// 随机延迟上限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMaxMinutes")]
    public int PollRandomDelayMaxMinutes { get; set; } = 3;

    /// <summary>
    /// 探测并发工作线程数。
    /// </summary>
    [JsonPropertyName("probeWorkers")]
    public int ProbeWorkers { get; set; } = 3;

    /// <summary>
    /// 探测批次间最小延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMinMs")]
    public int ProbeBatchDelayMinMs { get; set; } = 2000;

    /// <summary>
    /// 探测批次间最大延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMaxMs")]
    public int ProbeBatchDelayMaxMs { get; set; } = 3000;

    /// <summary>
    /// 执行操作的并发工作线程数。
    /// </summary>
    [JsonPropertyName("actionWorkers")]
    public int ActionWorkers { get; set; } = 4;

    /// <summary>
    /// 请求超时时间（毫秒）。
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 15000;

    /// <summary>
    /// 请求失败重试次数。
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>
    /// 自动动作模式，可选值：none、disable、delete。
    /// </summary>
    [JsonPropertyName("autoActionMode")]
    public string AutoActionMode { get; set; } = "none";

    /// <summary>
    /// 是否自动重新启用已恢复的账号。
    /// </summary>
    [JsonPropertyName("autoEnableRecovered")]
    public bool AutoEnableRecovered { get; set; }

    /// <summary>
    /// 额度使用率阈值（百分比），超过此值触发动作。
    /// </summary>
    [JsonPropertyName("usedPercentThreshold")]
    public int UsedPercentThreshold { get; set; } = 95;

    /// <summary>
    /// 目标供应商，默认 codex，预留扩展。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "codex";

    /// <summary>
    /// 是否启用优先级路由。
    /// </summary>
    [JsonPropertyName("priorityRoutingEnabled")]
    public bool PriorityRoutingEnabled { get; set; }

    /// <summary>
    /// 优先级路由最少保持启用的账号数量。
    /// </summary>
    [JsonPropertyName("priorityMinActiveCount")]
    public int PriorityMinActiveCount { get; set; } = 2;

    /// <summary>
    /// 是否禁用缓存刷新策略，强制每次巡检都真实请求。
    /// </summary>
    [JsonPropertyName("disableCacheRefresh")]
    public bool DisableCacheRefresh { get; set; }

    /// <summary>
    /// 从运行时站点配置提取可持久化的业务设置。
    /// </summary>
    public static PersistedPatrolSettings FromRuntime(PatrolSiteSettings settings)
    {
        return new PersistedPatrolSettings
        {
            AutoPollingEnabled = settings.AutoPollingEnabled,
            PollIntervalMinutes = settings.PollIntervalMinutes,
            PollRandomDelayMinMinutes = settings.PollRandomDelayMinMinutes,
            PollRandomDelayMaxMinutes = settings.PollRandomDelayMaxMinutes,
            ProbeWorkers = settings.ProbeWorkers,
            ProbeBatchDelayMinMs = settings.ProbeBatchDelayMinMs,
            ProbeBatchDelayMaxMs = settings.ProbeBatchDelayMaxMs,
            ActionWorkers = settings.ActionWorkers,
            TimeoutMs = settings.TimeoutMs,
            RetryCount = settings.RetryCount,
            AutoActionMode = settings.AutoActionMode,
            AutoEnableRecovered = settings.AutoEnableRecovered,
            UsedPercentThreshold = settings.UsedPercentThreshold,
            Provider = settings.Provider,
            PriorityRoutingEnabled = settings.PriorityRoutingEnabled,
            PriorityMinActiveCount = settings.PriorityMinActiveCount,
            DisableCacheRefresh = settings.DisableCacheRefresh,
        };
    }

    /// <summary>
    /// 将持久化设置覆盖到运行时站点配置。
    /// </summary>
    public void ApplyTo(PatrolSiteSettings settings)
    {
        settings.AutoPollingEnabled = AutoPollingEnabled;
        settings.PollIntervalMinutes = PollIntervalMinutes;
        settings.PollRandomDelayMinMinutes = PollRandomDelayMinMinutes;
        settings.PollRandomDelayMaxMinutes = PollRandomDelayMaxMinutes;
        settings.ProbeWorkers = ProbeWorkers;
        settings.ProbeBatchDelayMinMs = ProbeBatchDelayMinMs;
        settings.ProbeBatchDelayMaxMs = ProbeBatchDelayMaxMs;
        settings.ActionWorkers = ActionWorkers;
        settings.TimeoutMs = TimeoutMs;
        settings.RetryCount = RetryCount;
        settings.AutoActionMode = AutoActionMode;
        settings.AutoEnableRecovered = AutoEnableRecovered;
        settings.UsedPercentThreshold = UsedPercentThreshold;
        settings.Provider = Provider;
        settings.PriorityRoutingEnabled = PriorityRoutingEnabled;
        settings.PriorityMinActiveCount = PriorityMinActiveCount;
        settings.DisableCacheRefresh = DisableCacheRefresh;
    }
}
