using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 解析后的单个额度窗口
/// </summary>
public sealed class CodexQuotaWindowSnapshot
{
    /// <summary>
    /// 窗口唯一标识。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// 窗口显示标签，如"主窗口"、"次窗口"。
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>
    /// 已使用额度百分比。
    /// </summary>
    [JsonPropertyName("usedPercent")]
    public double? UsedPercent { get; set; }

    /// <summary>
    /// 重置时间显示文本。
    /// </summary>
    [JsonPropertyName("resetLabel")]
    public string ResetLabel { get; set; } = "";

    /// <summary>
    /// 限制窗口时长（秒）。
    /// </summary>
    [JsonPropertyName("limitWindowSeconds")]
    public double? LimitWindowSeconds { get; set; }

    /// <summary>
    /// 重置时间（UTC）。
    /// </summary>
    [JsonPropertyName("resetAtUtc")]
    public DateTime ResetAtUtc { get; set; }

    /// <summary>
    /// 最近一次已处理该重置点的时间，仅用于运行时避免重复触发。
    /// </summary>
    [JsonIgnore]
    public DateTime LastResetHandledAt { get; set; }
}

/// <summary>
/// 单个账号的额度快照
/// </summary>
public sealed class CodexQuotaSnapshot
{
    /// <summary>
    /// 账号名称。
    /// </summary>
    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = "";

    /// <summary>
    /// 用于前端展示的账号标识。
    /// </summary>
    [JsonPropertyName("displayAccount")]
    public string DisplayAccount { get; set; } = "";

    /// <summary>
    /// 订阅计划类型。
    /// </summary>
    [JsonPropertyName("planType")]
    public string? PlanType { get; set; }

    /// <summary>
    /// 是否已禁用。
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// 该账号的所有额度窗口快照。
    /// </summary>
    [JsonPropertyName("windows")]
    public List<CodexQuotaWindowSnapshot> Windows { get; set; } = [];

    /// <summary>
    /// 最近一次检查时间，命中缓存和跳过检查时也会更新。
    /// </summary>
    [JsonPropertyName("checkedAt")]
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// 最近一次真实请求刷新时间，仅真实请求成功或失败后更新。
    /// </summary>
    [JsonPropertyName("refreshedAt")]
    public DateTime RefreshedAt { get; set; }

    /// <summary>
    /// 查询时的 HTTP 状态码。
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// 查询是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息，查询失败时填充。
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = "";

    /// <summary>
    /// 是否来自缓存数据。
    /// </summary>
    [JsonPropertyName("fromCache")]
    public bool FromCache { get; set; }

    /// <summary>
    /// 使用缓存的原因说明。
    /// </summary>
    [JsonPropertyName("cacheReason")]
    public string CacheReason { get; set; } = "";

    /// <summary>
    /// 上次使用时间。
    /// </summary>
    [JsonPropertyName("lastUsageAt")]
    public DateTime LastUsageAt { get; set; }

    /// <summary>
    /// 禁用原因，仅优先级路由开启时有效。
    /// </summary>
    [JsonPropertyName("disableReason")]
    public string DisableReason { get; set; } = "";
}
