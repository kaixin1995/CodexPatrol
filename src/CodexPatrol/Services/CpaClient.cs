using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexPatrol.Models;
using CodexPatrol.Serialization;

namespace CodexPatrol.Services;

/// <summary>
/// CPA Management API 客户端，封装所有对 CPA 的 HTTP 调用。
/// </summary>
public sealed class CpaClient
{
    /// <summary>
    /// HTTP 客户端实例。
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// 构造 CpaClient，注入已配置好的 HttpClient。
    /// </summary>
    public CpaClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// 拼接完整的 CPA 管理接口地址，自动去除末尾斜杠并加上 /v0/management 前缀。
    /// </summary>
    private static string ApiUrl(PatrolSiteSettings site, string path)
    {
        return $"{site.CpaBaseUrl.TrimEnd('/')}/v0/management{path}";
    }

    /// <summary>
    /// 为请求添加 ManagementKey 鉴权头。
    /// </summary>
    private static void ApplyAuth(HttpRequestMessage msg, PatrolSiteSettings site)
    {
        if (!string.IsNullOrWhiteSpace(site.ManagementKey))
        {
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", site.ManagementKey);
        }
    }

    /// <summary>
    /// 发送 HTTP 请求，使用站点配置的超时时间。
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, PatrolSiteSettings site, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, site.TimeoutMs)));
        return await _http.SendAsync(request, timeoutCts.Token);
    }

    /// <summary>
    /// 获取认证文件列表。
    /// </summary>
    public async Task<AuthFilesResponse> GetAuthFilesAsync(PatrolSiteSettings site, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl(site, "/auth-files"));
        ApplyAuth(req, site);

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.AuthFilesResponse)
               ?? new AuthFilesResponse();
    }

    /// <summary>
    /// 通过 CPA 的 /api-call 接口代理请求上游 API。
    /// </summary>
    public async Task<ApiCallResponse> ApiCallAsync(PatrolSiteSettings site, ApiCallRequest payload, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl(site, "/api-call"));
        ApplyAuth(req, site);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, AppJsonContext.Default.ApiCallRequest),
            Encoding.UTF8,
            "application/json");

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.ApiCallResponse)
               ?? new ApiCallResponse();
    }

    /// <summary>
    /// 禁用指定账号。
    /// </summary>
    public async Task DisableAccountAsync(PatrolSiteSettings site, string name, CancellationToken ct = default)
    {
        await PatchAuthFileStatusAsync(site, name, true, ct);
    }

    /// <summary>
    /// 启用指定账号。
    /// </summary>
    public async Task EnableAccountAsync(PatrolSiteSettings site, string name, CancellationToken ct = default)
    {
        await PatchAuthFileStatusAsync(site, name, false, ct);
    }

    /// <summary>
    /// 通过 PATCH 接口修改账号的禁用/启用状态。
    /// </summary>
    private async Task PatchAuthFileStatusAsync(PatrolSiteSettings site, string name, bool disabled, CancellationToken ct)
    {
        var payload = new AuthFilePatchRequest { Name = name, Disabled = disabled };
        using var req = new HttpRequestMessage(HttpMethod.Patch, ApiUrl(site, "/auth-files/status"));
        ApplyAuth(req, site);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, AppJsonContext.Default.AuthFilePatchRequest),
            Encoding.UTF8,
            "application/json");

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 删除指定账号。
    /// </summary>
    public async Task DeleteAccountAsync(PatrolSiteSettings site, string name, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, ApiUrl(site, $"/auth-files?name={Uri.EscapeDataString(name)}"));
        ApplyAuth(req, site);

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 获取原始 usage 数据的 JSON 字符串。
    /// </summary>
    public async Task<string> GetUsageRawAsync(PatrolSiteSettings site, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl(site, "/usage"));
        ApplyAuth(req, site);

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// 从 CPA usage-queue 拉取调用记录，返回原始 JSON 字符串列表。
    /// </summary>
    public async Task<List<string>> PopUsageQueueAsync(PatrolSiteSettings site, int count, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl(site, "/usage-queue")}?count={Math.Max(1, count)}");
        ApplyAuth(req, site);

        using var resp = await SendAsync(req, site, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseQueueItems(json);
    }

    /// <summary>
    /// 解析 usage-queue 返回的 JSON 数组，支持对象、字符串编码对象、null 三种元素格式。
    /// </summary>
    private static List<string> ParseQueueItems(string json)
    {
        var items = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return items;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    items.Add(element.GetRawText());
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    // 队列条目可能是字符串编码的 JSON 对象
                    var inner = element.GetString();
                    if (!string.IsNullOrWhiteSpace(inner)
                        && inner.TrimStart().StartsWith('{'))
                    {
                        items.Add(inner);
                    }
                }
            }
        }
        catch
        {
            // 解析失败则返回空列表
        }

        return items;
    }

    /// <summary>
    /// 通过 CPA 代理请求 Codex 用量数据，返回 HTTP 状态码和响应体。
    /// </summary>
    public async Task<(int statusCode, string body)> RequestCodexUsageAsync(
        PatrolSiteSettings site,
        string authIndex,
        string? accountId,
        CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer $TOKEN$",
            ["Content-Type"] = "application/json",
            ["User-Agent"] = "codex_cli_rs/0.76.0 (Debian 13.0.0; x86_64) WindowsTerminal",
        };

        // 有 accountId 时补充 Chatgpt-Account-Id 头。
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            headers["Chatgpt-Account-Id"] = accountId;
        }

        var payload = new ApiCallRequest
        {
            AuthIndex = authIndex,
            Method = "GET",
            Url = "https://chatgpt.com/backend-api/wham/usage",
            Header = headers,
        };

        var result = await ApiCallAsync(site, payload, ct);
        var statusCode = result.Status_Code ?? result.StatusCode ?? 0;
        var body = result.BodyText ?? (result.Body?.ToString() ?? "");

        return (statusCode, body);
    }
}
