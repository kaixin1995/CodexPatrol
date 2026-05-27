using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 运行时状态 API，仅暴露当前进程内存中的日志和进度。
/// </summary>
public static class RuntimeEndpoints
{
    /// <summary>
    /// 当前系统版本号，统一由后端维护。
    /// </summary>
    public const string AppVersion = "V1.0.0.3";

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

        // 前端布局读取版本号，避免在多个页面脚本里重复维护。
        group.MapGet("/app-info", () =>
        {
            return Results.Ok(new AppInfoResponse
            {
                Version = AppVersion,
            });
        });

        return group;
    }
}
