using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 运行时操作日志，仅保存在当前进程内存中。
/// </summary>
public sealed class OperationLogEntry
{
    /// <summary>
    /// 日志唯一标识。
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// 日志创建时间。
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 日志级别，如 info、warn、error。
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    /// <summary>
    /// 日志分类，如 system、inspection。
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "system";

    /// <summary>
    /// 操作类型，如 probe、action、refresh。
    /// </summary>
    [JsonPropertyName("operationType")]
    public string OperationType { get; set; } = "";

    /// <summary>
    /// 触发来源，如 manual、auto。
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "manual";

    /// <summary>
    /// 日志消息内容。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// 关联账号名称。
    /// </summary>
    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = "";

    /// <summary>
    /// 关联账号的展示名称。
    /// </summary>
    [JsonPropertyName("displayAccount")]
    public string DisplayAccount { get; set; } = "";

    /// <summary>
    /// 关联站点标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 关联站点名称。
    /// </summary>
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = "";
}

/// <summary>
/// 当前运行中的进度快照，前端通过轮询读取。
/// </summary>
public sealed class RuntimeProgressState
{
    /// <summary>
    /// 当前操作类型，如 probe、action、idle。
    /// </summary>
    [JsonPropertyName("operationType")]
    public string OperationType { get; set; } = "idle";

    /// <summary>
    /// 触发来源，如 manual、auto。
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// 整体状态，如 running、completed、failed、idle。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    /// <summary>
    /// 当前阶段，如 probing、acting、idle。
    /// </summary>
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = "idle";

    /// <summary>
    /// 进度描述文本。
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// 总任务数。
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// 已处理任务数。
    /// </summary>
    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    /// <summary>
    /// 动作阶段总任务数。
    /// </summary>
    [JsonPropertyName("actionTotal")]
    public int ActionTotal { get; set; }

    /// <summary>
    /// 动作阶段已处理数。
    /// </summary>
    [JsonPropertyName("actionProcessed")]
    public int ActionProcessed { get; set; }

    /// <summary>
    /// 完成百分比（0-100）。
    /// </summary>
    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    /// <summary>
    /// 操作开始时间。
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 操作结束时间。
    /// </summary>
    [JsonPropertyName("finishedAt")]
    public DateTime FinishedAt { get; set; }

    /// <summary>
    /// 最后更新时间。
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 当前正在处理的账号名称。
    /// </summary>
    [JsonPropertyName("currentAccountName")]
    public string CurrentAccountName { get; set; } = "";

    /// <summary>
    /// 当前正在处理的账号展示名称。
    /// </summary>
    [JsonPropertyName("currentDisplayAccount")]
    public string CurrentDisplayAccount { get; set; } = "";
}
