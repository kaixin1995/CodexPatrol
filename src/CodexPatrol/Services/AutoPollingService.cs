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
    /// CPA 客户端，用于优先级路由时执行启用/禁用操作。
    /// </summary>
    private readonly CpaClient _cpa;

    /// <summary>
    /// 记录达到额度重置检测时间的禁用账号。
    /// </summary>
    private sealed record ReachedQuotaResetCandidate(AuthFileItem Account, DateTime DueAtUtc);

    /// <summary>
    /// 构造 AutoPollingService。
    /// </summary>
    public AutoPollingService(
        InspectionEngine engine,
        CpaClient cpa,
        RuntimeStore store,
        ILogger<AutoPollingService> logger)
    {
        _engine = engine;
        _cpa = cpa;
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
                await WarmupStartupQuotasAsync(site.SiteId, site, accounts, stoppingToken);
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
                        _store.SetNextResetCheckAt(DateTime.MinValue, site.SiteId);
                        continue;
                    }

                    if (_store.IsPolling(site.SiteId) || _store.GetProgress(site.SiteId).Status == "running")
                    {
                        continue;
                    }

                    if (await TryRefreshScheduledRealQuotasAsync(site.SiteId, site, stoppingToken))
                    {
                        continue;
                    }

                    if (!site.AutoPollingEnabled)
                    {
                        _store.SetNextScheduledAt(DateTime.MinValue, site.SiteId);
                        _store.SetNextResetCheckAt(DateTime.MinValue, site.SiteId);
                        continue;
                    }

                    var nextRun = _store.GetNextScheduledAt(site.SiteId);
                    if (nextRun == DateTime.MinValue)
                    {
                        await RunInspectionAsync(site.SiteId, site, stoppingToken, forceRefresh: false);
                        if (_store.HasSite(site.SiteId))
                        {
                            _store.SetNextScheduledAt(BuildNextRunAt(site), site.SiteId);
                        }
                        continue;
                    }

                    // 已禁用账号到达额度重置时间 1 分钟后，优先执行额外处理。
                    if (await TryHandleReachedQuotaResetAsync(site.SiteId, site, stoppingToken))
                    {
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
    /// 应用启动后，为每个站点顺序预热少量启用账号的额度缓存。
    /// </summary>
    private async Task WarmupStartupQuotasAsync(string siteId, PatrolSiteSettings settings, List<AuthFileItem> accounts, CancellationToken ct)
    {
        List<AuthFileItem> warmupAccounts;

        // 优先级路由开启时，按优先级顺序选取预热账号（不论是否禁用），否则取启用账号
        if (settings.PriorityRoutingEnabled)
        {
            var priorities = _store.GetAccountPriorities(siteId);
            warmupAccounts = priorities.Count > 0
                ? priorities
                    .OrderBy(p => p.Priority)
                    .Select(p => accounts.FirstOrDefault(a =>
                        string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                    .Where(a => a != null)
                    .Take(3)
                    .ToList()!
                : accounts.Where(a => !a.Disabled).Take(3).ToList();
        }
        else
        {
            warmupAccounts = accounts
                .Where(account => !account.Disabled)
                .Take(3)
                .ToList();
        }

        if (warmupAccounts.Count == 0)
        {
            _store.AddOperationLog("quota", "startupWarmup", "system", "启动预热未找到可做真实检测的账号", siteId: siteId);
            return;
        }

        _store.AddOperationLog("quota", "startupWarmup", "system", $"启动预热开始，将对最多 {warmupAccounts.Count} 个账号做真实额度检测", siteId: siteId);

        for (var index = 0; index < warmupAccounts.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var account = warmupAccounts[index];
            var decision = await _engine.InspectAccountAsync(siteId, account, forceRefresh: true, ct: ct);
            var quota = _store.GetQuota(account.Name, siteId);
            var weeklyUsedPercent = quota is null ? null : CodexQuotaParser.GetWeeklyUsedPercent(quota);
            var weeklyText = weeklyUsedPercent.HasValue ? $"{weeklyUsedPercent.Value:0.##}%" : "未知";

            _store.AddOperationLog(
                "quota",
                "startupWarmup",
                "system",
                !string.IsNullOrWhiteSpace(decision.Error)
                    ? $"启动预热真实检测第 {index + 1} 个账号失败：{account.Name}，{decision.Error}"
                    : $"启动预热真实检测第 {index + 1} 个账号完成：{account.Name}，周额度 {weeklyText}，阈值 {settings.UsedPercentThreshold}%",
                string.IsNullOrWhiteSpace(decision.Error) ? "info" : "warning",
                account.Name,
                decision.DisplayAccount,
                siteId);

            if (weeklyUsedPercent.HasValue && weeklyUsedPercent.Value < settings.UsedPercentThreshold)
            {
                _store.AddOperationLog("quota", "startupWarmup", "system", $"启动预热真实检测停止：账号 {account.Name} 周额度 {weeklyText} 未达到阈值 {settings.UsedPercentThreshold}%", siteId: siteId);
                return;
            }
        }

        _store.AddOperationLog("quota", "startupWarmup", "system", $"启动预热真实检测结束：已按上限探测 {warmupAccounts.Count} 个账号", siteId: siteId);
    }

    /// <summary>
    /// 对达到分散真实刷新窗口的非例外账号做小批量真实刷新，避免大量账号同时过期。
    /// </summary>
    private async Task<bool> TryRefreshScheduledRealQuotasAsync(string siteId, PatrolSiteSettings settings, CancellationToken ct)
    {
        var accounts = _store.GetAccounts(siteId);
        if (accounts.Count == 0)
        {
            try
            {
                accounts = await _engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
            }
            catch (Exception ex)
            {
                _store.AddOperationLog("quota", "freshnessRefresh", "auto", $"加载账号列表失败：{ex.Message}", "warning", siteId: siteId);
                _store.AddExceptionLog("quota", "freshnessRefresh", "auto", ex, "加载保鲜真实刷新候选账号异常", level: "warning", siteId: siteId);
                return false;
            }
        }

        var nowUtc = DateTime.UtcNow;
        var exceptions = _store.GetExceptions(siteId);
        var dueAccounts = accounts
            .Where(account => !exceptions.Contains(account.Name))
            .Select(account => new
            {
                Account = account,
                DueAtUtc = QuotaCachePolicy.GetScheduledRealRefreshAt(_store.GetQuota(account.Name, siteId), siteId, account.Name),
            })
            .Where(item => !item.DueAtUtc.HasValue || item.DueAtUtc.Value <= nowUtc)
            .OrderBy(item => item.DueAtUtc ?? DateTime.MinValue)
            .ThenBy(item => item.Account.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, settings.ProbeWorkers))
            .Select(item => item.Account)
            .ToList();

        if (dueAccounts.Count == 0)
        {
            return false;
        }

        _store.SetPollingState(true, siteId);
        try
        {
            _store.AddOperationLog("quota", "freshnessRefresh", "auto", $"检测到 {dueAccounts.Count} 个账号达到真实刷新时间，开始分批真实刷新", siteId: siteId);
            var decisions = await _engine.InspectAccountsAsync(
                siteId,
                dueAccounts,
                settings.ProbeWorkers,
                0,
                0,
                forceRefresh: true,
                onProgress: (decision, _, _) =>
                {
                    _store.AddOperationLog(
                        "quota",
                        "freshnessRefresh",
                        "auto",
                        string.IsNullOrWhiteSpace(decision.Error)
                            ? $"额度保鲜真实刷新完成：{decision.AccountName}"
                            : $"额度保鲜真实刷新失败：{decision.AccountName}，{decision.Error}",
                        string.IsNullOrWhiteSpace(decision.Error) ? "info" : "warning",
                        decision.AccountName,
                        decision.DisplayAccount,
                        siteId);
                    return Task.CompletedTask;
                },
                ct: ct);
            _store.AddOperationLog("quota", "freshnessRefresh", "auto", $"本轮保鲜真实刷新完成，共处理 {decisions.Count} 个账号", siteId: siteId);
            return true;
        }
        catch (Exception ex)
        {
            _store.AddOperationLog("quota", "freshnessRefresh", "auto", $"保鲜真实刷新失败：{ex.Message}", "warning", siteId: siteId);
            _store.AddExceptionLog("quota", "freshnessRefresh", "auto", ex, "保鲜真实刷新异常", level: "warning", siteId: siteId);
            return true;
        }
        finally
        {
            if (_store.HasSite(siteId))
            {
                _store.SetPollingState(false, siteId);
            }
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
            var actionItems = InspectionEngine.FilterAutoActionItems(mode, settings.AutoEnableRecovered, settings.PriorityRoutingEnabled, decisions);
            _store.AddOperationLog(
                "inspection",
                "inspection",
                "auto",
                $"自动巡检决策汇总：保留 {decisions.Count(decision => decision.Action == InspectionAction.Keep)}，禁用建议 {decisions.Count(decision => decision.Action == InspectionAction.Disable)}，启用建议 {decisions.Count(decision => decision.Action == InspectionAction.Enable)}，删除建议 {decisions.Count(decision => decision.Action == InspectionAction.Delete)}；自动模式 {mode}；自动启用 {(settings.AutoEnableRecovered ? "开启" : "关闭")}；优先级路由 {(settings.PriorityRoutingEnabled ? "开启" : "关闭")}；待执行动作 {actionItems.Count} 个",
                siteId: siteId);

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
            else
            {
                _store.AddOperationLog("inspection", "inspection", "auto", "本轮自动巡检没有可执行的自动动作", siteId: siteId);
            }

            // 无论是否有自动动作，优先级路由开启时都要执行调度
            var priorityOutcomes = new List<ActionOutcome>();
            if (settings.PriorityRoutingEnabled)
            {
                priorityOutcomes = await ApplyPriorityRoutingAsync(siteId, settings, decisions, ct);
            }

            var runResult = new InspectionRunResult
            {
                Decisions = decisions,
                ActionOutcomes = outcomes,
                PriorityRoutingOutcomes = priorityOutcomes,
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
    /// 已禁用账号的额度窗口到达重置时间后，先做真实检测；如需自动动作，也只处理这些到点账号本身。
    /// </summary>
    private async Task<bool> TryHandleReachedQuotaResetAsync(string siteId, PatrolSiteSettings settings, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var dueCandidates = ResolveReachedQuotaResetCandidates(siteId, settings.UsedPercentThreshold, nowUtc);
        var nextResetCheckAt = ResolveNextResetCheckAt(siteId, settings.UsedPercentThreshold, nowUtc);
        _store.SetNextResetCheckAt(nextResetCheckAt ?? DateTime.MinValue, siteId);

        if (dueCandidates.Count == 0)
        {
            return false;
        }

        _store.SetPollingState(true, siteId);
        try
        {
            _store.AddOperationLog(
                "inspection",
                "inspection",
                "auto",
                $"检测到 {dueCandidates.Count} 个已禁用账号达到额度重置时间，开始真实检测",
                siteId: siteId);

            var decisions = await CheckReachedQuotaAccountsAsync(siteId, settings, dueCandidates, ct);
            var refreshedNowUtc = DateTime.UtcNow;

            foreach (var candidate in dueCandidates)
            {
                MarkReachedResetWindowsHandled(_store.GetQuota(candidate.Account.Name, siteId), settings.UsedPercentThreshold, refreshedNowUtc);
            }

            if (!settings.AutoEnableRecovered)
            {
                if (decisions.Any(decision => decision.Action == InspectionAction.Enable))
                {
                    _store.AddOperationLog("inspection", "inspection", "auto", "检测到额度已恢复，但未开启自动启用已恢复的账号，跳过额外单账号自动动作", siteId: siteId);
                }

                // 优先级路由开启时，标记恢复账号为 OrderedStandby
                if (settings.PriorityRoutingEnabled)
                {
                    foreach (var decision in decisions.Where(d => d.Action == InspectionAction.Enable || d.DisableReason == DisableReason.OrderedStandby))
                    {
                        _store.SetDisableReason(decision.AccountName, DisableReason.OrderedStandby, siteId);
                    }
                }

                EnsureNextInspectionScheduled(siteId, settings, refreshedNowUtc);
                _store.SetNextResetCheckAt(ResolveNextResetCheckAt(siteId, settings.UsedPercentThreshold, refreshedNowUtc) ?? DateTime.MinValue, siteId);
                return true;
            }

            // 优先级路由开启时，不在此处直接启用恢复的账号，由优先级调度统一处理
            if (settings.PriorityRoutingEnabled)
            {
                _store.AddOperationLog("inspection", "inspection", "auto",
                    "额度重置检测完成，优先级路由开启，恢复账号将由优先级调度统一处理",
                    siteId: siteId);
                foreach (var decision in decisions.Where(d => d.Action == InspectionAction.Enable || d.DisableReason == DisableReason.OrderedStandby))
                {
                    _store.SetDisableReason(decision.AccountName, DisableReason.OrderedStandby, siteId);
                }
                EnsureNextInspectionScheduled(siteId, settings, refreshedNowUtc);
                _store.SetNextResetCheckAt(ResolveNextResetCheckAt(siteId, settings.UsedPercentThreshold, refreshedNowUtc) ?? DateTime.MinValue, siteId);
                return true;
            }

            var mode = ResolveAutoActionMode(settings.AutoActionMode);
            var actionItems = InspectionEngine.FilterAutoActionItems(mode, settings.AutoEnableRecovered, settings.PriorityRoutingEnabled, decisions);
            if (actionItems.Count == 0)
            {
                EnsureNextInspectionScheduled(siteId, settings, refreshedNowUtc);
                _store.SetNextResetCheckAt(ResolveNextResetCheckAt(siteId, settings.UsedPercentThreshold, refreshedNowUtc) ?? DateTime.MinValue, siteId);
                return true;
            }

            _store.AddOperationLog("inspection", "inspection", "auto", $"检测到可处理账号，开始执行 {actionItems.Count} 个单账号自动动作", siteId: siteId);
            await _engine.ExecuteActionsAsync(
                siteId,
                actionItems,
                settings.ActionWorkers,
                onProgress: (outcome, _, _) =>
                {
                    _store.AddOperationLog(
                        "inspection",
                        "inspection",
                        "auto",
                        outcome.Success
                            ? $"额度重置后自动{ResolveOutcomeLabel(outcome.Action)}成功"
                            : $"额度重置后自动{ResolveOutcomeLabel(outcome.Action)}失败：{outcome.Error}",
                        outcome.Success ? "info" : "error",
                        outcome.FileName,
                        outcome.DisplayAccount,
                        siteId);
                    return Task.CompletedTask;
                },
                ct: ct);

            try
            {
                var files = await _engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
                _store.SetAccounts(files, siteId);
            }
            catch (Exception ex)
            {
                _store.AddOperationLog("inspection", "inspection", "auto", $"刷新账号状态失败：{ex.Message}", "warning", siteId: siteId);
                _store.AddExceptionLog("inspection", "inspection", "auto", ex, "额度重置后刷新账号状态异常", level: "warning", siteId: siteId);
            }

            var completedAtUtc = DateTime.UtcNow;
            EnsureNextInspectionScheduled(siteId, settings, completedAtUtc);
            _store.SetNextResetCheckAt(ResolveNextResetCheckAt(siteId, settings.UsedPercentThreshold, completedAtUtc) ?? DateTime.MinValue, siteId);
            return true;
        }
        finally
        {
            if (_store.HasSite(siteId))
            {
                _store.SetPollingState(false, siteId);
            }
        }
    }

    /// <summary>
    /// 对达到重置检测时间的禁用账号批次做真实检测，并按并发限制返回这些账号各自的巡检决策。
    /// </summary>
    private async Task<List<InspectionDecision>> CheckReachedQuotaAccountsAsync(
        string siteId,
        PatrolSiteSettings settings,
        List<ReachedQuotaResetCandidate> candidates,
        CancellationToken ct)
    {
        var targetAccounts = candidates
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Account.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Account)
            .ToList();

        return await _engine.InspectAccountsAsync(
            siteId,
            targetAccounts,
            settings.ProbeWorkers,
            settings.ProbeBatchDelayMinMs,
            settings.ProbeBatchDelayMaxMs,
            forceRefresh: true,
            onProgress: (decision, _, _) =>
            {
                _store.AddOperationLog(
                    "inspection",
                    "inspection",
                    "auto",
                    BuildResetQuotaProbeLogMessage(decision),
                    string.IsNullOrWhiteSpace(decision.Error) ? "info" : "warning",
                    decision.AccountName,
                    decision.DisplayAccount,
                    siteId);
                return Task.CompletedTask;
            },
            onBatchDelay: (batchIndex, totalBatches, delay) =>
            {
                _store.AddOperationLog(
                    "inspection",
                    "inspection",
                    "auto",
                    $"额度重置检测第 {batchIndex}/{totalBatches} 批已完成，等待 {delay.TotalSeconds:F1} 秒后继续",
                    siteId: siteId);
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>
    /// 收集已达到重置检测时间且尚未处理过的禁用账号。
    /// </summary>
    private List<ReachedQuotaResetCandidate> ResolveReachedQuotaResetCandidates(string siteId, int threshold, DateTime nowUtc)
    {
        return _store.GetAccounts(siteId)
            .Where(account => account.Disabled)
            .Select(account => new ReachedQuotaResetCandidate(account, ResolveReachedResetCheckAt(_store.GetQuota(account.Name, siteId), threshold, nowUtc)))
            .Where(candidate => candidate.DueAtUtc != DateTime.MinValue)
            .OrderBy(candidate => candidate.DueAtUtc)
            .ThenBy(candidate => candidate.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 计算下一次尚未到达的额度重置检测时间，用于提前唤醒轮询。
    /// </summary>
    private DateTime? ResolveNextResetCheckAt(string siteId, int threshold, DateTime nowUtc)
    {
        var nextResetCheckAt = _store.GetAccounts(siteId)
            .Where(account => account.Disabled)
            .Select(account => ResolveUpcomingResetCheckAt(_store.GetQuota(account.Name, siteId), threshold, nowUtc))
            .Where(resetCheckAt => resetCheckAt.HasValue)
            .Select(resetCheckAt => resetCheckAt!.Value)
            .OrderBy(resetCheckAt => resetCheckAt)
            .FirstOrDefault();

        return nextResetCheckAt == DateTime.MinValue ? null : nextResetCheckAt;
    }

    /// <summary>
    /// 计算已经达到重置检测时间且尚未处理的最早时间点。
    /// </summary>
    private static DateTime ResolveReachedResetCheckAt(CodexQuotaSnapshot? quota, int threshold, DateTime nowUtc)
    {
        return EnumerateTrackedResetWindows(quota, threshold)
            .Select(window => ResolveResetCheckAt(window))
            .Where(resetCheckAt => resetCheckAt <= nowUtc)
            .OrderBy(resetCheckAt => resetCheckAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// 计算下一次尚未到达的重置检测时间。
    /// </summary>
    private static DateTime? ResolveUpcomingResetCheckAt(CodexQuotaSnapshot? quota, int threshold, DateTime nowUtc)
    {
        var nextResetCheckAt = EnumerateTrackedResetWindows(quota, threshold)
            .Select(window => ResolveResetCheckAt(window))
            .Where(resetCheckAt => resetCheckAt > nowUtc)
            .OrderBy(resetCheckAt => resetCheckAt)
            .FirstOrDefault();

        return nextResetCheckAt == DateTime.MinValue ? null : nextResetCheckAt;
    }

    /// <summary>
    /// 枚举需要跟踪重置检测时间的额度窗口：免费号只看周限额，收费号同时看周限额和 5 小时限额。
    /// </summary>
    private static IEnumerable<CodexQuotaWindowSnapshot> EnumerateTrackedResetWindows(CodexQuotaSnapshot? quota, int threshold)
    {
        if (quota is null || !quota.Success)
        {
            yield break;
        }

        var isFreePlan = string.Equals(quota.PlanType, "Free", StringComparison.OrdinalIgnoreCase);
        foreach (var window in quota.Windows)
        {
            if (window.ResetAtUtc == DateTime.MinValue)
            {
                continue;
            }

            if (!window.UsedPercent.HasValue || window.UsedPercent.Value < threshold)
            {
                continue;
            }

            if (window.LimitWindowSeconds == WeekSeconds)
            {
                if (window.LastResetHandledAt < ResolveResetCheckAt(window))
                {
                    yield return window;
                }
                continue;
            }

            if (!isFreePlan && window.LimitWindowSeconds == FiveHourSeconds && window.LastResetHandledAt < ResolveResetCheckAt(window))
            {
                yield return window;
            }
        }
    }

    /// <summary>
    /// 将已达到时间点的额度窗口标记为已处理，避免同一重置点被反复触发。
    /// </summary>
    private static void MarkReachedResetWindowsHandled(CodexQuotaSnapshot? quota, int threshold, DateTime nowUtc)
    {
        foreach (var window in EnumerateTrackedResetWindows(quota, threshold))
        {
            var resetCheckAt = ResolveResetCheckAt(window);
            if (resetCheckAt <= nowUtc)
            {
                window.LastResetHandledAt = nowUtc;
            }
        }
    }

    /// <summary>
    /// 计算单个额度窗口对应的重置检测时间。
    /// </summary>
    private static DateTime ResolveResetCheckAt(CodexQuotaWindowSnapshot window)
    {
        return window.ResetAtUtc + ResetCheckDelay;
    }

    /// <summary>
    /// 如果常规轮询时间已过或尚未初始化，则重新安排下一次常规巡检。
    /// </summary>
    private void EnsureNextInspectionScheduled(string siteId, PatrolSiteSettings settings, DateTime nowUtc)
    {
        if (!_store.HasSite(siteId))
        {
            return;
        }

        var scheduledAt = _store.GetNextScheduledAt(siteId);
        if (scheduledAt == DateTime.MinValue || scheduledAt <= nowUtc)
        {
            _store.SetNextScheduledAt(BuildNextRunAt(settings, nowUtc), siteId);
        }
    }

    /// <summary>
    /// 如果额度重置检测时间更早，则提前调度下一次轮询。
    /// </summary>
    private void ScheduleEarlierResetCheck(string siteId, DateTime? nextResetCheckAt)
    {
        if (!_store.HasSite(siteId) || !nextResetCheckAt.HasValue)
        {
            return;
        }

        var scheduledAt = _store.GetNextResetCheckAt(siteId);
        if (scheduledAt == DateTime.MinValue || nextResetCheckAt.Value < scheduledAt)
        {
            _store.SetNextResetCheckAt(nextResetCheckAt.Value, siteId);
        }
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
    /// 优先级路由调度：按优先级从高到低，至少保持 minActiveCount 个可用账号启用，其余标记 OrderedStandby。
    /// </summary>
    private async Task<List<ActionOutcome>> ApplyPriorityRoutingAsync(
        string siteId, PatrolSiteSettings settings,
        List<InspectionDecision> decisions, CancellationToken ct)
    {
        if (!settings.PriorityRoutingEnabled)
        {
            return [];
        }

        var priorities = _store.GetAccountPriorities(siteId);
        if (priorities.Count == 0)
        {
            return [];
        }

        var minActiveCount = Math.Max(1, settings.PriorityMinActiveCount);
        var exceptions = _store.GetExceptions(siteId);

        // 按优先级升序找到额度可用的账号，收集至达到 minActiveCount
        var activeAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var priority in priorities.OrderBy(p => p.Priority))
        {
            if (exceptions.Contains(priority.Name))
            {
                continue;
            }

            var account = _store.GetAccount(priority.Name, siteId);
            if (account == null)
            {
                continue;
            }

            var quota = _store.GetQuota(priority.Name, siteId);
            var weeklyPercent = quota != null ? CodexQuotaParser.GetWeeklyUsedPercent(quota) : null;

            if (!weeklyPercent.HasValue || weeklyPercent.Value < settings.UsedPercentThreshold)
            {
                activeAccountNames.Add(priority.Name);
                if (activeAccountNames.Count >= minActiveCount)
                {
                    break;
                }
            }
        }

        if (activeAccountNames.Count == 0)
        {
            _store.AddOperationLog("account", "priorityRouting", "auto",
                "优先级路由：所有配置优先级的账号额度已耗尽", "warning",
                siteId: siteId);
        }
        else if (activeAccountNames.Count < minActiveCount)
        {
            _store.AddOperationLog("account", "priorityRouting", "auto",
                $"优先级路由：仅剩 {activeAccountNames.Count} 个可用账号，不足最少保持数 {minActiveCount}", "warning",
                siteId: siteId);
        }

        var outcomes = new List<ActionOutcome>();
        var accounts = _store.GetAccounts(siteId);
        foreach (var account in accounts)
        {
            var priorityEntry = priorities.FirstOrDefault(
                p => string.Equals(p.Name, account.Name, StringComparison.OrdinalIgnoreCase));
            if (priorityEntry == null)
            {
                continue;
            }

            if (activeAccountNames.Contains(account.Name))
            {
                if (account.Disabled &&
                    _store.GetDisableReason(account.Name, siteId) == DisableReason.OrderedStandby)
                {
                    try
                    {
                        await _cpa.EnableAccountAsync(settings, account.Name, ct);
                        _store.UpdateAccountDisabledState(account.Name, false, siteId);
                        _store.ClearDisableReason(account.Name, siteId);
                        outcomes.Add(new ActionOutcome
                        {
                            Action = "enable", FileName = account.Name,
                            DisplayAccount = account.Account ?? account.Email ?? account.Name, Success = true,
                        });
                        _store.AddOperationLog("account", "priorityRouting", "auto",
                            $"优先级路由恢复启用：{account.Name}（优先级 {priorityEntry.Priority}）",
                            siteId: siteId);
                    }
                    catch (Exception ex)
                    {
                        outcomes.Add(new ActionOutcome
                        {
                            Action = "enable", FileName = account.Name,
                            DisplayAccount = account.Account ?? account.Email ?? account.Name, Success = false,
                            Error = ex.Message,
                        });
                        _store.AddOperationLog("account", "priorityRouting", "auto",
                            $"优先级路由恢复启用失败：{account.Name}，{ex.Message}", "error",
                            siteId: siteId);
                    }
                }
            }
            else
            {
                if (!account.Disabled &&
                    _store.GetDisableReason(account.Name, siteId) == DisableReason.None)
                {
                    try
                    {
                        await _cpa.DisableAccountAsync(settings, account.Name, ct);
                        _store.UpdateAccountDisabledState(account.Name, true, siteId);
                        _store.SetDisableReason(account.Name, DisableReason.OrderedStandby, siteId);
                        outcomes.Add(new ActionOutcome
                        {
                            Action = "disable", FileName = account.Name,
                            DisplayAccount = account.Account ?? account.Email ?? account.Name, Success = true,
                        });
                        _store.AddOperationLog("account", "priorityRouting", "auto",
                            $"优先级路由待命禁用：{account.Name}（优先级 {priorityEntry.Priority}）",
                            siteId: siteId);
                    }
                    catch (Exception ex)
                    {
                        outcomes.Add(new ActionOutcome
                        {
                            Action = "disable", FileName = account.Name,
                            DisplayAccount = account.Account ?? account.Email ?? account.Name, Success = false,
                            Error = ex.Message,
                        });
                        _store.AddOperationLog("account", "priorityRouting", "auto",
                            $"优先级路由待命禁用失败：{account.Name}，{ex.Message}", "error",
                            siteId: siteId);
                    }
                }
            }
        }

        return outcomes;
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
    /// 构建额度重置后的真实检测日志消息。
    /// </summary>
    private string BuildResetQuotaProbeLogMessage(InspectionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Error))
        {
            return $"额度重置检测失败，建议{ResolveDecisionLabel(decision.Action)}：{decision.Error}";
        }

        return $"额度重置检测完成（真实请求），建议{ResolveDecisionLabel(decision.Action)}：{decision.Reason}";
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
