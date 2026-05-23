using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 通用消息响应。
/// </summary>
public sealed class MessageResponse
{
    /// <summary>
    /// 响应消息文本。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// 错误响应。
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// 错误描述文本。
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

/// <summary>
/// 应用基础信息响应。
/// </summary>
public sealed class AppInfoResponse
{
    /// <summary>
    /// 当前应用版本号。
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

/// <summary>
/// 例外名单响应。
/// </summary>
public sealed class ExceptionsResponse
{
    /// <summary>
    /// 例外账号名称列表。
    /// </summary>
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = [];
}

/// <summary>
/// 巡检状态响应。
/// </summary>
public sealed class InspectionStatusResponse
{
    /// <summary>
    /// 是否正在轮询中。
    /// </summary>
    [JsonPropertyName("isPolling")]
    public bool IsPolling { get; set; }

    /// <summary>
    /// 下一次常规自动巡检时间。
    /// </summary>
    [JsonPropertyName("nextScheduledAt")]
    public DateTime NextScheduledAt { get; set; }

    /// <summary>
    /// 下一次额度重置检查时间。
    /// </summary>
    [JsonPropertyName("nextResetCheckAt")]
    public DateTime NextResetCheckAt { get; set; }

    /// <summary>
    /// 上次巡检开始时间。
    /// </summary>
    [JsonPropertyName("lastRunStartedAt")]
    public DateTime LastRunStartedAt { get; set; }

    /// <summary>
    /// 上次巡检结束时间。
    /// </summary>
    [JsonPropertyName("lastRunFinishedAt")]
    public DateTime LastRunFinishedAt { get; set; }

    /// <summary>
    /// 是否启用自动轮询。
    /// </summary>
    [JsonPropertyName("autoPollingEnabled")]
    public bool AutoPollingEnabled { get; set; }

    /// <summary>
    /// 轮询间隔（分钟）。
    /// </summary>
    [JsonPropertyName("pollIntervalMinutes")]
    public int PollIntervalMinutes { get; set; }
}

/// <summary>
/// 站点概要信息。
/// </summary>
public sealed class SiteOptionResponse
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = "";

    /// <summary>
    /// 站点是否启用。
    /// </summary>
    [JsonPropertyName("siteEnabled")]
    public bool SiteEnabled { get; set; }

    /// <summary>
    /// CPA 管理接口地址。
    /// </summary>
    [JsonPropertyName("cpaBaseUrl")]
    public string CpaBaseUrl { get; set; } = "";

    /// <summary>
    /// 是否已配置管理密钥。
    /// </summary>
    [JsonPropertyName("hasManagementKey")]
    public bool HasManagementKey { get; set; }

    /// <summary>
    /// 目标供应商。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "codex";
}

/// <summary>
/// 站点列表响应。
/// </summary>
public sealed class SiteListResponse
{
    /// <summary>
    /// 当前选中的站点标识。
    /// </summary>
    [JsonPropertyName("selectedSiteId")]
    public string SelectedSiteId { get; set; } = "";

    /// <summary>
    /// 可用站点列表。
    /// </summary>
    [JsonPropertyName("sites")]
    public List<SiteOptionResponse> Sites { get; set; } = [];
}

/// <summary>
/// 设置查询响应，隐藏敏感信息。
/// </summary>
public sealed class SettingsResponse
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = "";

    /// <summary>
    /// 站点是否启用。
    /// </summary>
    [JsonPropertyName("siteEnabled")]
    public bool SiteEnabled { get; set; }

    /// <summary>
    /// CPA 管理接口地址。
    /// </summary>
    [JsonPropertyName("cpaBaseUrl")]
    public string CpaBaseUrl { get; set; } = "";

    /// <summary>
    /// 是否已配置管理密钥。
    /// </summary>
    [JsonPropertyName("hasManagementKey")]
    public bool HasManagementKey { get; set; }

    /// <summary>
    /// 是否启用自动轮询巡检。
    /// </summary>
    [JsonPropertyName("autoPollingEnabled")]
    public bool AutoPollingEnabled { get; set; }

    /// <summary>
    /// 轮询间隔（分钟）。
    /// </summary>
    [JsonPropertyName("pollIntervalMinutes")]
    public int PollIntervalMinutes { get; set; }

    /// <summary>
    /// 随机延迟下限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMinMinutes")]
    public int PollRandomDelayMinMinutes { get; set; }

    /// <summary>
    /// 随机延迟上限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMaxMinutes")]
    public int PollRandomDelayMaxMinutes { get; set; }

    /// <summary>
    /// 探测并发工作线程数。
    /// </summary>
    [JsonPropertyName("probeWorkers")]
    public int ProbeWorkers { get; set; }

    /// <summary>
    /// 探测批次间最小延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMinMs")]
    public int ProbeBatchDelayMinMs { get; set; }

    /// <summary>
    /// 探测批次间最大延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMaxMs")]
    public int ProbeBatchDelayMaxMs { get; set; }

    /// <summary>
    /// 执行操作的并发工作线程数。
    /// </summary>
    [JsonPropertyName("actionWorkers")]
    public int ActionWorkers { get; set; }

    /// <summary>
    /// 请求超时时间（毫秒）。
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; }

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
    /// 额度使用率阈值（百分比）。
    /// </summary>
    [JsonPropertyName("usedPercentThreshold")]
    public int UsedPercentThreshold { get; set; }

    /// <summary>
    /// 目标供应商。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "codex";
}

/// <summary>
/// 刷新结果响应。
/// </summary>
public sealed class RefreshResponse
{
    /// <summary>
    /// 本次刷新的账号数量。
    /// </summary>
    [JsonPropertyName("refreshed")]
    public int Refreshed { get; set; }
}

/// <summary>
/// 自动轮询操作响应。
/// </summary>
public sealed class AutoPollingResponse
{
    /// <summary>
    /// 当前自动轮询启用状态。
    /// </summary>
    [JsonPropertyName("autoPollingEnabled")]
    public bool AutoPollingEnabled { get; set; }
}

/// <summary>
/// 保存设置响应。
/// </summary>
public sealed class SaveSettingsResponse
{
    /// <summary>
    /// 是否保存成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>
/// 保存设置请求体，与运行时实体分开以避免 DI 注入歧义。
/// </summary>
public sealed class SaveSettingsRequest
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = "";

    /// <summary>
    /// 是否启用该站点。
    /// </summary>
    [JsonPropertyName("siteEnabled")]
    public bool SiteEnabled { get; set; } = true;

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
    public string Provider { get; set; } = "";

    /// <summary>
    /// 轮询间隔（分钟）。
    /// </summary>
    [JsonPropertyName("pollIntervalMinutes")]
    public int PollIntervalMinutes { get; set; }

    /// <summary>
    /// 随机延迟下限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMinMinutes")]
    public int? PollRandomDelayMinMinutes { get; set; }

    /// <summary>
    /// 随机延迟上限（分钟）。
    /// </summary>
    [JsonPropertyName("pollRandomDelayMaxMinutes")]
    public int? PollRandomDelayMaxMinutes { get; set; }

    /// <summary>
    /// 探测并发工作线程数。
    /// </summary>
    [JsonPropertyName("probeWorkers")]
    public int ProbeWorkers { get; set; }

    /// <summary>
    /// 探测批次间最小延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMinMs")]
    public int ProbeBatchDelayMinMs { get; set; }

    /// <summary>
    /// 探测批次间最大延迟（毫秒）。
    /// </summary>
    [JsonPropertyName("probeBatchDelayMaxMs")]
    public int ProbeBatchDelayMaxMs { get; set; }

    /// <summary>
    /// 执行操作的并发工作线程数。
    /// </summary>
    [JsonPropertyName("actionWorkers")]
    public int ActionWorkers { get; set; }

    /// <summary>
    /// 请求超时时间（毫秒）。
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; }

    /// <summary>
    /// 请求失败重试次数。
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>
    /// 自动动作模式。
    /// </summary>
    [JsonPropertyName("autoActionMode")]
    public string AutoActionMode { get; set; } = "";

    /// <summary>
    /// 额度使用率阈值（百分比）。
    /// </summary>
    [JsonPropertyName("usedPercentThreshold")]
    public int UsedPercentThreshold { get; set; }

    /// <summary>
    /// 是否自动重新启用已恢复的账号。
    /// </summary>
    [JsonPropertyName("autoEnableRecovered")]
    public bool AutoEnableRecovered { get; set; }
}

/// <summary>
/// 账号刷新响应。
/// </summary>
public sealed class AccountRefreshResponse
{
    /// <summary>
    /// 刷新的账号数量。
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// 登录状态响应。
/// </summary>
public sealed class AuthStatusResponse
{
    /// <summary>
    /// 是否已配置登录密码。
    /// </summary>
    [JsonPropertyName("passwordConfigured")]
    public bool PasswordConfigured { get; set; }

    /// <summary>
    /// 当前是否已通过认证。
    /// </summary>
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }

    /// <summary>
    /// 是否需要首次设置密码。
    /// </summary>
    [JsonPropertyName("setupRequired")]
    public bool SetupRequired { get; set; }
}

/// <summary>
/// 登录请求。
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// 登录密码。
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

/// <summary>
/// 首次设置密码请求。
/// </summary>
public sealed class SetupPasswordRequest
{
    /// <summary>
    /// 新密码。
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    /// <summary>
    /// 确认密码。
    /// </summary>
    [JsonPropertyName("confirmPassword")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// usage-queue 监控状态。
/// </summary>
public sealed class UsageMonitorStatus
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 站点显示名称。
    /// </summary>
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = "";

    /// <summary>
    /// 监控是否活跃。
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// 该站点是否不支持 usage 监控。
    /// </summary>
    [JsonPropertyName("unsupported")]
    public bool Unsupported { get; set; }

    /// <summary>
    /// 上次轮询时间。
    /// </summary>
    [JsonPropertyName("lastPollAt")]
    public DateTime LastPollAt { get; set; }

    /// <summary>
    /// 累计轮询次数。
    /// </summary>
    [JsonPropertyName("pollCount")]
    public int PollCount { get; set; }

    /// <summary>
    /// 累计弹出的 usage 条目数。
    /// </summary>
    [JsonPropertyName("totalItemsPopped")]
    public int TotalItemsPopped { get; set; }

    /// <summary>
    /// 当前活跃的 authIndex 数量。
    /// </summary>
    [JsonPropertyName("activeAuthIndexCount")]
    public int ActiveAuthIndexCount { get; set; }
}
