using System.Text.Json.Serialization;
using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 例外名单相关 API 端点。
/// </summary>
public static class ExceptionEndpoints
{
    /// <summary>
    /// 注册例外名单相关路由。
    /// </summary>
    public static RouteGroupBuilder MapExceptionApi(this RouteGroupBuilder group)
    {
        // 获取当前站点的例外名单列表。
        group.MapGet("/", (RuntimeStore store, string? siteId) =>
        {
            return Results.Ok(new ExceptionsResponse { Exceptions = store.GetExceptions(siteId).ToList() });
        });

        // 添加单个账号到例外名单。
        group.MapPost("/", (ExceptionRequest payload, RuntimeStore store, string? siteId) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            var accountName = payload.AccountName.Trim();
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return Results.BadRequest(new ErrorResponse { Error = "AccountName 不能为空" });
            }

            // 确保账号存在才能加入例外名单。
            if (store.GetAccount(accountName, resolvedSiteId) is null)
            {
                return Results.BadRequest(new ErrorResponse { Error = "账号不存在，不能加入例外名单" });
            }

            store.AddException(accountName, resolvedSiteId);
            return Results.Ok(new ExceptionsResponse { Exceptions = store.GetExceptions(resolvedSiteId).ToList() });
        });

        // 整体替换例外名单。
        group.MapPut("/", (ExceptionListRequest payload, RuntimeStore store, string? siteId) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            // 去空、去重后得到最终名单。
            var exceptionNames = (payload.Exceptions ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 校验所有账号是否都存在于当前站点。
            if (exceptionNames.Any(name => store.GetAccount(name, resolvedSiteId) is null))
            {
                return Results.BadRequest(new ErrorResponse { Error = "例外名单中包含当前站点不存在的账号" });
            }

            store.SetExceptions(exceptionNames, resolvedSiteId);
            return Results.Ok(new ExceptionsResponse { Exceptions = store.GetExceptions(resolvedSiteId).ToList() });
        });

        // 从例外名单中移除指定账号。
        group.MapDelete("/{accountName}", (string accountName, RuntimeStore store, string? siteId) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            store.RemoveException(accountName, resolvedSiteId);
            return Results.Ok(new ExceptionsResponse { Exceptions = store.GetExceptions(resolvedSiteId).ToList() });
        });

        return group;
    }
}

/// <summary>
/// 添加例外名单的单条请求。
/// </summary>
public sealed class ExceptionRequest
{
    /// <summary>
    /// 要加入例外名单的账号名。
    /// </summary>
    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = "";
}

/// <summary>
/// 整体替换例外名单的请求。
/// </summary>
public sealed class ExceptionListRequest
{
    /// <summary>
    /// 新的例外名单列表，将完全覆盖旧名单。
    /// </summary>
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = [];
}
