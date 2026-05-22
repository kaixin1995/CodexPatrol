using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// CPA /api-call 请求体
/// </summary>
public sealed class ApiCallRequest
{
    /// <summary>
    /// 认证索引，指定使用哪个认证文件发起请求。
    /// </summary>
    [JsonPropertyName("authIndex")]
    public string AuthIndex { get; set; } = "";

    /// <summary>
    /// HTTP 方法，如 GET、POST。
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    /// <summary>
    /// 请求目标 URL。
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// 附加请求头。
    /// </summary>
    [JsonPropertyName("header")]
    public Dictionary<string, string>? Header { get; set; }

    /// <summary>
    /// 请求体内容。
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

/// <summary>
/// CPA /api-call 响应体
/// </summary>
public sealed class ApiCallResponse
{
    /// <summary>
    /// HTTP 状态码（snake_case 格式）。
    /// </summary>
    [JsonPropertyName("status_code")]
    public int? Status_Code { get; set; }

    /// <summary>
    /// HTTP 状态码（camelCase 格式）。
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    /// <summary>
    /// 响应中是否包含状态码。
    /// </summary>
    [JsonPropertyName("has_status_code")]
    public bool HasStatusCode { get; set; }

    /// <summary>
    /// 响应头字典。
    /// </summary>
    [JsonPropertyName("header")]
    public Dictionary<string, object?>? Header { get; set; }

    /// <summary>
    /// 响应体文本。
    /// </summary>
    [JsonPropertyName("bodyText")]
    public string? BodyText { get; set; }

    /// <summary>
    /// 响应体原始对象。
    /// </summary>
    [JsonPropertyName("body")]
    public object? Body { get; set; }
}

/// <summary>
/// CPA auth-files PATCH 请求体
/// </summary>
public sealed class AuthFilePatchRequest
{
    /// <summary>
    /// 认证文件名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 是否禁用。
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

/// <summary>
/// 执行结果
/// </summary>
public sealed class ActionOutcome
{
    /// <summary>
    /// 执行的动作类型。
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    /// <summary>
    /// 认证文件名称。
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    /// <summary>
    /// 账号展示名称。
    /// </summary>
    [JsonPropertyName("displayAccount")]
    public string DisplayAccount { get; set; } = "";

    /// <summary>
    /// 执行是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息，执行失败时填充。
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
