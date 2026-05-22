using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 应用全局配置。
/// 敏感信息（CpaBaseUrl、ManagementKey）从 connection.json 或环境变量读取，
/// 业务参数从 appsettings.json 读取。
/// </summary>
public sealed class PatrolSettings
{
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
    /// 监听主机地址。
    /// </summary>
    [JsonPropertyName("listenHost")]
    public string ListenHost { get; set; } = "0.0.0.0";

    /// <summary>
    /// 监听端口号。
    /// </summary>
    [JsonPropertyName("listenPort")]
    public int ListenPort { get; set; } = 22014;

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
    /// 本地登录密码哈希，仅存储哈希值。
    /// </summary>
    [JsonPropertyName("loginPasswordHash")]
    public string LoginPasswordHash { get; set; } = "";

    /// <summary>
    /// 目标供应商，默认 codex，预留扩展
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "codex";
}

/// <summary>
/// 敏感连接信息，从 connection.json 读取，不入库
/// </summary>
public sealed class CpaConnectionInfo
{
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
}
