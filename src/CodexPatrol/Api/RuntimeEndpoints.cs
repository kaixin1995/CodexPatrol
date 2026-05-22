using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 运行时状态 API，仅暴露当前进程内存中的日志和进度。
/// </summary>
public static class RuntimeEndpoints
{
    /// <summary>
    /// 注册运行时状态相关路由。
    /// </summary>
    public static RouteGroupBuilder MapRuntimeApi(this RouteGroupBuilder group)
    {
        // 获取操作日志列表，默认返回最近 200 条。
        group.MapGet("/logs", (RuntimeStore store, int? limit, string? siteId) =>
        {
            return Results.Ok(store.GetOperationLogs(limit ?? 200, siteId));
        });

        // 获取当前任务进度。
        group.MapGet("/progress", (RuntimeStore store, string? siteId) =>
        {
            return Results.Ok(store.GetProgress(siteId));
        });

        // usage-queue 监控状态：每个站点是否活跃、是否支持、轮询统计。
        group.MapGet("/usage-monitor", (RuntimeStore store) =>
        {
            return Results.Ok(store.GetUsageMonitorStatus());
        });

        return group;
    }
}
