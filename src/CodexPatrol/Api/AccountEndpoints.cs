using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 账号列表相关 API 端点。
/// </summary>
public static class AccountEndpoints
{
    /// <summary>
    /// 注册账号管理相关路由。
    /// </summary>
    public static RouteGroupBuilder MapAccountApi(this RouteGroupBuilder group)
    {
        // 获取当前站点的账号列表。
        group.MapGet("/", (RuntimeStore store, string? siteId) =>
        {
            var accounts = store.GetAccounts(siteId);
            return Results.Ok(accounts);
        });

        // 从 CPA 后端重新加载账号列表。
        group.MapPost("/refresh", async (string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var candidates = await engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
            store.SetAccounts(candidates, siteId);
            return Results.Ok(candidates);
        });

        // 预览当前站点可安全清理的无效账号，前端确认弹窗仅展示这里返回的名单。
        group.MapGet("/cleanup-invalid/preview", (RuntimeStore store, string? siteId) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            var candidates = BuildInvalidCleanupCandidates(store, resolvedSiteId);
            return Results.Ok(new InvalidAccountCleanupPreviewResponse
            {
                TotalCandidates = candidates.Count,
                Accounts = candidates,
            });
        });

        // 批量清理无效账号：后端会再次校验，只删除仍然满足安全条件的账号，避免误删。
        group.MapPost("/cleanup-invalid", async (InvalidAccountCleanupRequest payload, string? siteId, CpaClient cpa, RuntimeStore store, CancellationToken ct) =>
        {
            var settings = store.GetSettings(siteId);
            var resolvedSiteId = settings.SiteId;
            if (store.IsPolling(resolvedSiteId) || store.GetProgress(resolvedSiteId).Status == "running")
            {
                return Results.Conflict(new ErrorResponse { Error = "当前已有任务正在运行中，请稍后再试" });
            }

            var requestedNames = (payload.AccountNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (requestedNames.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse { Error = "请先选择要清理的无效账号" });
            }

            var candidatesByName = BuildInvalidCleanupCandidates(store, resolvedSiteId)
                .ToDictionary(item => item.AccountName, StringComparer.OrdinalIgnoreCase);
            var deletedAccounts = new List<InvalidAccountCandidateResponse>();
            var skippedAccounts = new List<InvalidAccountCandidateResponse>();

            store.AddOperationLog("account", "cleanupInvalid", "manual", $"开始清理无效账号，请求数量 {requestedNames.Count}", siteId: resolvedSiteId);

            foreach (var accountName in requestedNames)
            {
                if (!candidatesByName.TryGetValue(accountName, out var candidate))
                {
                    skippedAccounts.Add(new InvalidAccountCandidateResponse
                    {
                        AccountName = accountName,
                        DisplayAccount = accountName,
                        Reason = "已跳过：当前已不满足无效账号清理条件",
                    });
                    continue;
                }

                try
                {
                    await cpa.DeleteAccountAsync(settings, accountName, ct);
                    deletedAccounts.Add(candidate);
                    store.RemoveException(accountName, resolvedSiteId);
                    store.AddOperationLog("account", "cleanupInvalid", "manual", $"已删除无效账号：{candidate.Reason}", accountName: accountName, displayAccount: candidate.DisplayAccount, siteId: resolvedSiteId);
                }
                catch (Exception ex)
                {
                    skippedAccounts.Add(new InvalidAccountCandidateResponse
                    {
                        AccountName = candidate.AccountName,
                        DisplayAccount = candidate.DisplayAccount,
                        Reason = $"删除失败：{ex.Message}",
                    });
                    store.AddOperationLog("account", "cleanupInvalid", "manual", $"删除无效账号失败：{ex.Message}", "error", accountName, candidate.DisplayAccount, resolvedSiteId);
                    store.AddExceptionLog("account", "cleanupInvalid", "manual", ex, $"删除无效账号异常：{accountName}", accountName: accountName, displayAccount: candidate.DisplayAccount, siteId: resolvedSiteId);
                }
            }

            if (deletedAccounts.Count > 0)
            {
                // 仅同步本地运行时状态里已确认删除的账号，避免页面继续展示旧数据。
                var deletedNames = new HashSet<string>(deletedAccounts.Select(item => item.AccountName), StringComparer.OrdinalIgnoreCase);
                var remainingAccounts = store.GetAccounts(resolvedSiteId)
                    .Where(account => !deletedNames.Contains(account.Name))
                    .ToList();
                store.SetAccounts(remainingAccounts, resolvedSiteId);
            }

            store.AddOperationLog("account", "cleanupInvalid", "manual", $"无效账号清理完成：已删除 {deletedAccounts.Count} 个，跳过 {skippedAccounts.Count} 个", siteId: resolvedSiteId);
            return Results.Ok(new InvalidAccountCleanupResultResponse
            {
                DeletedCount = deletedAccounts.Count,
                SkippedCount = skippedAccounts.Count,
                DeletedAccounts = deletedAccounts,
                SkippedAccounts = skippedAccounts,
            });
        });

        // 禁用指定账号。
        group.MapPost("/{accountName}/disable", async (string accountName, string? siteId, CpaClient cpa, RuntimeStore store, CancellationToken ct) =>
        {
            var settings = store.GetSettings(siteId);
            var resolvedSiteId = settings.SiteId;
            store.AddOperationLog("account", "accountDisable", "manual", $"开始禁用账号 {accountName}", accountName: accountName, siteId: resolvedSiteId);

            try
            {
                await cpa.DisableAccountAsync(settings, accountName, ct);
            }
            catch (Exception ex)
            {
                store.AddOperationLog("account", "accountDisable", "manual", $"禁用账号失败：{ex.Message}", "error", accountName, siteId: resolvedSiteId);
                store.AddExceptionLog("account", "accountDisable", "manual", ex, $"禁用账号异常：{accountName}", accountName: accountName, siteId: resolvedSiteId);
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }

            // 同步内存中的账号禁用状态，标记为手动禁用原因。
            store.UpdateAccountDisabledState(accountName, disabled: true, DisableReason.ManualDisabled, resolvedSiteId);
            store.AddOperationLog("account", "accountDisable", "manual", "账号已禁用，内存状态已同步", accountName: accountName, siteId: resolvedSiteId);
            return Results.Ok(new MessageResponse { Message = "已禁用" });
        });

        // 启用指定账号。
        group.MapPost("/{accountName}/enable", async (string accountName, string? siteId, CpaClient cpa, RuntimeStore store, CancellationToken ct) =>
        {
            var settings = store.GetSettings(siteId);
            var resolvedSiteId = settings.SiteId;
            store.AddOperationLog("account", "accountEnable", "manual", $"开始启用账号 {accountName}", accountName: accountName, siteId: resolvedSiteId);

            try
            {
                await cpa.EnableAccountAsync(settings, accountName, ct);
            }
            catch (Exception ex)
            {
                store.AddOperationLog("account", "accountEnable", "manual", $"启用账号失败：{ex.Message}", "error", accountName, siteId: resolvedSiteId);
                store.AddExceptionLog("account", "accountEnable", "manual", ex, $"启用账号异常：{accountName}", accountName: accountName, siteId: resolvedSiteId);
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }

            // 同步内存中的账号启用状态，清除禁用原因。
            store.UpdateAccountDisabledState(accountName, disabled: false, DisableReason.None, resolvedSiteId);
            store.AddOperationLog("account", "accountEnable", "manual", "账号已启用，内存状态已同步", accountName: accountName, siteId: resolvedSiteId);
            return Results.Ok(new MessageResponse { Message = "已启用" });
        });

        return group;
    }

    /// <summary>
    /// 仅挑出可安全清理的无效账号：必须是当前额度错误，且明确属于 401 / token invalidated。
    /// </summary>
    private static List<InvalidAccountCandidateResponse> BuildInvalidCleanupCandidates(RuntimeStore store, string siteId)
    {
        return store.GetAccounts(siteId)
            .Select(account =>
            {
                var quota = store.GetQuota(account.Name, siteId);
                return InspectionEngine.TryGetInvalidCredentialFailureReason(quota, out var reason)
                    ? new InvalidAccountCandidateResponse
                    {
                        AccountName = account.Name,
                        DisplayAccount = string.IsNullOrWhiteSpace(quota?.DisplayAccount)
                            ? account.Account ?? account.Email ?? account.Label ?? account.Name
                            : quota.DisplayAccount,
                        Reason = reason,
                    }
                    : null;
            })
            .Where(item => item is not null)
            .OrderBy(item => item!.DisplayAccount, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item!.AccountName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item!)
            .ToList();
    }
}
