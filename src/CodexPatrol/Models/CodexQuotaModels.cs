namespace CodexPatrol.Models;

/// <summary>
/// Codex usage 接口原始响应负载。
/// </summary>
public sealed class CodexUsagePayload
{
    /// <summary>
    /// 订阅计划类型（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("plan_type")]
    public string? Plan_Type { get; set; }

    /// <summary>
    /// 订阅计划类型（camelCase 格式）。
    /// </summary>
    public string? PlanType { get; set; }

    /// <summary>
    /// 主速率限制信息（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rate_limit")]
    public CodexRateLimitInfo? Rate_Limit { get; set; }

    /// <summary>
    /// 主速率限制信息（camelCase 格式）。
    /// </summary>
    public CodexRateLimitInfo? RateLimit { get; set; }

    /// <summary>
    /// 代码审查速率限制（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("code_review_rate_limit")]
    public CodexRateLimitInfo? Code_Review_Rate_Limit { get; set; }

    /// <summary>
    /// 代码审查速率限制（camelCase 格式）。
    /// </summary>
    public CodexRateLimitInfo? CodeReviewRateLimit { get; set; }

    /// <summary>
    /// 附加速率限制列表（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("additional_rate_limits")]
    public List<CodexAdditionalRateLimit>? Additional_Rate_Limits { get; set; }

    /// <summary>
    /// 附加速率限制列表（camelCase 格式）。
    /// </summary>
    public List<CodexAdditionalRateLimit>? AdditionalRateLimits { get; set; }
}

/// <summary>
/// 速率限制详情，包含是否允许、是否达到上限以及窗口信息。
/// </summary>
public sealed class CodexRateLimitInfo
{
    /// <summary>
    /// 当前请求是否被允许。
    /// </summary>
    public bool? Allowed { get; set; }

    /// <summary>
    /// 是否已达到限制（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("limit_reached")]
    public bool? Limit_Reached { get; set; }

    /// <summary>
    /// 是否已达到限制（camelCase 格式）。
    /// </summary>
    public bool? LimitReached { get; set; }

    /// <summary>
    /// 主额度窗口（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("primary_window")]
    public CodexUsageWindow? Primary_Window { get; set; }

    /// <summary>
    /// 主额度窗口（camelCase 格式）。
    /// </summary>
    public CodexUsageWindow? PrimaryWindow { get; set; }

    /// <summary>
    /// 次额度窗口（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("secondary_window")]
    public CodexUsageWindow? Secondary_Window { get; set; }

    /// <summary>
    /// 次额度窗口（camelCase 格式）。
    /// </summary>
    public CodexUsageWindow? SecondaryWindow { get; set; }
}

/// <summary>
/// 单个额度窗口的使用情况。
/// </summary>
public sealed class CodexUsageWindow
{
    /// <summary>
    /// 已使用百分比（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("used_percent")]
    public double? Used_Percent { get; set; }

    /// <summary>
    /// 已使用百分比（camelCase 格式）。
    /// </summary>
    public double? UsedPercent { get; set; }

    /// <summary>
    /// 限制窗口时长（秒）（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("limit_window_seconds")]
    public double? Limit_Window_Seconds { get; set; }

    /// <summary>
    /// 限制窗口时长（秒）（camelCase 格式）。
    /// </summary>
    public double? LimitWindowSeconds { get; set; }

    /// <summary>
    /// 距离重置的剩余秒数（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("reset_after_seconds")]
    public double? Reset_After_Seconds { get; set; }

    /// <summary>
    /// 距离重置的剩余秒数（camelCase 格式）。
    /// </summary>
    public double? ResetAfterSeconds { get; set; }

    /// <summary>
    /// 重置时间戳（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("reset_at")]
    public double? Reset_At { get; set; }

    /// <summary>
    /// 重置时间戳（camelCase 格式）。
    /// </summary>
    public double? ResetAt { get; set; }
}

/// <summary>
/// 附加速率限制条目，如 Premium 系列模型等的独立限制。
/// </summary>
public sealed class CodexAdditionalRateLimit
{
    /// <summary>
    /// 限制名称（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("limit_name")]
    public string? Limit_Name { get; set; }

    /// <summary>
    /// 限制名称（camelCase 格式）。
    /// </summary>
    public string? LimitName { get; set; }

    /// <summary>
    /// 计量特性名称（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("metered_feature")]
    public string? Metered_Feature { get; set; }

    /// <summary>
    /// 计量特性名称（camelCase 格式）。
    /// </summary>
    public string? MeteredFeature { get; set; }

    /// <summary>
    /// 对应的速率限制信息（snake_case 格式）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rate_limit")]
    public CodexRateLimitInfo? Rate_Limit { get; set; }

    /// <summary>
    /// 对应的速率限制信息（camelCase 格式）。
    /// </summary>
    public CodexRateLimitInfo? RateLimit { get; set; }
}
