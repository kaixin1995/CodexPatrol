using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 巡检动作类型。
/// </summary>
public enum InspectionAction
{
    /// <summary>
    /// 保持不变。
    /// </summary>
    Keep,

    /// <summary>
    /// 删除认证文件。
    /// </summary>
    Delete,

    /// <summary>
    /// 禁用账号。
    /// </summary>
    Disable,

    /// <summary>
    /// 启用账号。
    /// </summary>
    Enable
}

/// <summary>
/// 自动动作模式。
/// </summary>
public enum AutoActionMode
{
    /// <summary>
    /// 不执行自动动作。
    /// </summary>
    None,

    /// <summary>
    /// 自动禁用超限账号。
    /// </summary>
    Disable,

    /// <summary>
    /// 自动删除超限账号。
    /// </summary>
    Delete
}

/// <summary>
/// 单个账号的巡检决策结果
/// </summary>
public sealed class InspectionDecision
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
    /// 认证索引。
    /// </summary>
    [JsonPropertyName("authIndex")]
    public string AuthIndex { get; set; } = "";

    /// <summary>
    /// 决定的巡检动作。
    /// </summary>
    [JsonPropertyName("action")]
    public InspectionAction Action { get; set; } = InspectionAction.Keep;

    /// <summary>
    /// 决策原因说明。
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    /// <summary>
    /// 查询时的 HTTP 状态码。
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// 额度使用百分比。
    /// </summary>
    [JsonPropertyName("usedPercent")]
    public double? UsedPercent { get; set; }

    /// <summary>
    /// 是否已达到额度上限。
    /// </summary>
    [JsonPropertyName("isQuotaReached")]
    public bool IsQuotaReached { get; set; }

    /// <summary>
    /// 检查时间。
    /// </summary>
    [JsonPropertyName("checkedAt")]
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// 账号当前是否处于禁用状态。
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// 错误信息，查询失败时填充。
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

/// <summary>
/// 一次巡检运行的完整结果
/// </summary>
public sealed class InspectionRunResult
{
    /// <summary>
    /// 所有账号的巡检决策列表。
    /// </summary>
    [JsonPropertyName("decisions")]
    public List<InspectionDecision> Decisions { get; set; } = [];

    /// <summary>
    /// 执行动作后的结果列表。
    /// </summary>
    [JsonPropertyName("actionOutcomes")]
    public List<ActionOutcome> ActionOutcomes { get; set; } = [];

    /// <summary>
    /// 参与巡检的总账号数。
    /// </summary>
    [JsonPropertyName("totalAccounts")]
    public int TotalAccounts { get; set; }

    /// <summary>
    /// 实际探测的账号数。
    /// </summary>
    [JsonPropertyName("probedCount")]
    public int ProbedCount { get; set; }

    /// <summary>
    /// 决定删除的账号数。
    /// </summary>
    [JsonPropertyName("deleteCount")]
    public int DeleteCount { get; set; }

    /// <summary>
    /// 决定禁用的账号数。
    /// </summary>
    [JsonPropertyName("disableCount")]
    public int DisableCount { get; set; }

    /// <summary>
    /// 决定启用的账号数。
    /// </summary>
    [JsonPropertyName("enableCount")]
    public int EnableCount { get; set; }

    /// <summary>
    /// 决定保持不变的账号数。
    /// </summary>
    [JsonPropertyName("keepCount")]
    public int KeepCount { get; set; }

    /// <summary>
    /// 巡检开始时间。
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 巡检结束时间。
    /// </summary>
    [JsonPropertyName("finishedAt")]
    public DateTime FinishedAt { get; set; }

    /// <summary>
    /// 运行状态，如 completed、failed。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "completed";
}
