using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 额度相关 API 端点。
/// </summary>
public static class QuotaEndpoints
{
    /// <summary>
    /// 注册额度相关路由。
    /// </summary>
    public static RouteGroupBuilder MapQuotaApi(this RouteGroupBuilder group)
    {
        // 获取当前站点所有账号的额度信息。
        group.MapGet("/", (RuntimeStore store, string? siteId) =>
        {
            return Results.Ok(store.GetQuotas(siteId));
        });

        // 刷新所有账号的额度信息。
        group.MapPost("/refresh", async (bool? force, string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            // 防止并发执行，检查是否已有任务在运行。
            if (store.IsPolling(resolvedSiteId) || store.GetProgress(resolvedSiteId).Status == "running")
            {
                return Results.Conflict(new ErrorResponse { Error = "当前已有任务正在运行中，请稍后再试" });
            }

            var forceRefresh = force == true;
            var settings = store.GetSettings(resolvedSiteId);
            store.StartProgress("quotaRefresh", "manual", 0, "开始刷新全部额度", resolvedSiteId);
            store.AddOperationLog("quota", "quotaRefresh", "manual", forceRefresh ? "开始强制刷新全部额度" : "开始刷新全部额度", siteId: resolvedSiteId);

            try
            {
                // 加载所有账号（含例外名单中的）用于批量刷新额度。
                var candidates = await engine.LoadCandidatesAsync(resolvedSiteId, includeExceptions: true, ct);
                store.UpdateProgress("prepare", $"已加载 {candidates.Count} 个账号，准备刷新额度", total: candidates.Count, processed: 0, siteId: resolvedSiteId);

                var decisions = await engine.InspectAccountsAsync(
                    resolvedSiteId,
                    candidates,
                    settings.ProbeWorkers,
                    settings.ProbeBatchDelayMinMs,
                    settings.ProbeBatchDelayMaxMs,
                    forceRefresh: forceRefresh,
                    onProgress: (decision, processed, total) =>
                    {
                        store.UpdateProgress(
                            "probing",
                            $"已刷新 {processed}/{total} 个账号额度",
                            total: total,
                            processed: processed,
                            currentAccountName: decision.AccountName,
                            currentDisplayAccount: decision.DisplayAccount,
                            siteId: resolvedSiteId);
                        store.AddOperationLog(
                            "quota",
                            "quotaRefresh",
                            "manual",
                            BuildQuotaRefreshLogMessage(store, resolvedSiteId, decision),
                            string.IsNullOrWhiteSpace(decision.Error) ? "info" : "error",
                            decision.AccountName,
                            decision.DisplayAccount,
                            resolvedSiteId);
                        return Task.CompletedTask;
                    },
                    onBatchDelay: (batchIndex, totalBatches, delay) =>
                    {
                        store.UpdateProgress("delay", $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续", siteId: resolvedSiteId);
                        store.AddOperationLog("quota", "quotaRefresh", "manual", $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续", siteId: resolvedSiteId);
                        return Task.CompletedTask;
                    },
                    ct: ct);

                store.CompleteProgress($"全部额度刷新完成，共处理 {decisions.Count} 个账号", resolvedSiteId);
                store.AddOperationLog("quota", "quotaRefresh", "manual", $"全部额度刷新完成，共处理 {decisions.Count} 个账号", siteId: resolvedSiteId);
                return Results.Ok(new RefreshResponse { Refreshed = decisions.Count });
            }
            catch (Exception ex)
            {
                store.FailProgress($"刷新全部额度失败：{ex.Message}", resolvedSiteId);
                store.AddOperationLog("quota", "quotaRefresh", "manual", $"刷新全部额度失败：{ex.Message}", "error", siteId: resolvedSiteId);
                store.AddExceptionLog("quota", "quotaRefresh", "manual", ex, "刷新全部额度异常", siteId: resolvedSiteId);
                return Results.Problem(ex.Message);
            }
        });

        // 刷新单个账号的额度信息。
        group.MapPost("/{accountName}/refresh", async (string accountName, bool? force, string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            // 防止并发执行，检查是否已有任务在运行。
            if (store.IsPolling(resolvedSiteId) || store.GetProgress(resolvedSiteId).Status == "running")
            {
                return Results.Conflict(new ErrorResponse { Error = "当前已有任务正在运行中，请稍后再试" });
            }

            var forceRefresh = force == true;
            store.StartProgress("quotaRefresh", "manual", 1, $"开始刷新账号 {accountName} 的额度", resolvedSiteId);
            store.AddOperationLog("quota", "quotaRefresh", "manual", forceRefresh ? $"开始强制刷新账号 {accountName} 的额度" : $"开始刷新账号 {accountName} 的额度", accountName: accountName, siteId: resolvedSiteId);

            try
            {
                // 加载账号列表并查找目标账号。
                var candidates = await engine.LoadCandidatesAsync(resolvedSiteId, includeExceptions: true, ct);
                var account = candidates.FirstOrDefault(file => file.Name == accountName);
                if (account is null)
                {
                    store.FailProgress($"账号 {accountName} 不存在", resolvedSiteId);
                    store.AddOperationLog("quota", "quotaRefresh", "manual", $"账号 {accountName} 不存在", "error", accountName, siteId: resolvedSiteId);
                    return Results.NotFound(new ErrorResponse { Error = $"账号 {accountName} 不存在" });
                }

                store.UpdateProgress(
                    "probing",
                    $"正在刷新账号 {accountName} 的额度",
                    total: 1,
                    processed: 0,
                    currentAccountName: account.Name,
                    currentDisplayAccount: account.Account ?? account.Email ?? account.Label ?? account.Name,
                    siteId: resolvedSiteId);
                var decision = await engine.InspectAccountAsync(resolvedSiteId, account, null, forceRefresh: forceRefresh, ct: ct);
                store.UpdateProgress(
                    "probing",
                    $"账号 {accountName} 的额度刷新完成",
                    total: 1,
                    processed: 1,
                    currentAccountName: decision.AccountName,
                    currentDisplayAccount: decision.DisplayAccount,
                    siteId: resolvedSiteId);
                store.CompleteProgress($"账号 {accountName} 的额度已刷新", resolvedSiteId);
                store.AddOperationLog(
                    "quota",
                    "quotaRefresh",
                    "manual",
                    BuildQuotaRefreshLogMessage(store, resolvedSiteId, decision),
                    string.IsNullOrWhiteSpace(decision.Error) ? "info" : "error",
                    decision.AccountName,
                    decision.DisplayAccount,
                    resolvedSiteId);

                var quota = store.GetQuota(accountName, resolvedSiteId);
                return quota is null ? Results.NotFound() : Results.Ok(quota);
            }
            catch (Exception ex)
            {
                store.FailProgress($"刷新账号 {accountName} 额度失败：{ex.Message}", resolvedSiteId);
                store.AddOperationLog("quota", "quotaRefresh", "manual", $"刷新账号 {accountName} 额度失败：{ex.Message}", "error", accountName, siteId: resolvedSiteId);
                store.AddExceptionLog("quota", "quotaRefresh", "manual", ex, $"刷新单个账号额度异常：{accountName}", accountName: accountName, siteId: resolvedSiteId);
                return Results.Problem(ex.Message);
            }
        });

        return group;
    }

    /// <summary>
    /// 根据探测结果拼接额度刷新日志消息。
    /// </summary>
    private static string BuildQuotaRefreshLogMessage(RuntimeStore store, string siteId, InspectionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Error))
        {
            return $"额度刷新失败：{decision.Error}";
        }

        var quota = store.GetQuota(decision.AccountName, siteId);
        if (quota?.FromCache == true)
        {
            var cacheReason = string.IsNullOrWhiteSpace(quota.CacheReason) ? "命中缓存" : quota.CacheReason;
            var mode = cacheReason.Contains("跳过", StringComparison.OrdinalIgnoreCase) ? "跳过检查" : "缓存复用";
            return $"额度刷新完成：{mode}（{cacheReason}）";
        }

        var refreshedAtText = quota is { RefreshedAt: var refreshedAt } && refreshedAt != DateTime.MinValue ? $"，真实刷新时间 {refreshedAt:yyyy-MM-dd HH:mm:ss} UTC" : "";
        return $"额度刷新完成：真实请求{refreshedAtText}";
    }
}
