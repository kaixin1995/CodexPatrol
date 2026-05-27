using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 设置相关 API 端点。
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// 注册设置相关路由。
    /// </summary>
    public static RouteGroupBuilder MapSettingsApi(this RouteGroupBuilder group)
    {
        // 获取站点列表及当前选中站点。
        group.MapGet("/sites", (RuntimeStore store, string? siteId) =>
        {
            var selectedSiteId = store.ResolveSiteId(siteId);
            var sites = store.GetSites()
                .Select(BuildSiteOption)
                .ToList();
            return Results.Ok(new SiteListResponse
            {
                SelectedSiteId = selectedSiteId,
                Sites = sites,
            });
        });

        // 创建新站点。
        group.MapPost("/sites", (SaveSettingsRequest payload, RuntimeStore store) =>
        {
            var created = store.CreateSite(payload);
            return Results.Ok(BuildSettingsResponse(created));
        });

        // 删除指定站点。
        group.MapDelete("/sites/{siteId}", (string siteId, RuntimeStore store) =>
        {
            if (!store.DeleteSite(siteId, out var error))
            {
                return Results.BadRequest(new ErrorResponse { Error = error });
            }

            return Results.Ok(new SaveSettingsResponse { Success = true });
        });

        // 获取当前站点的完整设置。
        group.MapGet("/", (RuntimeStore store, string? siteId) =>
        {
            var settings = store.GetSettings(siteId);
            return Results.Ok(BuildSettingsResponse(settings));
        });

        // 保存当前站点设置。
        group.MapPut("/", (SaveSettingsRequest payload, RuntimeStore store) =>
        {
            store.ApplySettings(payload);
            return Results.Ok(new SaveSettingsResponse { Success = true });
        });

        // 获取优先级路由状态和配置。
        group.MapGet("/priority-routing", (RuntimeStore store, string? siteId) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            var settings = store.GetSettings(resolvedSiteId);
            var priorities = store.GetAccountPriorities(resolvedSiteId);

            return Results.Ok(BuildPriorityRoutingStatusResponse(settings, priorities));
        });

        // 更新优先级路由配置（开关 + 优先级列表）。
        group.MapPut("/priority-routing", async (UpdatePriorityRoutingRequest payload, RuntimeStore store, CpaClient cpa, string? siteId, CancellationToken ct) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);

            // 更新优先级路由开关和最少保持数
            store.UpdateSettings(resolvedSiteId, settings =>
            {
                settings.PriorityRoutingEnabled = payload.PriorityRoutingEnabled;
                if (payload.PriorityMinActiveCount > 0)
                {
                    settings.PriorityMinActiveCount = payload.PriorityMinActiveCount;
                }
            });

            // 更新优先级列表
            if (payload.AccountPriorities is not null)
            {
                store.SetAccountPriorities(
                    payload.AccountPriorities
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.Last())
                        .Select(p => new AccountPriority { Name = p.Name, Priority = Math.Max(1, p.Priority) })
                        .ToList(),
                    resolvedSiteId);
            }

            var settings = store.GetSettings(resolvedSiteId);
            var priorities = store.GetAccountPriorities(resolvedSiteId);

            store.AddOperationLog("system", "settings", "system",
                $"优先级路由配置已更新，共 {priorities.Count} 个账号",
                siteId: resolvedSiteId);

            var cpaPrioritySyncWarning = await TrySyncCpaPrioritiesAsync(store, cpa, settings, priorities, resolvedSiteId, ct);

            return Results.Ok(BuildPriorityRoutingStatusResponse(settings, priorities, cpaPrioritySyncWarning));
        });

        return group;
    }

    /// <summary>
    /// 构造优先级路由状态响应。
    /// </summary>
    private static PriorityRoutingStatusResponse BuildPriorityRoutingStatusResponse(
        PatrolSiteSettings settings,
        List<AccountPriority> priorities,
        string? cpaPrioritySyncWarning = null)
    {
        return new PriorityRoutingStatusResponse
        {
            PriorityRoutingEnabled = settings.PriorityRoutingEnabled,
            PriorityMinActiveCount = settings.PriorityMinActiveCount,
            CpaPrioritySyncWarning = cpaPrioritySyncWarning,
            AccountPriorities = priorities
                .Select(p => new AccountPriorityResponse { Name = p.Name, Priority = p.Priority })
                .ToList(),
        };
    }

    /// <summary>
    /// 将本地优先级顺序安全地增量同步到 CPA。
    /// </summary>
    private static async Task<string?> TrySyncCpaPrioritiesAsync(
        RuntimeStore store,
        CpaClient cpa,
        PatrolSiteSettings settings,
        List<AccountPriority> priorities,
        string siteId,
        CancellationToken ct)
    {
        if (priorities.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.CpaBaseUrl))
        {
            return LogCpaPrioritySyncWarning(store, siteId, "本地配置已保存，但未配置 CPA 地址，无法同步远端优先级");
        }

        if (string.IsNullOrWhiteSpace(settings.ManagementKey))
        {
            return LogCpaPrioritySyncWarning(store, siteId, "本地配置已保存，但未配置 ManagementKey，无法同步 CPA 优先级");
        }

        try
        {
            var remoteGroups = (await cpa.GetAuthFilesAsync(settings, ct)).Files
                .Where(file => !string.IsNullOrWhiteSpace(file.Name))
                .GroupBy(file => file.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var duplicateRemoteGroup = remoteGroups.FirstOrDefault(group => group.Count() > 1);
            if (duplicateRemoteGroup is not null)
            {
                return LogCpaPrioritySyncWarning(store, siteId,
                    $"本地配置已保存，但 CPA 存在重名账号 {duplicateRemoteGroup.Key}，已停止优先级同步");
            }

            var remoteByName = remoteGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var orderedPriorities = priorities
                .Where(priority => !string.IsNullOrWhiteSpace(priority.Name))
                .OrderBy(priority => priority.Priority)
                .ThenBy(priority => priority.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missingNames = orderedPriorities
                .Where(priority => !remoteByName.ContainsKey(priority.Name))
                .Select(priority => priority.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingNames.Count > 0)
            {
                return LogCpaPrioritySyncWarning(store, siteId,
                    $"本地配置已保存，但以下账号未在 CPA 中找到，已停止优先级同步：{string.Join("、", missingNames)}");
            }

            var updates = new List<AuthFilePriorityPatchRequest>();
            for (var index = 0; index < orderedPriorities.Count; index++)
            {
                var localPriority = orderedPriorities[index];
                var remote = remoteByName[localPriority.Name];
                var desiredPriority = orderedPriorities.Count - index;
                if (remote.Priority == desiredPriority)
                {
                    continue;
                }

                updates.Add(new AuthFilePriorityPatchRequest
                {
                    Name = remote.Name,
                    Priority = desiredPriority,
                });
            }

            if (updates.Count == 0)
            {
                store.AddOperationLog("system", "priorityRouting", "system",
                    "优先级路由配置已保存，CPA 优先级无需变更",
                    siteId: siteId);
                return null;
            }

            foreach (var update in updates)
            {
                await cpa.UpdateAccountPriorityAsync(settings, update.Name, update.Priority, ct);
            }

            store.AddOperationLog("system", "priorityRouting", "system",
                $"优先级路由配置已保存，并已同步 CPA 优先级，共更新 {updates.Count} 个账号",
                siteId: siteId);
            return null;
        }
        catch (Exception ex)
        {
            return LogCpaPrioritySyncWarning(store, siteId, $"本地配置已保存，但同步 CPA 优先级失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 记录 CPA 优先级同步告警并返回提示文本。
    /// </summary>
    private static string LogCpaPrioritySyncWarning(RuntimeStore store, string siteId, string message)
    {
        store.AddOperationLog("system", "priorityRouting", "system", message, "warning", siteId: siteId);
        return message;
    }

    /// <summary>
    /// 将站点设置转换为站点选项响应对象（用于站点列表展示）。
    /// </summary>
    private static SiteOptionResponse BuildSiteOption(PatrolSiteSettings settings)
    {
        return new SiteOptionResponse
        {
            SiteId = settings.SiteId,
            SiteName = settings.Name,
            SiteEnabled = settings.Enabled,
            CpaBaseUrl = settings.CpaBaseUrl,
            HasManagementKey = !string.IsNullOrWhiteSpace(settings.ManagementKey),
            Provider = settings.Provider,
        };
    }

    /// <summary>
    /// 将站点设置转换为完整的设置响应对象。
    /// </summary>
    private static SettingsResponse BuildSettingsResponse(PatrolSiteSettings settings)
    {
        return new SettingsResponse
        {
            SiteId = settings.SiteId,
            SiteName = settings.Name,
            SiteEnabled = settings.Enabled,
            CpaBaseUrl = settings.CpaBaseUrl,
            HasManagementKey = !string.IsNullOrWhiteSpace(settings.ManagementKey),
            AutoPollingEnabled = settings.AutoPollingEnabled,
            PollIntervalMinutes = settings.PollIntervalMinutes,
            PollRandomDelayMinMinutes = settings.PollRandomDelayMinMinutes,
            PollRandomDelayMaxMinutes = settings.PollRandomDelayMaxMinutes,
            ProbeWorkers = settings.ProbeWorkers,
            ProbeBatchDelayMinMs = settings.ProbeBatchDelayMinMs,
            ProbeBatchDelayMaxMs = settings.ProbeBatchDelayMaxMs,
            ActionWorkers = settings.ActionWorkers,
            TimeoutMs = settings.TimeoutMs,
            RetryCount = settings.RetryCount,
            AutoActionMode = settings.AutoActionMode,
            AutoEnableRecovered = settings.AutoEnableRecovered,
            UsedPercentThreshold = settings.UsedPercentThreshold,
            Provider = settings.Provider,
            PriorityRoutingEnabled = settings.PriorityRoutingEnabled,
            PriorityMinActiveCount = settings.PriorityMinActiveCount,
        };
    }
}
