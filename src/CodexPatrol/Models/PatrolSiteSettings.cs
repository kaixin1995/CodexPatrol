using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 单个 CPA 站点的完整运行配置。
/// </summary>
public sealed class PatrolSiteSettings
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "default";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "默认站点";

    /// <summary>
    /// 是否启用该站点。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CPA 管理接口地址。
    /// </summary>
    [JsonPropertyName("cpaBaseUrl")]
    public string CpaBaseUrl { get; set; } = "http://localhost:8317";

    /// <summary>
    /// CPA 管理密钥。
    /// </summary>
    [JsonPropertyName("managementKey")]
    public string ManagementKey { get; set; } = "";

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
    /// 是否启用优先级路由。关闭时所有账号同等对待，启用时按优先级顺序调度。
    /// </summary>
    [JsonPropertyName("priorityRoutingEnabled")]
    public bool PriorityRoutingEnabled { get; set; }

    /// <summary>
    /// 优先级路由最少保持启用的账号数量，默认 2。
    /// 防止当前账号在两次巡检之间额度耗尽导致 CPA 无可用账号。
    /// </summary>
    [JsonPropertyName("priorityMinActiveCount")]
    public int PriorityMinActiveCount { get; set; } = 2;

    /// <summary>
    /// 是否禁用缓存刷新策略，强制每次巡检都真实请求。
    /// 默认 false（走缓存策略，根据 usage-queue 调用日志决定哪些账号刷新）。
    /// 当日志模块被禁用导致读不到调用信息时，可设为 true 以保证额度数据准确。
    /// </summary>
    [JsonPropertyName("disableCacheRefresh")]
    public bool DisableCacheRefresh { get; set; }

    /// <summary>
    /// 浅拷贝当前站点配置，返回独立实例。
    /// </summary>
    public PatrolSiteSettings Clone()
    {
        return new PatrolSiteSettings
        {
            SiteId = SiteId,
            Name = Name,
            Enabled = Enabled,
            CpaBaseUrl = CpaBaseUrl,
            ManagementKey = ManagementKey,
            AutoPollingEnabled = AutoPollingEnabled,
            PollIntervalMinutes = PollIntervalMinutes,
            PollRandomDelayMinMinutes = PollRandomDelayMinMinutes,
            PollRandomDelayMaxMinutes = PollRandomDelayMaxMinutes,
            ProbeWorkers = ProbeWorkers,
            ProbeBatchDelayMinMs = ProbeBatchDelayMinMs,
            ProbeBatchDelayMaxMs = ProbeBatchDelayMaxMs,
            ActionWorkers = ActionWorkers,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            AutoActionMode = AutoActionMode,
            AutoEnableRecovered = AutoEnableRecovered,
            UsedPercentThreshold = UsedPercentThreshold,
            Provider = Provider,
            PriorityRoutingEnabled = PriorityRoutingEnabled,
            PriorityMinActiveCount = PriorityMinActiveCount,
            DisableCacheRefresh = DisableCacheRefresh,
        };
    }
}

/// <summary>
/// connection.json 的多站点格式，保留旧字段以兼容单站点版本。
/// </summary>
public sealed class MultiSiteConnectionConfig
{
    /// <summary>
    /// 多站点连接列表。
    /// </summary>
    [JsonPropertyName("sites")]
    public List<CpaConnectionSite> Sites { get; set; } = [];

    /// <summary>
    /// 旧版单站点 CPA 地址，兼容旧格式。
    /// </summary>
    [JsonPropertyName("cpaBaseUrl")]
    public string CpaBaseUrl { get; set; } = "";

    /// <summary>
    /// 旧版单站点管理密钥，兼容旧格式。
    /// </summary>
    [JsonPropertyName("managementKey")]
    public string ManagementKey { get; set; } = "";
}

/// <summary>
/// 单个站点的敏感连接信息。
/// </summary>
public sealed class CpaConnectionSite
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "default";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "默认站点";

    /// <summary>
    /// 是否启用该站点。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CPA 管理接口地址。
    /// </summary>
    [JsonPropertyName("cpaBaseUrl")]
    public string CpaBaseUrl { get; set; } = "";

    /// <summary>
    /// CPA 管理密钥。
    /// </summary>
    [JsonPropertyName("managementKey")]
    public string ManagementKey { get; set; } = "";

    /// <summary>
    /// 目标供应商。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "codex";
}

/// <summary>
/// 单个账号的优先级配置。
/// </summary>
public sealed class AccountPriority
{
    /// <summary>
    /// 账号名称，与 AuthFileItem.Name 对应。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 优先级数值，越小越优先。0 表示未配置同等优先。
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// 是否等待下一次巡检先做首检，再纳入自动排序。
    /// </summary>
    [JsonPropertyName("pendingFirstInspection")]
    public bool PendingFirstInspection { get; set; }
}

/// <summary>
/// patrol-config.json 中单个站点的非敏感业务配置。
/// </summary>
public sealed class PatrolSiteConfig
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "default";

    /// <summary>
    /// 该站点的例外账号列表。
    /// </summary>
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = [];

    /// <summary>
    /// 该站点的账号优先级配置，数值越小越优先。
    /// </summary>
    [JsonPropertyName("accountPriorities")]
    public List<AccountPriority> AccountPriorities { get; set; } = [];

    /// <summary>
    /// 该站点的业务配置。
    /// </summary>
    [JsonPropertyName("settings")]
    public PersistedPatrolSettings Settings { get; set; } = new();
}
