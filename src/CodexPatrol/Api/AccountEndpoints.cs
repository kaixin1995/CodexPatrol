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
}
