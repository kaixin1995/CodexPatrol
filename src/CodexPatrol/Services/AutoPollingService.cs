using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 自动轮询后台服务：进程启动后按站点配置间隔自动执行巡检。
/// </summary>
public sealed class AutoPollingService : BackgroundService
{
    /// <summary>
    /// 巡检引擎，执行账号探测和动作。
    /// </summary>
    private readonly InspectionEngine _engine;

    /// <summary>
    /// 运行时状态仓库。
    /// </summary>
    private readonly RuntimeStore _store;

    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger<AutoPollingService> _logger;

    /// <summary>
    /// 5 小时限额窗口秒数。
    /// </summary>
    private const int FiveHourSeconds = 18_000;

    /// <summary>
    /// 周限额窗口秒数。
    /// </summary>
    private const int WeekSeconds = 604_800;

    /// <summary>
    /// 达到额度重置时间后，额外等待 1 分钟再做真实检测，避免上游额度尚未立即刷新。
    /// </summary>
    private static readonly TimeSpan ResetCheckDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 构造 AutoPollingService。
    /// </summary>
    public AutoPollingService(
        InspectionEngine engine,
        RuntimeStore store,
        ILogger<AutoPollingService> logger)
    {
        _engine = engine;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// 后台服务入口：启动时加载各站点账号，然后持续按间隔自动巡检。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);

        _logger.LogInformation("自动轮询服务已启动");

        foreach (var site in _store.GetSites().Where(site => site.Enabled))
        {
            if (!_store.HasSite(site.SiteId))
            {
                continue;
            }

            try
            {
                _store.AddOperationLog("inspection", "inspection", "system", $"自动轮询服务已启动（{site.Name}）", siteId: site.SiteId);
                var accounts = await _engine.LoadCandidatesAsync(site.SiteId, includeExceptions: true, stoppingToken);
                _store.SetAccounts(accounts, site.SiteId);
                _store.AddOperationLog("system", "startup", "system", $"启动时已同步 {accounts.Count} 个账号", siteId: site.SiteId);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (Exception ex)
            {
                if (_store.HasSite(site.SiteId))
                {
                    _store.AddOperationLog("system", "startup", "system", $"启动同步账号失败：{ex.Message}", "warning", siteId: site.SiteId);
                    _store.AddExceptionLog("system", "startup", "system", ex, "启动同步账号异常", level: "warning", siteId: site.SiteId);
                }
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var site in _store.GetSites())
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (!_store.HasSite(site.SiteId))
                {
                    continue;
                }

                try
                {
                    if (!site.Enabled)
                    {
                        _store.SetNextScheduledAt(DateTime.MinValue, site.SiteId);
                        continue;
                    }

                    if (!site.AutoPollingEnabled)
                    {
                        _store.SetNextScheduledAt(DateTime.MinValue, site.SiteId);
                        continue;
                    }

                    if (_store.IsPolling(site.SiteId) || _store.GetProgress(site.SiteId).Status == "running")
                    {
                        continue;
                    }

                    // 已禁用账号到达额度重置时间 1 分钟后，优先执行额外处理。
                    if (await TryHandleReachedQuotaResetAsync(site.SiteId, site, stoppingToken))
                    {
                        continue;
                    }

                    var nextRun = _store.GetNextScheduledAt(site.SiteId);
                    if (nextRun == DateTime.MinValue)
                    {
                        _store.SetNextScheduledAt(BuildNextRunAt(site), site.SiteId);
                        continue;
                    }

                    if (DateTime.UtcNow < nextRun)
                    {
                        continue;
                    }

                    await RunInspectionAsync(site.SiteId, site, stoppingToken, forceRefresh: false);
                    if (_store.HasSite(site.SiteId))
                    {
                        _store.SetNextScheduledAt(BuildNextRunAt(site), site.SiteId);
                    }
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    /// <summary>
    /// 执行单次自动巡检流程：加载候选、探测、决策、执行动作、记录结果。
    /// </summary>
    private async Task RunInspectionAsync(string siteId, PatrolSiteSettings settings, CancellationToken ct, bool forceRefresh = false)
    {
        _store.SetPollingState(true, siteId);
        _store.SetLastRunStartedAt(DateTime.UtcNow, siteId);
        _store.StartProgress("inspection", "auto", 0, "开始自动巡检", siteId);
        _store.AddOperationLog("inspection", "inspection", "auto", "开始自动巡检", siteId: siteId);

        _logger.LogInformation("开始自动巡检，站点：{SiteName}，供应商：{Provider}", settings.Name, settings.Provider);

        try
        {
            var candidates = await _engine.LoadCandidatesAsync(siteId, includeExceptions: false, ct);
            _store.UpdateProgress("prepare", $"已加载 {candidates.Count} 个待巡检账号", total: candidates.Count, processed: 0, siteId: siteId);

            if (candidates.Count == 0)
            {
                _store.SetLastRun(new InspectionRunResult
                {
                    TotalAccounts = 0,
                    ProbedCount = 0,
                    StartedAt = _store.GetLastRunStartedAt(siteId),
                    FinishedAt = DateTime.UtcNow,
                    Status = "completed",
                }, siteId);
                _store.SetPollingState(false, siteId);
                _store.SetLastRunFinishedAt(DateTime.UtcNow, siteId);
                _store.CompleteProgress("本次没有可巡检账号", siteId);
                _store.AddOperationLog("inspection", "inspection", "auto", "自动巡检结束，本次没有可巡检账号", siteId: siteId);
                return;
            }

            var decisions = await _engine.InspectAccountsAsync(
                siteId,
                candidates,
                settings.ProbeWorkers,
                settings.ProbeBatchDelayMinMs,
                settings.ProbeBatchDelayMaxMs,
                forceRefresh: forceRefresh,
                onProgress: (decision, processed, total) =>
                {
                    _store.UpdateProgress(
                        "probing",
                        $"已探测 {processed}/{total} 个账号",
                        total: total,
                        processed: processed,
                        currentAccountName: decision.AccountName,
                        currentDisplayAccount: decision.DisplayAccount,
                        siteId: siteId);
                    _store.AddOperationLog(
                        "inspection",
                        "inspection",
                        "auto",
                        BuildInspectionProbeLogMessage(siteId, decision),
                        string.IsNullOrWhiteSpace(decision.Error) ? "info" : "warning",
                        decision.AccountName,
                        decision.DisplayAccount,
                        siteId);
                    return Task.CompletedTask;
                },
                onBatchDelay: (batchIndex, totalBatches, delay) =>
                {
                    _store.UpdateProgress("delay", $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续", siteId: siteId);
                    _store.AddOperationLog("inspection", "inspection", "auto", $"第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续", siteId: siteId);
                    return Task.CompletedTask;
                },
                ct: ct);

            var outcomes = new List<ActionOutcome>();
            var mode = ResolveAutoActionMode(settings.AutoActionMode);
            var actionItems = InspectionEngine.FilterAutoActionItems(mode, settings.AutoEnableRecovered, decisions);

            if (actionItems.Count > 0)
            {
                _store.UpdateProgress(
                    "actions",
                    $"开始执行 {actionItems.Count} 个自动动作",
                    total: candidates.Count,
                    processed: decisions.Count,
                    actionTotal: actionItems.Count,
                    actionProcessed: 0,
                    siteId: siteId);
                _store.AddOperationLog("inspection", "inspection", "auto", $"开始执行 {actionItems.Count} 个自动动作", siteId: siteId);

                outcomes = await _engine.ExecuteActionsAsync(
                    siteId,
                    actionItems,
                    settings.ActionWorkers,
                    onProgress: (outcome, processed, total) =>
                    {
                        _store.UpdateProgress(
                            "actions",
                            $"已执行 {processed}/{total} 个自动动作",
                            total: candidates.Count,
                            processed: decisions.Count,
                            actionTotal: total,
                            actionProcessed: processed,
                            currentAccountName: outcome.FileName,
                            currentDisplayAccount: outcome.DisplayAccount,
                            siteId: siteId);
                        _store.AddOperationLog(
                            "inspection",
                            "inspection",
                            "auto",
                            outcome.Success
                                ? $"自动{ResolveOutcomeLabel(outcome.Action)}成功"
                                : $"自动{ResolveOutcomeLabel(outcome.Action)}失败：{outcome.Error}",
                            outcome.Success ? "info" : "error",
                            outcome.FileName,
                            outcome.DisplayAccount,
                            siteId);
                        return Task.CompletedTask;
                    },
                    ct);

                try
                {
                    var files = await _engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
                    _store.SetAccounts(files, siteId);
                }
                catch (Exception ex)
                {
                    _store.AddOperationLog("inspection", "inspection", "auto", $"刷新账号状态失败：{ex.Message}", "warning", siteId: siteId);
                    _store.AddExceptionLog("inspection", "inspection", "auto", ex, "自动巡检后刷新账号状态异常", level: "warning", siteId: siteId);
                }
            }

            var runResult = new InspectionRunResult
            {
                Decisions = decisions,
                ActionOutcomes = outcomes,
                TotalAccounts = candidates.Count,
                ProbedCount = decisions.Count,
                DeleteCount = decisions.Count(decision => decision.Action == InspectionAction.Delete),
                DisableCount = decisions.Count(decision => decision.Action == InspectionAction.Disable),
                EnableCount = decisions.Count(decision => decision.Action == InspectionAction.Enable),
                KeepCount = decisions.Count(decision => decision.Action == InspectionAction.Keep),
                StartedAt = _store.GetLastRunStartedAt(siteId),
                FinishedAt = DateTime.UtcNow,
                Status = "completed",
            };

            _store.SetLastRun(runResult, siteId);
            _store.CompleteProgress($"自动巡检完成，共探测 {runResult.ProbedCount} 个账号", siteId);
            _store.AddOperationLog(
                "inspection",
                "inspection",
                "auto",
                $"自动巡检完成：禁用建议 {runResult.DisableCount}，启用建议 {runResult.EnableCount}，删除建议 {runResult.DeleteCount}",
                siteId: siteId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动巡检异常，站点：{SiteName}", settings.Name);
            _store.AddExceptionLog("inspection", "inspection", "auto", ex, "自动巡检异常", siteId: siteId);
            _store.SetLastRun(new InspectionRunResult
            {
                Status = "error",
                StartedAt = _store.GetLastRunStartedAt(siteId),
                FinishedAt = DateTime.UtcNow,
            }, siteId);
            _store.FailProgress($"自动巡检失败：{ex.Message}", siteId);
            _store.AddOperationLog("inspection", "inspection", "auto", $"自动巡检失败：{ex.Message}", "error", siteId: siteId);
        }
        finally
        {
            _store.SetPollingState(false, siteId);
            _store.SetLastRunFinishedAt(DateTime.UtcNow, siteId);
        }
    }

    /// <summary>
    /// 已禁用账号的额度窗口到达重置时间后，优先触发一次强制巡检。
    /// </summary>
    private async Task<bool> TryHandleReachedQuotaResetAsync(string siteId, PatrolSiteSettings settings, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var nextResetCheckAt = _store.GetAccounts(siteId)
            .Where(account => account.Disabled)
            .Select(account => ResolveResetCheckAt(_store.GetQuota(account.Name, siteId), settings.UsedPercentThreshold))
            .Where(resetCheckAt => resetCheckAt.HasValue)
            .Select(resetCheckAt => resetCheckAt!.Value)
            .OrderBy(resetCheckAt => resetCheckAt)
            .FirstOrDefault();

        if (nextResetCheckAt == DateTime.MinValue)
        {
            return false;
        }

        var scheduledAt = _store.GetNextScheduledAt(siteId);
        if (scheduledAt == DateTime.MinValue || nextResetCheckAt < scheduledAt)
        {
            _store.SetNextScheduledAt(nextResetCheckAt, siteId);
        }

        if (nowUtc < nextResetCheckAt)
        {
            return false;
        }

        _store.AddOperationLog("inspection", "inspection", "auto", "检测到已禁用账号达到额度重置时间，开始强制自动巡检", siteId: siteId);
        await RunInspectionAsync(siteId, settings, ct, forceRefresh: true);
        if (_store.HasSite(siteId))
        {
            _store.SetNextScheduledAt(BuildNextRunAt(settings), siteId);
        }

        return true;
    }

    /// <summary>
    /// 计算已达到阈值的额度窗口对应的重检时间。
    /// </summary>
    private static DateTime? ResolveResetCheckAt(CodexQuotaSnapshot? quota, int threshold)
    {
        if (quota is null || !quota.Success)
        {
            return null;
        }

        var resetCheckTimes = quota.Windows
            .Where(window => window.ResetAtUtc != DateTime.MinValue)
            .Where(IsTrackedResetWindow)
            .Where(window => window.UsedPercent.HasValue && window.UsedPercent.Value >= threshold)
            .Select(window => window.ResetAtUtc + ResetCheckDelay)
            .OrderBy(resetCheckAt => resetCheckAt)
            .ToList();

        return resetCheckTimes.Count == 0 ? null : resetCheckTimes[0];
    }

    /// <summary>
    /// 仅跟踪 5 小时和周额度窗口的重置时间。
    /// </summary>
    private static bool IsTrackedResetWindow(CodexQuotaWindowSnapshot window)
    {
        return window.LimitWindowSeconds == FiveHourSeconds || window.LimitWindowSeconds == WeekSeconds;
    }

    /// <summary>
    /// 计算下次巡检时间，基于当前时间加上配置间隔和随机延迟。
    /// </summary>
    private static DateTime BuildNextRunAt(PatrolSiteSettings settings)
    {
        return BuildNextRunAt(settings, DateTime.UtcNow);
    }

    /// <summary>
    /// 计算下次巡检时间，基于指定时间加上配置间隔和随机延迟。
    /// </summary>
    private static DateTime BuildNextRunAt(PatrolSiteSettings settings, DateTime nowUtc)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, settings.PollIntervalMinutes));
        var randomDelayMinMinutes = Math.Max(0, settings.PollRandomDelayMinMinutes);
        var randomDelayMaxMinutes = Math.Max(randomDelayMinMinutes, settings.PollRandomDelayMaxMinutes);
        var randomDelayMinutes = Random.Shared.Next(randomDelayMinMinutes, randomDelayMaxMinutes + 1);
        return nowUtc + interval + TimeSpan.FromMinutes(randomDelayMinutes);
    }

    /// <summary>
    /// 将字符串解析为自动动作模式枚举，无效值返回 None。
    /// </summary>
    private static AutoActionMode ResolveAutoActionMode(string value)
    {
        return Enum.TryParse<AutoActionMode>(value, true, out var mode)
            ? mode
            : AutoActionMode.None;
    }

    /// <summary>
    /// 根据探测结果构建日志消息，区分成功和失败情况。
    /// </summary>
    private string BuildInspectionProbeLogMessage(string siteId, InspectionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Error))
        {
            return $"探测失败，建议{ResolveDecisionLabel(decision.Action)}：{decision.Error}";
        }

        var quota = _store.GetQuota(decision.AccountName, siteId);
        var source = quota?.FromCache == true ? "命中缓存" : "实时请求";
        return $"探测完成（{source}），建议{ResolveDecisionLabel(decision.Action)}：{decision.Reason}";
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
    /// 将动作字符串转换为中文标签，用于执行结果日志。
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
