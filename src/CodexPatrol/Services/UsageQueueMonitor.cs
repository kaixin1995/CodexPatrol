using System.Text.Json;
using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 后台轮询 CPA usage-queue，记录哪些 auth_index 有调用活动。
/// 仅维护一个 HashSet，内存占用极小。
/// </summary>
public sealed class UsageQueueMonitor : BackgroundService
{
    /// <summary>
    /// CPA 客户端，用于拉取 usage-queue 数据。
    /// </summary>
    private readonly CpaClient _cpa;

    /// <summary>
    /// 运行时状态仓库，用于记录调用活动。
    /// </summary>
    private readonly RuntimeStore _store;

    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger<UsageQueueMonitor> _logger;

    /// <summary>
    /// 轮询间隔（毫秒），默认 30 秒，需小于 CPA 队列默认保留时间（60 秒）。
    /// </summary>
    private const int PollIntervalMs = 30_000;

    /// <summary>
    /// 每次拉取的最大条目数。
    /// </summary>
    private const int PopCount = 200;

    /// <summary>
    /// 构造 UsageQueueMonitor。
    /// </summary>
    public UsageQueueMonitor(CpaClient cpa, RuntimeStore store, ILogger<UsageQueueMonitor> logger)
    {
        _cpa = cpa;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// 后台服务入口，持续轮询所有启用站点的 usage-queue。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("usage-queue 监控服务已启动");
        _store.AddOperationLog("monitor", "usageQueue", "system", "usage-queue 监控服务已启动，轮询间隔 30 秒");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllSitesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "usage-queue 轮询异常");
            }

            try
            {
                await Task.Delay(PollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _store.AddOperationLog("monitor", "usageQueue", "system", "usage-queue 监控服务已停止");
        _logger.LogInformation("usage-queue 监控服务已停止");
    }

    /// <summary>
    /// 遍历所有启用站点执行轮询。
    /// </summary>
    private async Task PollAllSitesAsync(CancellationToken ct)
    {
        foreach (var siteId in _store.GetEnabledSiteIds())
        {
            ct.ThrowIfCancellationRequested();
            await PollSiteAsync(siteId, ct);
        }
    }

    /// <summary>
    /// 轮询单个站点的 usage-queue，提取 auth_index 并标记调用活动。
    /// </summary>
    private async Task PollSiteAsync(string siteId, CancellationToken ct)
    {
        if (_store.IsUsageQueueUnsupported(siteId))
        {
            return;
        }

        var settings = _store.GetSettings(siteId);

        try
        {
            var items = await _cpa.PopUsageQueueAsync(settings, PopCount, ct);
            var accounts = _store.GetAccounts(siteId);
            var knownAuthIndexMap = BuildKnownAuthIndexMap(accounts);

            // 统计本轮队列中可识别、可匹配和新增活跃的账号数量，便于前端判断监控是否真正生效。
            var parsedAuthIndexCount = 0;
            var matchedAccountCount = 0;
            var unmatchedAuthIndexCount = 0;
            var missingAuthIndexCount = 0;
            var newActiveCount = 0;
            var matchedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawItem in items)
            {
                var authIndex = ExtractAuthIndex(rawItem);
                if (string.IsNullOrWhiteSpace(authIndex))
                {
                    missingAuthIndexCount++;
                    continue;
                }

                parsedAuthIndexCount++;
                authIndex = authIndex.Trim();

                if (knownAuthIndexMap.TryGetValue(authIndex, out var matchedAccount))
                {
                    matchedAccounts.Add(matchedAccount);
                    matchedAccountCount++;
                }
                else
                {
                    unmatchedAuthIndexCount++;
                }

                if (!_store.HasAccountUsage(siteId, authIndex))
                {
                    newActiveCount++;
                }

                _store.MarkAccountUsage(siteId, authIndex);
            }

            // 更新监控统计。
            _store.RecordUsageMonitorPoll(siteId, items.Count);

            // 首次成功拉取后标记为活跃并记录日志。
            if (!_store.IsUsageMonitorActive(siteId))
            {
                _store.SetUsageMonitorActive(siteId, true);
                _store.AddOperationLog("monitor", "usageQueue", "system",
                    $"站点 {settings.Name} 的 usage-queue 监控已激活，开始消费调用队列", siteId: siteId);
            }

            _store.AddOperationLog(
                "monitor",
                "usageQueue",
                "system",
                BuildPollSummaryMessage(settings.Name, items.Count, parsedAuthIndexCount, matchedAccountCount, unmatchedAuthIndexCount, missingAuthIndexCount, newActiveCount, matchedAccounts),
                siteId: siteId);

            if (items.Count == 0)
            {
                _store.AddOperationLog("monitor", "usageQueue", "system",
                    $"站点 {settings.Name} 本轮未获取到新的 usage-queue 记录", siteId: siteId);
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
        {
            _store.MarkUsageQueueUnsupported(siteId);
            _store.AddOperationLog("monitor", "usageQueue", "system",
                $"站点 {settings.Name} 不支持 usage-queue 接口（404），后续不再轮询，额度缓存策略将不可用", level: "warning", siteId: siteId);
            _logger.LogWarning("站点 {SiteId} 不支持 usage-queue（404），后续不再轮询", siteId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _store.AddOperationLog("monitor", "usageQueue", "system",
                $"站点 {settings.Name} 拉取 usage-queue 失败：{ex.Message}", level: "warning", siteId: siteId);

            // 暂时性错误不标记为不支持，下次继续重试。
            _logger.LogDebug(ex, "站点 {SiteId} usage-queue 轮询失败，下次继续重试", siteId);
        }
    }

    /// <summary>
    /// 根据账号列表构建 auth_index 到账号显示名的映射，便于统计匹配情况。
    /// </summary>
    private static Dictionary<string, string> BuildKnownAuthIndexMap(List<AuthFileItem> accounts)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            var authIndex = (account.Auth_Index ?? account.AuthIndex ?? string.Empty).Trim();
            if (authIndex.Length == 0)
            {
                continue;
            }

            map[authIndex] = ResolveDisplayAccount(account);
        }

        return map;
    }

    /// <summary>
    /// 生成本轮轮询摘要，明确展示拉取数量、匹配结果和新增活跃账号数量。
    /// </summary>
    private static string BuildPollSummaryMessage(
        string siteName,
        int itemsCount,
        int parsedAuthIndexCount,
        int matchedAccountCount,
        int unmatchedAuthIndexCount,
        int missingAuthIndexCount,
        int newActiveCount,
        HashSet<string> matchedAccounts)
    {
        var matchedPreview = matchedAccounts.Count > 0
            ? $"，命中账号：{string.Join('、', matchedAccounts.Take(3))}{(matchedAccounts.Count > 3 ? " 等" : string.Empty)}"
            : string.Empty;

        return $"站点 {siteName} 本轮拉取 {itemsCount} 条 usage-queue 记录，解析出 {parsedAuthIndexCount} 个 auth_index，匹配到 {matchedAccountCount} 条已知账号记录，未匹配 {unmatchedAuthIndexCount} 条，缺少 auth_index {missingAuthIndexCount} 条，新增活跃账号 {newActiveCount} 个{matchedPreview}";
    }

    /// <summary>
    /// 从账号文件中提取显示名称，按 account、email、label、name 优先级依次尝试。
    /// </summary>
    private static string ResolveDisplayAccount(AuthFileItem file)
    {
        var account = (file.Account ?? string.Empty).Trim();
        if (account.Length > 0)
        {
            return account;
        }

        var email = (file.Email ?? string.Empty).Trim();
        if (email.Length > 0)
        {
            return email;
        }

        var label = (file.Label ?? string.Empty).Trim();
        if (label.Length > 0)
        {
            return label;
        }

        return file.Name;
    }

    /// <summary>
    /// 从原始 JSON 中提取 auth_index，支持 auth_index / authIndex / AuthIndex 三种键名。
    /// </summary>
    public static string ExtractAuthIndex(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            foreach (var name in new[] { "auth_index", "authIndex", "AuthIndex" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? "";
                }
            }
        }
        catch
        {
            // 解析失败返回空
        }

        return "";
    }
}
