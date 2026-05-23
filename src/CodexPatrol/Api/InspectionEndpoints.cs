using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 巡检相关 API 端点。
/// </summary>
public static class InspectionEndpoints
{
    /// <summary>
    /// 注册巡检相关路由。
    /// </summary>
    public static RouteGroupBuilder MapInspectionApi(this RouteGroupBuilder group)
    {
        // 手动触发一次完整巡检。
        group.MapPost("/run", async (bool? force, string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            // 防止并发执行，检查是否已有巡检或轮询任务在运行。
            if (store.IsPolling(resolvedSiteId) || IsRuntimeBusy(store, resolvedSiteId))
            {
                return Results.Conflict(new ErrorResponse { Error = "当前已有任务正在运行中，请稍后再试" });
            }

            var forceRefresh = force == true;
            store.SetPollingState(true, resolvedSiteId);
            store.SetLastRunStartedAt(DateTime.UtcNow, resolvedSiteId);
            store.StartProgress("inspection", "manual", 0, "开始加载巡检账号", resolvedSiteId);
            store.AddOperationLog("inspection", "inspection", "manual", forceRefresh ? "开始强制手动巡检" : "开始手动巡检", siteId: resolvedSiteId);

            try
            {
                // 加载待巡检账号（不含例外名单中的账号）。
                var settings = store.GetSettings(resolvedSiteId);
                var candidates = await engine.LoadCandidatesAsync(resolvedSiteId, includeExceptions: false, ct);
                store.UpdateProgress("prepare", $"已加载 {candidates.Count} 个待巡检账号", total: candidates.Count, processed: 0, siteId: resolvedSiteId);

                // 没有待巡检账号时直接返回空结果。
                if (candidates.Count == 0)
                {
                    var emptyResult = new InspectionRunResult
                    {
                        TotalAccounts = 0,
                        ProbedCount = 0,
                        Status = "completed",
                        StartedAt = store.GetLastRunStartedAt(resolvedSiteId),
                        FinishedAt = DateTime.UtcNow,
                    };

                    store.SetLastRun(emptyResult, resolvedSiteId);
                    store.SetLastRunFinishedAt(emptyResult.FinishedAt, resolvedSiteId);
                    store.CompleteProgress("本次没有可巡检账号", resolvedSiteId);
                    store.AddOperationLog("inspection", "inspection", "manual", "手动巡检结束，本次没有可巡检账号", siteId: resolvedSiteId);
                    store.SetPollingState(false, resolvedSiteId);
                    return Results.Ok(emptyResult);
                }

                // 逐个探测账号并收集巡检决策。
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
                            $"已探测 {processed}/{total} 个账号",
                            total: total,
                            processed: processed,
                            currentAccountName: decision.AccountName,
                            currentDisplayAccount: decision.DisplayAccount,
                            siteId: resolvedSiteId);
                        store.AddOperationLog(
                            "inspection",
                            "inspection",
                            "manual",
                            BuildInspectionProbeLogMessage(store, resolvedSiteId, decision),
                            string.IsNullOrWhiteSpace(decision.Error) ? "info" : "warning",
                            decision.AccountName,
                            decision.DisplayAccount,
                            resolvedSiteId);
                        return Task.CompletedTask;
                    },
                    onBatchDelay: (batchIndex, totalBatches, delay) =>
                    {
                        store.UpdateProgress(
                            "delay",
                            $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续",
                            siteId: resolvedSiteId);
                        store.AddOperationLog(
                            "inspection",
                            "inspection",
                            "manual",
                            $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续",
                            siteId: resolvedSiteId);
                        return Task.CompletedTask;
                    },
                    ct: ct);

                var outcomes = new List<ActionOutcome>();
                var mode = ResolveAutoActionMode(settings.AutoActionMode);
                // 按自动动作模式筛选需要执行的账号。
                var actionItems = InspectionEngine.FilterAutoActionItems(mode, settings.AutoEnableRecovered, decisions);
                store.AddOperationLog(
                    "inspection",
                    "inspection",
                    "manual",
                    $"手动巡检决策汇总：保留 {decisions.Count(decision => decision.Action == InspectionAction.Keep)}，禁用建议 {decisions.Count(decision => decision.Action == InspectionAction.Disable)}，启用建议 {decisions.Count(decision => decision.Action == InspectionAction.Enable)}，删除建议 {decisions.Count(decision => decision.Action == InspectionAction.Delete)}；自动模式 {mode}；自动启用 {(settings.AutoEnableRecovered ? "开启" : "关闭")}；待执行动作 {actionItems.Count} 个",
                    siteId: resolvedSiteId);

                // 有自动动作需要执行时进入此分支。
                if (actionItems.Count > 0)
                {
                    store.UpdateProgress(
                        "actions",
                        $"开始执行 {actionItems.Count} 个自动动作",
                        total: candidates.Count,
                        processed: decisions.Count,
                        actionTotal: actionItems.Count,
                        actionProcessed: 0,
                        siteId: resolvedSiteId);
                    store.AddOperationLog("inspection", "inspection", "manual", $"开始执行 {actionItems.Count} 个自动动作", siteId: resolvedSiteId);

                    outcomes = await engine.ExecuteActionsAsync(
                        resolvedSiteId,
                        actionItems,
                        settings.ActionWorkers,
                        onProgress: (outcome, processed, total) =>
                        {
                            store.UpdateProgress(
                                "actions",
                                $"已执行 {processed}/{total} 个自动动作",
                                total: candidates.Count,
                                processed: decisions.Count,
                                actionTotal: total,
                                actionProcessed: processed,
                                currentAccountName: outcome.FileName,
                                currentDisplayAccount: outcome.DisplayAccount,
                                siteId: resolvedSiteId);
                            store.AddOperationLog(
                                "inspection",
                                "inspection",
                                "manual",
                                outcome.Success
                                    ? $"自动{ResolveOutcomeLabel(outcome.Action)}成功"
                                    : $"自动{ResolveOutcomeLabel(outcome.Action)}失败：{outcome.Error}",
                                outcome.Success ? "info" : "error",
                                outcome.FileName,
                                outcome.DisplayAccount,
                                resolvedSiteId);
                            return Task.CompletedTask;
                        },
                        ct);

                    // 执行完自动动作后刷新账号状态以保持同步。
                    await RefreshAccountsAfterActionsAsync(engine, store, resolvedSiteId, ct);
                }
                else
                {
                    store.AddOperationLog("inspection", "inspection", "manual", "本轮手动巡检没有可执行的自动动作", siteId: resolvedSiteId);
                }

                // 汇总巡检结果并写入运行时状态。
                var runResult = BuildRunResult(candidates.Count, decisions, outcomes, store.GetLastRunStartedAt(resolvedSiteId), DateTime.UtcNow, "completed");
                store.SetLastRun(runResult, resolvedSiteId);
                store.SetLastRunFinishedAt(runResult.FinishedAt, resolvedSiteId);
                store.CompleteProgress($"手动巡检完成，共探测 {runResult.ProbedCount} 个账号", resolvedSiteId);
                store.AddOperationLog(
                    "inspection",
                    "inspection",
                    "manual",
                    $"手动巡检完成：禁用建议 {runResult.DisableCount}，启用建议 {runResult.EnableCount}，删除建议 {runResult.DeleteCount}",
                    siteId: resolvedSiteId);
                store.SetPollingState(false, resolvedSiteId);

                return Results.Ok(runResult);
            }
            catch (Exception ex)
            {
                store.SetLastRunFinishedAt(DateTime.UtcNow, resolvedSiteId);
                store.FailProgress($"手动巡检失败：{ex.Message}", resolvedSiteId);
                store.AddOperationLog("inspection", "inspection", "manual", $"手动巡检失败：{ex.Message}", "error", siteId: resolvedSiteId);
                store.AddExceptionLog("inspection", "inspection", "manual", ex, "手动巡检异常", siteId: resolvedSiteId);
                store.SetPollingState(false, resolvedSiteId);
                return Results.Problem(ex.Message);
            }
        });

        // 手动执行指定的巡检动作列表。
        group.MapPost("/execute", async (List<InspectionDecision> items, string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var settings = store.GetSettings(siteId);
            var outcomes = await engine.ExecuteActionsAsync(settings.SiteId, items, settings.ActionWorkers, ct: ct);
            return Results.Ok(outcomes);
        });

        // 获取当前巡检状态（是否正在轮询、下次计划时间等）。
        group.MapGet("/status", (RuntimeStore store, string? siteId) =>
        {
            var settings = store.GetSettings(siteId);
            return Results.Ok(new InspectionStatusResponse
            {
                IsPolling = store.IsPolling(settings.SiteId),
                NextScheduledAt = store.GetNextScheduledAt(settings.SiteId),
                NextResetCheckAt = store.GetNextResetCheckAt(settings.SiteId),
                LastRunStartedAt = store.GetLastRunStartedAt(settings.SiteId),
                LastRunFinishedAt = store.GetLastRunFinishedAt(settings.SiteId),
                AutoPollingEnabled = settings.AutoPollingEnabled,
                PollIntervalMinutes = settings.PollIntervalMinutes,
            });
        });

        // 获取最近一次巡检的运行结果。
        group.MapGet("/last-run", (RuntimeStore store, string? siteId) =>
        {
            var settings = store.GetSettings(siteId);
            var lastRun = store.GetLastRun(settings.SiteId);
            return lastRun is null ? Results.NoContent() : Results.Ok(lastRun);
        });

        // 启动自动轮询。
        group.MapPost("/auto/start", (RuntimeStore store, string? siteId) =>
        {
            var settings = store.GetSettings(siteId);
            store.UpdateSettings(settings.SiteId, current => current.AutoPollingEnabled = true);
            // 重置常规巡检时间以立即触发，同时清空重置检测时间等待后台重新计算。
            store.SetNextScheduledAt(DateTime.MinValue, settings.SiteId);
            store.SetNextResetCheckAt(DateTime.MinValue, settings.SiteId);
            store.AddOperationLog("inspection", "inspection", "manual", "已启动自动轮询", siteId: settings.SiteId);
            return Results.Ok(new AutoPollingResponse { AutoPollingEnabled = true });
        });

        // 停止自动轮询。
        group.MapPost("/auto/stop", (RuntimeStore store, string? siteId) =>
        {
            var settings = store.GetSettings(siteId);
            store.UpdateSettings(settings.SiteId, current => current.AutoPollingEnabled = false);
            store.SetNextScheduledAt(DateTime.MinValue, settings.SiteId);
            store.SetNextResetCheckAt(DateTime.MinValue, settings.SiteId);
            store.AddOperationLog("inspection", "inspection", "manual", "已停止自动轮询", "warning", siteId: settings.SiteId);
            return Results.Ok(new AutoPollingResponse { AutoPollingEnabled = false });
        });

        // 刷新所有账号额度（含例外名单中的账号）。
        group.MapPost("/refresh-quotas", async (bool? force, string? siteId, InspectionEngine engine, RuntimeStore store, CancellationToken ct) =>
        {
            var resolvedSiteId = store.ResolveSiteId(siteId);
            // 防止并发执行。
            if (store.IsPolling(resolvedSiteId) || IsRuntimeBusy(store, resolvedSiteId))
            {
                return Results.Conflict(new ErrorResponse { Error = "当前已有任务正在运行中，请稍后再试" });
            }

            var forceRefresh = force == true;
            var settings = store.GetSettings(resolvedSiteId);
            store.StartProgress("quotaRefresh", "manual", 0, "开始刷新全部额度", resolvedSiteId);
            store.AddOperationLog("quota", "quotaRefresh", "manual", forceRefresh ? "开始强制刷新全部额度" : "开始刷新全部额度", siteId: resolvedSiteId);

            try
            {
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
                store.AddExceptionLog("quota", "quotaRefresh", "manual", ex, "巡检页刷新全部额度异常", siteId: resolvedSiteId);
                return Results.Problem(ex.Message);
            }
        });

        return group;
    }

    /// <summary>
    /// 检查指定站点是否有正在执行的任务。
    /// </summary>
    private static bool IsRuntimeBusy(RuntimeStore store, string siteId)
    {
        return store.GetProgress(siteId).Status == "running";
    }

    /// <summary>
    /// 将字符串解析为自动动作模式枚举，无法识别时默认为 None。
    /// </summary>
    private static AutoActionMode ResolveAutoActionMode(string value)
    {
        return Enum.TryParse<AutoActionMode>(value, true, out var mode)
            ? mode
            : AutoActionMode.None;
    }

    /// <summary>
    /// 根据巡检决策和执行结果汇总一次巡检的运行结果。
    /// </summary>
    private static InspectionRunResult BuildRunResult(
        int totalAccounts,
        List<InspectionDecision> decisions,
        List<ActionOutcome> outcomes,
        DateTime startedAt,
        DateTime finishedAt,
        string status)
    {
        return new InspectionRunResult
        {
            Decisions = decisions,
            ActionOutcomes = outcomes,
            TotalAccounts = totalAccounts,
            ProbedCount = decisions.Count,
            DeleteCount = decisions.Count(decision => decision.Action == InspectionAction.Delete),
            DisableCount = decisions.Count(decision => decision.Action == InspectionAction.Disable),
            EnableCount = decisions.Count(decision => decision.Action == InspectionAction.Enable),
            KeepCount = decisions.Count(decision => decision.Action == InspectionAction.Keep),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Status = status,
        };
    }

    /// <summary>
    /// 自动动作执行完毕后重新加载账号列表以同步最新状态。
    /// </summary>
    private static async Task RefreshAccountsAfterActionsAsync(
        InspectionEngine engine,
        RuntimeStore store,
        string siteId,
        CancellationToken ct)
    {
        try
        {
            var refreshed = await engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
            store.SetAccounts(refreshed, siteId);
        }
        catch (Exception ex)
        {
            store.AddOperationLog("inspection", "inspection", "manual", $"刷新账号状态失败：{ex.Message}", "warning", siteId: siteId);
        }
    }

    /// <summary>
    /// 根据探测结果拼接巡检日志消息，包含缓存命中或实时请求标记。
    /// </summary>
    private static string BuildInspectionProbeLogMessage(RuntimeStore store, string siteId, InspectionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Error))
        {
            return $"探测失败，建议{ResolveDecisionLabel(decision.Action)}：{decision.Error}";
        }

        var quota = store.GetQuota(decision.AccountName, siteId);
        if (quota?.FromCache == true)
        {
            var cacheReason = string.IsNullOrWhiteSpace(quota.CacheReason) ? "命中缓存" : quota.CacheReason;
            var mode = cacheReason.Contains("跳过", StringComparison.OrdinalIgnoreCase) ? "跳过检查" : "缓存复用";
            return $"探测完成（{mode}）：{cacheReason}；建议{ResolveDecisionLabel(decision.Action)}：{decision.Reason}";
        }

        var refreshedAtText = quota?.RefreshedAt != DateTime.MinValue ? $"，刷新时间 {quota.RefreshedAt:yyyy-MM-dd HH:mm:ss} UTC" : "";
        return $"探测完成（真实请求{refreshedAtText}），建议{ResolveDecisionLabel(decision.Action)}：{decision.Reason}";
    }

    /// <summary>
    /// 根据探测结果拼接额度刷新日志消息，包含缓存命中原因。
    /// </summary>
    private static string BuildQuotaRefreshLogMessage(RuntimeStore store, string siteId, InspectionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Error))
        {
            return $"额度刷新失败：{decision.Error}";
        }

        var quota = store.GetQuota(decision.AccountName, siteId);
        var source = quota?.FromCache == true ? "命中缓存" : "实时请求";
        var cacheReason = quota?.FromCache == true && !string.IsNullOrWhiteSpace(quota.CacheReason)
            ? $"（{quota.CacheReason}）"
            : "";
        return $"额度刷新完成：{source}{cacheReason}";
    }

    /// <summary>
    /// 将巡检动作枚举转换为中文标签。
    /// </summary>
    private static string ResolveDecisionLabel(InspectionAction action)
    {
        return action switch
        {
            InspectionAction.Delete => "删除",
            InspectionAction.Disable => "禁用",
            InspectionAction.Enable => "启用",
            _ => "保留",
        };
    }

    /// <summary>
    /// 将执行动作字符串转换为中文标签。
    /// </summary>
    private static string ResolveOutcomeLabel(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "delete" => "删除",
            "disable" => "禁用",
            "enable" => "启用",
            _ => action,
        };
    }
}
