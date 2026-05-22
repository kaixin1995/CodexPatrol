using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 例外名单配置，落盘到 patrol-config.json
/// </summary>
public sealed class ExceptionConfig
{
    /// <summary>
    /// 例外账号的文件名列表
    /// </summary>
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = [];
}
