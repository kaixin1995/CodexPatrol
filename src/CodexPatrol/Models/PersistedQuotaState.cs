using System.Text.Json.Serialization;

namespace CodexPatrol.Models;

/// <summary>
/// 本地持久化的额度缓存，按站点保存账号额度与上次真实刷新结果。
/// </summary>
public sealed class PersistedQuotaState
{
    /// <summary>
    /// 各站点的额度缓存列表。
    /// </summary>
    [JsonPropertyName("sites")]
    public List<PersistedQuotaSiteState> Sites { get; set; } = [];
}

/// <summary>
/// 单个站点的额度缓存快照。
/// </summary>
public sealed class PersistedQuotaSiteState
{
    /// <summary>
    /// 站点唯一标识。
    /// </summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    /// <summary>
    /// 该站点下所有账号的额度快照列表。
    /// </summary>
    [JsonPropertyName("quotas")]
    public List<CodexQuotaSnapshot> Quotas { get; set; } = [];
}
