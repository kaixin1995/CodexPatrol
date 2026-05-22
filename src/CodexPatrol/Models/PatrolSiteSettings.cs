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
    /// 该站点的业务配置。
    /// </summary>
    [JsonPropertyName("settings")]
    public PersistedPatrolSettings Settings { get; set; } = new();
}
