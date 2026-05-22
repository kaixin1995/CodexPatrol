using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// CPA auth-files 接口返回的认证文件条目
/// AOT 模式下必须用 JsonPropertyName 显式映射，否则 PascalCase 属性无法匹配 JSON 小写字段
/// </summary>
public sealed class AuthFileItem
{
    /// <summary>
    /// 认证文件名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 文件类型。
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// 认证供应商。
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// 认证索引（camelCase 格式）。
    /// </summary>
    [JsonPropertyName("authIndex")]
    public string? AuthIndex { get; set; }

    /// <summary>
    /// 认证索引（snake_case 格式，兼容旧接口）。
    /// </summary>
    [JsonPropertyName("auth_index")]
    public string? Auth_Index { get; set; }

    /// <summary>
    /// 关联账号名。
    /// </summary>
    [JsonPropertyName("account")]
    public string? Account { get; set; }

    /// <summary>
    /// 关联邮箱。
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// 账号标签。
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// 是否已禁用。
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// 账号状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// 账号状态描述。
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>
    /// ChatGPT 账户 ID，用于 Codex 请求头
    /// </summary>
    [JsonPropertyName("chatgpt_account_id")]
    public string? Chatgpt_Account_Id { get; set; }

    /// <summary>
    /// ChatGPT 账户 ID（camelCase 格式）。
    /// </summary>
    [JsonPropertyName("chatgptAccountId")]
    public string? ChatgptAccount_Id { get; set; }
}

/// <summary>
/// auth-files 列表接口返回。
/// </summary>
public sealed class AuthFilesResponse
{
    /// <summary>
    /// 认证文件列表。
    /// </summary>
    [JsonPropertyName("files")]
    public List<AuthFileItem> Files { get; set; } = [];

    /// <summary>
    /// 文件总数。
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }
}
