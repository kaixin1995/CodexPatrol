using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 统一处理本地优先级顺序到 CPA 远端 priority 的同步。
/// </summary>
internal static class PriorityRoutingRemoteSync
{
    public static async Task<string?> TrySyncAsync(
        RuntimeStore store,
        CpaClient cpa,
        PatrolSiteSettings settings,
        List<AccountPriority> priorities,
        string siteId,
        CancellationToken ct,
        string contextMessage = "本地配置已保存")
    {
        if (priorities.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.CpaBaseUrl))
        {
            return LogWarning(store, siteId, $"{contextMessage}，但未配置 CPA 地址，无法同步远端优先级");
        }

        if (string.IsNullOrWhiteSpace(settings.ManagementKey))
        {
            return LogWarning(store, siteId, $"{contextMessage}，但未配置 ManagementKey，无法同步 CPA 优先级");
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
                return LogWarning(store, siteId,
                    $"{contextMessage}，但 CPA 存在重名账号 {duplicateRemoteGroup.Key}，已停止优先级同步");
            }

            var remoteByName = remoteGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var exceptions = store.GetExceptions(siteId);
            var orderedPriorities = priorities
                // 例外账号和待首检账号都不应提前影响 CPA 远端优先级。
                .Where(priority => !string.IsNullOrWhiteSpace(priority.Name)
                    && !priority.PendingFirstInspection
                    && !exceptions.Contains(priority.Name))
                .OrderBy(priority => priority.Priority)
                .ThenBy(priority => priority.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (orderedPriorities.Count == 0)
            {
                return null;
            }

            var missingNames = orderedPriorities
                .Where(priority => !remoteByName.ContainsKey(priority.Name))
                .Select(priority => priority.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingNames.Count > 0)
            {
                return LogWarning(store, siteId,
                    $"{contextMessage}，但以下账号未在 CPA 中找到，已停止优先级同步：{string.Join("、", missingNames)}");
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
                store.AddOperationLog("priority", "priorityRouting", "system",
                    $"{contextMessage}，CPA 优先级无需变更",
                    siteId: siteId);
                return null;
            }

            foreach (var update in updates)
            {
                await cpa.UpdateAccountPriorityAsync(settings, update.Name, update.Priority, ct);
            }

            store.AddOperationLog("priority", "priorityRouting", "system",
                $"{contextMessage}，并已同步 CPA 优先级，共更新 {updates.Count} 个账号",
                siteId: siteId);
            return null;
        }
        catch (Exception ex)
        {
            return LogWarning(store, siteId, $"{contextMessage}，但同步 CPA 优先级失败：{ex.Message}");
        }
    }

    private static string LogWarning(RuntimeStore store, string siteId, string message)
    {
        store.AddOperationLog("priority", "priorityRouting", "system", message, "warning", siteId: siteId);
        return message;
    }
}
