using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 本地登录鉴权服务：管理密码哈希、会话令牌和 appsettings 持久化。
/// </summary>
public sealed class AuthService
{
    /// <summary>
    /// 认证 Cookie 名称。
    /// </summary>
    public const string AuthCookieName = "codex_patrol_auth";

    /// <summary>
    /// 密码哈希格式前缀，用于识别 PBKDF2-SHA256 格式。
    /// </summary>
    private const string HashPrefix = "PBKDF2-SHA256";

    /// <summary>
    /// 盐值长度（字节）。
    /// </summary>
    private const int SaltSize = 16;

    /// <summary>
    /// 哈希值长度（字节）。
    /// </summary>
    private const int HashSize = 32;

    /// <summary>
    /// PBKDF2 迭代次数。
    /// </summary>
    private const int IterationCount = 100_000;

    /// <summary>
    /// 会话令牌有效期。
    /// </summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// 应用配置（运行时实例）。
    /// </summary>
    private readonly PatrolSettings _settings;

    /// <summary>
    /// appsettings.json 文件路径。
    /// </summary>
    private readonly string _appSettingsPath;

    /// <summary>
    /// 配置写入锁。
    /// </summary>
    private readonly object _settingsLock = new();

    /// <summary>
    /// 构造 AuthService，默认使用应用基目录查找 appsettings.json。
    /// </summary>
    public AuthService(PatrolSettings settings, string? baseDirectory = null)
    {
        _settings = settings;
        _appSettingsPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "appsettings.json");
    }

    /// <summary>
    /// 检查是否已配置登录密码。
    /// </summary>
    public bool HasPasswordConfigured()
    {
        return !string.IsNullOrWhiteSpace(_settings.LoginPasswordHash);
    }

    /// <summary>
    /// 验证密码是否正确。
    /// </summary>
    public bool VerifyPassword(string password)
    {
        if (!HasPasswordConfigured() || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        return VerifyHash(password, _settings.LoginPasswordHash);
    }

    /// <summary>
    /// 设置登录密码，要求不少于 8 位，成功后自动持久化哈希到 appsettings.json。
    /// </summary>
    public bool TrySetPassword(string password, out string error)
    {
        if (password.Length < 8)
        {
            error = "密码长度不能少于 8 位";
            return false;
        }

        lock (_settingsLock)
        {
            _settings.LoginPasswordHash = HashPassword(password);
            SavePasswordHash();
        }

        error = "";
        return true;
    }

    /// <summary>
    /// 创建会话令牌，格式为 Base64Url(payload).Base64Url(signature)，
    /// payload 包含过期时间和随机数，signature 用 HMAC-SHA256 签名。
    /// </summary>
    public string CreateSessionToken()
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"{expiresAt}.{nonce}";
        var signature = ComputeTokenSignature(payload);
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Base64UrlEncode(signature)}";
    }

    /// <summary>
    /// 验证会话令牌是否有效：签名正确且未过期。
    /// </summary>
    public bool IsAuthenticated(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !HasPasswordConfigured())
        {
            return false;
        }

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var expectedSignature = ComputeTokenSignature(payload);
        if (!CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature))
        {
            return false;
        }

        var payloadParts = payload.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (payloadParts.Length != 2 || !long.TryParse(payloadParts[0], out var expiresAtUnix))
        {
            return false;
        }

        return DateTimeOffset.UtcNow < DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
    }

    /// <summary>
    /// 作废会话（当前为空实现，重置密码后旧令牌会因签名密钥变化而自动失效）。
    /// </summary>
    public void RevokeSession(string? token)
    {
    }

    /// <summary>
    /// 从请求 Cookie 中提取会话令牌。
    /// </summary>
    public string? GetSessionToken(HttpRequest request)
    {
        return request.Cookies.TryGetValue(AuthCookieName, out var token) ? token : null;
    }

    /// <summary>
    /// 向响应中追加认证 Cookie。
    /// </summary>
    public void AppendAuthCookie(HttpResponse response, string token, bool isHttps)
    {
        response.Cookies.Append(AuthCookieName, token, BuildCookieOptions(isHttps, persistent: true));
    }

    /// <summary>
    /// 清除认证 Cookie（设为过期）。
    /// </summary>
    public void ClearAuthCookie(HttpResponse response, bool isHttps)
    {
        response.Cookies.Delete(AuthCookieName, BuildCookieOptions(isHttps, persistent: false));
    }

    /// <summary>
    /// 将密码哈希写回 appsettings.json，避免明文落盘。
    /// </summary>
    private void SavePasswordHash()
    {
        try
        {
            JsonObject root;
            if (File.Exists(_appSettingsPath))
            {
                root = JsonNode.Parse(File.ReadAllText(_appSettingsPath)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var patrolSettings = root["PatrolSettings"] as JsonObject ?? new JsonObject();
            patrolSettings["LoginPasswordHash"] = _settings.LoginPasswordHash;
            root["PatrolSettings"] = patrolSettings;

            File.WriteAllText(_appSettingsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch
        {
            // 写入失败不中断主流程
        }
    }

    /// <summary>
    /// 使用 PBKDF2-SHA256 对密码进行哈希，格式为 PBKDF2-SHA256$iterations$salt$hash。
    /// </summary>
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, IterationCount, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', HashPrefix, IterationCount, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>
    /// 验证密码与存储的哈希是否匹配，使用固定时间比较防止时序攻击。
    /// </summary>
    private static bool VerifyHash(string password, string storedHash)
    {
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !string.Equals(parts[0], HashPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    /// <summary>
    /// 使用当前密码哈希派生签名密钥，重置密码后旧登录态会自动失效。
    /// </summary>
    private byte[] ComputeTokenSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.LoginPasswordHash));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    /// Base64 URL 安全编码，去除填充并替换 +/ 为 -_。
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Base64 URL 安全解码，自动补齐填充并还原 -_ 为 +/。
    /// </summary>
    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        var remainder = normalized.Length % 4;
        if (remainder > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        }

        return Convert.FromBase64String(normalized);
    }

    /// <summary>
    /// 构建 Cookie 选项，设置 HttpOnly、SameSite、Secure 等安全属性。
    /// </summary>
    private static CookieOptions BuildCookieOptions(bool isHttps, bool persistent)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = isHttps,
            Path = "/",
            Expires = persistent ? DateTimeOffset.UtcNow.Add(SessionLifetime) : DateTimeOffset.UtcNow.AddDays(-1),
        };
    }
}
