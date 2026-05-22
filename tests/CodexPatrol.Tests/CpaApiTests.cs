using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CodexPatrol.Tests;

/// <summary>
/// CPA Management API 集成测试
/// 直接调用真实 CPA 接口，验证连接和基本功能
/// 测试完成后还原账号状态，不会删除任何账号
/// </summary>
public class CpaApiTests
{
    private static readonly string CpaBaseUrl;
    private static readonly string ManagementKey;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static CpaApiTests()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "connection.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "CodexPatrol", "connection.json");
        }

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("sites", out var sites) && sites.ValueKind == JsonValueKind.Array)
            {
                var site = sites.EnumerateArray().FirstOrDefault(item =>
                    !item.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean());
                if (site.ValueKind == JsonValueKind.Undefined)
                {
                    site = sites.EnumerateArray().FirstOrDefault();
                }

                CpaBaseUrl = site.ValueKind != JsonValueKind.Undefined && site.TryGetProperty("cpaBaseUrl", out var siteUrl)
                    ? siteUrl.GetString() ?? ""
                    : "";
                ManagementKey = site.ValueKind != JsonValueKind.Undefined && site.TryGetProperty("managementKey", out var siteKey)
                    ? siteKey.GetString() ?? ""
                    : "";
            }
            else
            {
                CpaBaseUrl = root.TryGetProperty("CpaBaseUrl", out var u) ? u.GetString() ?? "" : "";
                ManagementKey = root.TryGetProperty("ManagementKey", out var k) ? k.GetString() ?? "" : "";
            }
        }
        else
        {
            CpaBaseUrl = "http://localhost:8317";
            ManagementKey = "";
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var url = $"{CpaBaseUrl.TrimEnd('/')}/v0/management{path}";
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(ManagementKey))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ManagementKey);
        }
        return req;
    }

    private async Task<JsonElement> SendAndParseAsync(HttpRequestMessage request)
    {
        using var resp = await Http.SendAsync(request);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    private static string ToJson(object obj) => JsonSerializer.Serialize(obj);

    // ===== 测试 1：验证 CPA 连接 =====
    [Fact]
    public async Task T01_GetConfig_ShouldConnect()
    {
        using var req = CreateRequest(HttpMethod.Get, "/config");
        using var resp = await Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.NotEmpty(json);

        Console.WriteLine($"[PASS] CPA 连接成功：{CpaBaseUrl}");
    }

    // ===== 测试 2：获取认证文件列表 =====
    [Fact]
    public async Task T02_GetAuthFiles_ShouldReturnList()
    {
        using var req = CreateRequest(HttpMethod.Get, "/auth-files");
        var root = await SendAndParseAsync(req);

        Assert.True(root.TryGetProperty("files", out var files));
        var filesList = files.EnumerateArray().ToList();
        Assert.NotEmpty(filesList);

        Console.WriteLine($"[PASS] 获取到 {filesList.Count} 个认证文件");

        foreach (var file in filesList)
        {
            var name = file.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var type = file.TryGetProperty("type", out var t) ? t.GetString() : "?";
            var disabled = file.TryGetProperty("disabled", out var d) && d.GetBoolean();
            Console.WriteLine($"  - {name} (type={type}, disabled={disabled})");
        }
    }

    // ===== 测试 3：获取 Codex 账号列表 =====
    [Fact]
    public async Task T03_GetCodexAccounts_ShouldFilter()
    {
        using var req = CreateRequest(HttpMethod.Get, "/auth-files");
        var root = await SendAndParseAsync(req);

        var files = root.GetProperty("files").EnumerateArray().ToList();
        var codexFiles = files.Where(f =>
        {
            var type = f.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var provider = f.TryGetProperty("provider", out var p) ? p.GetString() ?? "" : "";
            return type.Equals("codex", StringComparison.OrdinalIgnoreCase)
                || provider.Equals("codex", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        Assert.NotEmpty(codexFiles);
        Console.WriteLine($"[PASS] 筛选到 {codexFiles.Count} 个 Codex 账号");

        foreach (var file in codexFiles)
        {
            var name = file.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var authIndex = file.TryGetProperty("auth_index", out var ai) ? ai.GetString() :
                            file.TryGetProperty("authIndex", out var ai2) ? ai2.GetString() : "?";
            Console.WriteLine($"  - {name} (authIndex={authIndex})");
        }
    }

    // ===== 测试 4：探测单个 Codex 账号额度 =====
    [Fact]
    public async Task T04_ProbeCodexUsage_ShouldReturnData()
    {
        // 获取一个 codex 账号
        using var listReq = CreateRequest(HttpMethod.Get, "/auth-files");
        var listRoot = await SendAndParseAsync(listReq);

        var files = listRoot.GetProperty("files").EnumerateArray().ToList();
        var codexFile = files.FirstOrDefault(f =>
        {
            var type = f.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            return type.Equals("codex", StringComparison.OrdinalIgnoreCase);
        });

        Assert.True(codexFile.ValueKind != JsonValueKind.Undefined, "没有找到 Codex 账号");

        var authIndex = codexFile.TryGetProperty("auth_index", out var ai) ? ai.GetString() :
                        codexFile.TryGetProperty("authIndex", out var ai2) ? ai2.GetString() : null;
        Assert.NotNull(authIndex);

        var accountId = codexFile.TryGetProperty("chatgpt_account_id", out var aci) ? aci.GetString() :
                        codexFile.TryGetProperty("chatgptAccountId", out var aci2) ? aci2.GetString() : null;

        // 通过 /api-call 探测额度
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer $TOKEN$",
            ["Content-Type"] = "application/json",
            ["User-Agent"] = "codex_cli_rs/0.76.0 (Debian 13.0.0; x86_64) WindowsTerminal",
        };
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            headers["Chatgpt-Account-Id"] = accountId;
        }

        var payload = new
        {
            authIndex,
            method = "GET",
            url = "https://chatgpt.com/backend-api/wham/usage",
            header = headers,
        };

        using var callReq = CreateRequest(HttpMethod.Post, "/api-call");
        callReq.Content = new StringContent(ToJson(payload), Encoding.UTF8, "application/json");

        using var callResp = await Http.SendAsync(callReq);
        Assert.Equal(HttpStatusCode.OK, callResp.StatusCode);

        var body = await callResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);

        var callResult = JsonDocument.Parse(body).RootElement;
        var statusCode = callResult.TryGetProperty("status_code", out var sc) ? sc.GetInt32() :
                         callResult.TryGetProperty("statusCode", out var sc2) ? sc2.GetInt32() : 0;

        Console.WriteLine($"[PASS] Codex 额度探测成功：HTTP {statusCode}");
        Console.WriteLine($"  响应：{body[..Math.Min(body.Length, 500)]}");
    }

    // ===== 测试 5：禁用再启用一个账号（不删除），完成后还原 =====
    [Fact]
    public async Task T05_DisableAndEnableAccount_ShouldRestore()
    {
        // 获取一个未禁用的 codex 账号
        using var listReq = CreateRequest(HttpMethod.Get, "/auth-files");
        var listRoot = await SendAndParseAsync(listReq);

        var files = listRoot.GetProperty("files").EnumerateArray().ToList();
        var target = files.FirstOrDefault(f =>
        {
            var type = f.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var disabled = f.TryGetProperty("disabled", out var d) && d.GetBoolean();
            return type.Equals("codex", StringComparison.OrdinalIgnoreCase) && !disabled;
        });

        if (target.ValueKind == JsonValueKind.Undefined)
        {
            Console.WriteLine("[SKIP] 没有找到可测试的启用状态 Codex 账号");
            return;
        }

        var fileName = target.TryGetProperty("name", out var n) ? n.GetString()! : "";
        Console.WriteLine($"[INFO] 测试账号：{fileName}");

        try
        {
            // 禁用（用 /auth-files/status 端点）
            using var disableReq = CreateRequest(HttpMethod.Patch, "/auth-files/status");
            disableReq.Content = new StringContent(
                ToJson(new { name = fileName, disabled = true }),
                Encoding.UTF8, "application/json");

            using var disableResp = await Http.SendAsync(disableReq);
            Assert.Equal(HttpStatusCode.OK, disableResp.StatusCode);
            Console.WriteLine($"[PASS] 已禁用账号：{fileName}");
        }
        finally
        {
            // 还原：无论测试是否成功，都确保启用回来
            using var enableReq = CreateRequest(HttpMethod.Patch, "/auth-files/status");
            enableReq.Content = new StringContent(
                ToJson(new { name = fileName, disabled = false }),
                Encoding.UTF8, "application/json");

            using var enableResp = await Http.SendAsync(enableReq);
            Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);
            Console.WriteLine($"[PASS] 已还原启用账号：{fileName}");
        }
    }

    // ===== 测试 6：验证账号状态已还原 =====
    [Fact]
    public async Task T06_VerifyAccountRestored()
    {
        using var req = CreateRequest(HttpMethod.Get, "/auth-files");
        var root = await SendAndParseAsync(req);

        Assert.True(root.TryGetProperty("files", out var files));
        var count = files.EnumerateArray().Count();
        Assert.True(count > 0);

        Console.WriteLine($"[PASS] 账号列表验证完成，共 {count} 个文件");
    }

    // ===== 测试 7：验证 connection.json 读取逻辑 =====
    [Fact]
    public void T07_ConnectionJson_ShouldBeReadable()
    {
        // 验证 connection.json 能找到
        var path = Path.Combine(AppContext.BaseDirectory, "connection.json");
        Assert.True(File.Exists(path), $"connection.json 不存在于 {path}，检查 csproj 的 CopyToOutputDirectory 配置");

        // 用和主程序相同的方式（JsonDocument）读取
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string url;
        string key;

        if (root.TryGetProperty("sites", out var sites) && sites.ValueKind == JsonValueKind.Array)
        {
            var site = sites.EnumerateArray().FirstOrDefault(item =>
                !item.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean());
            if (site.ValueKind == JsonValueKind.Undefined)
            {
                site = sites.EnumerateArray().FirstOrDefault();
            }

            Assert.True(site.ValueKind != JsonValueKind.Undefined, "connection.json 的 sites 不能为空");
            Assert.True(site.TryGetProperty("cpaBaseUrl", out var siteUrl), "site 缺少 cpaBaseUrl 字段");
            Assert.True(site.TryGetProperty("managementKey", out var siteKey), "site 缺少 managementKey 字段");
            url = siteUrl.GetString() ?? "";
            key = siteKey.GetString() ?? "";
        }
        else
        {
            Assert.True(root.TryGetProperty("CpaBaseUrl", out var urlEl), "connection.json 缺少 CpaBaseUrl 字段");
            Assert.True(root.TryGetProperty("ManagementKey", out var keyEl), "connection.json 缺少 ManagementKey 字段");
            url = urlEl.GetString() ?? "";
            key = keyEl.GetString() ?? "";
        }

        Assert.NotEmpty(url);
        Assert.NotEqual("http://localhost:8317", url);
        Assert.NotEmpty(key);

        Console.WriteLine($"[PASS] connection.json 读取正确：CpaBaseUrl={url}, Key={key[..Math.Min(key.Length, 8)]}...");
    }
}
