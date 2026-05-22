using System.Collections.Concurrent;
using CodexPatrol.Models;

namespace CodexPatrol.Services;

/// <summary>
/// 巡检引擎：执行账号探测、决策、动作执行。
/// </summary>
public sealed class InspectionEngine
{
    /// <summary>
    /// CPA 客户端，用于请求上游用量数据。
    /// </summary>
    private readonly CpaClient _cpa;

    /// <summary>
    /// 运行时状态仓库。
    /// </summary>
    private readonly RuntimeStore _store;

    /// <summary>
    /// 全局探测并发计数。
    /// </summary>
    private int _globalProbeRunningCount;

    /// <summary>
    /// 全局探测等待队列，自动与手动线路统一在这里排队。
    /// </summary>
    private readonly LinkedList<TaskCompletionSource<bool>> _globalProbeWaiters = [];

    /// <summary>
    /// 全局探测并发锁。
    /// </summary>
    private readonly object _globalProbeLock = new();

    /// <summary>
    /// 构造 InspectionEngine。
    /// </summary>
    public InspectionEngine(CpaClient cpa, RuntimeStore store)
    {
        _cpa = cpa;
        _store = store;
    }

    /// <summary>
    /// 获取当前系统设置中的执行并发数，作为自动与手动线路共享的全局探测上限。
    /// </summary>
    private int ResolveGlobalProbeLimit()
    {
        return Math.Max(1, _store.GetSettings().ActionWorkers);
    }

    /// <summary>
    /// 申请一个全局探测槽位，超过执行并发数时进入等待队列。
    /// </summary>
    private async Task<IDisposable> AcquireGlobalProbeSlotAsync(CancellationToken ct)
    {
        LinkedListNode<TaskCompletionSource<bool>>? waiterNode = null;

        lock (_globalProbeLock)
        {
            if (_globalProbeRunningCount < ResolveGlobalProbeLimit())
            {
                _globalProbeRunningCount++;
                return new GlobalProbeLease(this);
            }

            waiterNode = _globalProbeWaiters.AddLast(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        using var registration = ct.Register(static state =>
        {
            ((TaskCompletionSource<bool>)state!).TrySetCanceled();
        }, waiterNode.Value);

        try
        {
            await waiterNode.Value.Task;
            return new GlobalProbeLease(this);
        }
        catch
        {
            lock (_globalProbeLock)
            {
                if (waiterNode.List is not null)
                {
                    _globalProbeWaiters.Remove(waiterNode);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 释放一个全局探测槽位，并按队列顺序唤醒等待中的任务。
    /// </summary>
    private void ReleaseGlobalProbeSlot()
    {
        lock (_globalProbeLock)
        {
            if (_globalProbeRunningCount > 0)
            {
                _globalProbeRunningCount--;
            }

            while (_globalProbeRunningCount < ResolveGlobalProbeLimit() && _globalProbeWaiters.Count > 0)
            {
                var waiter = _globalProbeWaiters.First!.Value;
                _globalProbeWaiters.RemoveFirst();
                if (waiter.TrySetResult(true))
                {
                    _globalProbeRunningCount++;
                }
            }
        }
    }

    /// <summary>
    /// 全局探测槽位释放器，确保每次成功申请后都能正确归还配额。
    /// </summary>
    private sealed class GlobalProbeLease : IDisposable
    {
        private InspectionEngine? _owner;

        public GlobalProbeLease(InspectionEngine owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseGlobalProbeSlot();
        }
    }

    /// <summary>
    /// 加载候选账号：获取全部 auth-files，筛选目标 provider，排除例外名单。
    /// </summary>
    public async Task<List<AuthFileItem>> LoadCandidatesAsync(
        string? siteId,
        bool includeExceptions,
        CancellationToken ct = default)
    {
        var resolvedSiteId = _store.ResolveSiteId(siteId);
        var settings = _store.GetSettings(resolvedSiteId);
        var response = await _cpa.GetAuthFilesAsync(settings, ct);
        var matchedFiles = response.Files
            .Where(file => MatchesProvider(file, settings.Provider))
            .ToList();
        _store.SetAccounts(matchedFiles, resolvedSiteId);

        var exceptions = _store.GetExceptions(resolvedSiteId);
        return matchedFiles
            .Where(file => includeExceptions || !exceptions.Contains(file.Name))
            .ToList();
    }

    /// <summary>
    /// 加载所有账号的最近调用时间映射，用于额度缓存策略判断。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateTime>?> LoadLastUsageByAuthIndexAsync(string? siteId, CancellationToken ct = default)
    {
        var resolvedSiteId = _store.ResolveSiteId(siteId);
        var settings = _store.GetSettings(resolvedSiteId);

        try
        {
            var usageJson = await _cpa.GetUsageRawAsync(settings, ct);
            return UsageActivityAnalyzer.BuildLastUsageByAuthIndex(usageJson);
        }
        catch (Exception ex)
        {
            _store.AddExceptionLog("quota", "usageActivity", "engine", ex, "读取使用记录异常", level: "warning", siteId: resolvedSiteId);
            return null;
        }
    }

    /// <summary>
    /// 探测单个账号并返回决策。
    /// </summary>
    public async Task<InspectionDecision> InspectAccountAsync(
        string? siteId,
        AuthFileItem file,
        IReadOnlyDictionary<string, DateTime>? lastUsageByAuthIndex = null,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var resolvedSiteId = _store.ResolveSiteId(siteId);
        var settings = _store.GetSettings(resolvedSiteId);
        var authIndex = file.Auth_Index ?? file.AuthIndex ?? "";
        var accountId = ResolveAccountId(file);
        var displayAccount = ResolveDisplayAccount(file);

        // 缺少 auth_index 无法请求上游，直接保留。
        if (string.IsNullOrWhiteSpace(authIndex))
        {
            return new InspectionDecision
            {
                AccountName = file.Name,
                DisplayAccount = displayAccount,
                Action = InspectionAction.Keep,
                Reason = "缺少 auth_index，保留账号",
                CheckedAt = DateTime.UtcNow,
                Disabled = file.Disabled,
                Error = "缺少 auth_index",
            };
        }

        try
        {
            var nowUtc = DateTime.UtcNow;
            var existingQuota = _store.GetQuota(file.Name, resolvedSiteId);

            // 判断 usage-queue 监控是否活跃，以及该账号是否有调用活动。
            var monitorActive = _store.IsUsageMonitorActive(resolvedSiteId);
            var hasUsage = monitorActive && _store.HasAccountUsage(resolvedSiteId, authIndex.Trim());

            // 已禁用的免费账号，如果周额度未重置则跳过本轮检查。
            if (!forceRefresh && QuotaCachePolicy.TrySkipDisabledFreeQuota(
                existingQuota,
                displayAccount,
                file.Disabled,
                settings.UsedPercentThreshold,
                nowUtc,
                out var skippedQuota,
                out var _))
            {
                _store.SetQuota(file.Name, skippedQuota!, resolvedSiteId);
                var skippedDecision = ResolveDecision(file, skippedQuota!.StatusCode, skippedQuota, settings.UsedPercentThreshold);
                skippedDecision.Reason = "免费账号已禁用，且周额度未重置，跳过本轮检查";
                skippedDecision.CheckedAt = nowUtc;
                return skippedDecision;
            }

            // 监控活跃且无调用活动时，尝试复用缓存。
            if (!forceRefresh && !hasUsage && monitorActive && QuotaCachePolicy.TryReuseQuota(
                existingQuota,
                displayAccount,
                file.Disabled,
                nowUtc,
                null,
                out var cachedQuota,
                out var _))
            {
                _store.SetQuota(file.Name, cachedQuota!, resolvedSiteId);
                var cachedDecision = ResolveDecision(file, cachedQuota!.StatusCode, cachedQuota, settings.UsedPercentThreshold);
                cachedDecision.CheckedAt = nowUtc;
                return cachedDecision;
            }

            // 真实探测请求统一走全局并发限制，自动与手动线路合并计数，超出后在这里排队等待。
            using var _ = await AcquireGlobalProbeSlotAsync(ct);
            var (statusCode, body) = await _cpa.RequestCodexUsageAsync(settings, authIndex, accountId, ct);

            // 解析额度并更新缓存。
            var quota = CodexQuotaParser.ParseQuotaSnapshot(
                file.Name,
                displayAccount,
                file.Disabled,
                statusCode,
                body);
            quota.FromCache = false;
            quota.CacheReason = "";
            quota.LastUsageAt = DateTime.MinValue;
            _store.SetQuota(file.Name, quota, resolvedSiteId);

            // 清除该账号的调用活动标记。
            _store.ClearAccountUsage(resolvedSiteId, authIndex.Trim());

            // 决策。
            var decision = ResolveDecision(file, statusCode, quota, settings.UsedPercentThreshold);
            decision.CheckedAt = DateTime.UtcNow;
            return decision;
        }
        catch (Exception ex)
        {
            _store.AddExceptionLog("inspection", "inspectAccount", "engine", ex, "探测账号异常", accountName: file.Name, displayAccount: displayAccount, siteId: resolvedSiteId);
            return new InspectionDecision
            {
                AccountName = file.Name,
                DisplayAccount = displayAccount,
                AuthIndex = authIndex,
                Action = InspectionAction.Keep,
                Reason = "探测异常，保留账号",
                StatusCode = 0,
                CheckedAt = DateTime.UtcNow,
                Disabled = file.Disabled,
                Error = ex.Message,
            };
        }
    }

    /// <summary>
    /// 并发探测多个账号。
    /// </summary>
    public async Task<List<InspectionDecision>> InspectAccountsAsync(
        string? siteId,
        List<AuthFileItem> files,
        int maxConcurrency,
        int batchDelayMinMs,
        int batchDelayMaxMs,
        bool forceRefresh = false,
        Func<InspectionDecision, int, int, Task>? onProgress = null,
        Func<int, int, TimeSpan, Task>? onBatchDelay = null,
        CancellationToken ct = default)
    {
        var resolvedSiteId = _store.ResolveSiteId(siteId);
        var results = new List<InspectionDecision>();
        var total = files.Count;
        if (total == 0)
        {
            return results;
        }

        var concurrency = Math.Max(1, maxConcurrency);
        var normalizedDelayMin = Math.Max(0, batchDelayMinMs);
        var normalizedDelayMax = Math.Max(normalizedDelayMin, batchDelayMaxMs);
        var totalBatches = (int)Math.Ceiling(total / (double)concurrency);
        var processed = 0;

        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = files
                .Skip(batchIndex * concurrency)
                .Take(concurrency)
                .ToList();

            var batchResults = await Task.WhenAll(batch.Select(file => InspectAccountAsync(resolvedSiteId, file, null, forceRefresh, ct)));
            foreach (var decision in batchResults.OrderBy(item => item.AccountName))
            {
                results.Add(decision);
                processed++;
                if (onProgress is not null)
                {
                    await onProgress(decision, processed, total);
                }
            }

            if (batchIndex >= totalBatches - 1 || normalizedDelayMax <= 0)
            {
                continue;
            }

            var delayMs = normalizedDelayMin == normalizedDelayMax
                ? normalizedDelayMin
                : Random.Shared.Next(normalizedDelayMin, normalizedDelayMax + 1);
            var delay = TimeSpan.FromMilliseconds(delayMs);

            if (onBatchDelay is not null)
            {
                await onBatchDelay(batchIndex + 1, totalBatches, delay);
            }

            await Task.Delay(delay, ct);
        }

        return results.OrderBy(decision => decision.AccountName).ToList();
    }

    /// <summary>
    /// 执行巡检动作（禁用/删除/启用）。
    /// </summary>
    public async Task<List<ActionOutcome>> ExecuteActionsAsync(
        string? siteId,
        List<InspectionDecision> decisions,
        int maxConcurrency,
        Func<ActionOutcome, int, int, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        var resolvedSiteId = _store.ResolveSiteId(siteId);
        var outcomes = new ConcurrentBag<ActionOutcome>();
        var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var actionItems = decisions
            .Where(decision => decision.Action != InspectionAction.Keep)
            .ToList();

        var total = actionItems.Count;
        var processed = 0;

        var tasks = actionItems.Select(async decision =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var outcome = await ExecuteSingleActionAsync(resolvedSiteId, decision, ct);
                outcomes.Add(outcome);

                var current = Interlocked.Increment(ref processed);
                if (onProgress is not null)
                {
                    await onProgress(outcome, current, total);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return outcomes.OrderBy(item => item.FileName).ToList();
    }

    /// <summary>
    /// 执行单个巡检动作（删除/禁用/启用），失败时记录异常日志。
    /// </summary>
    private async Task<ActionOutcome> ExecuteSingleActionAsync(
        string siteId,
        InspectionDecision decision,
        CancellationToken ct)
    {
        var settings = _store.GetSettings(siteId);

        try
        {
            switch (decision.Action)
            {
                case InspectionAction.Delete:
                    await _cpa.DeleteAccountAsync(settings, decision.AccountName, ct);
                    return new ActionOutcome
                    {
                        Action = "delete",
                        FileName = decision.AccountName,
                        DisplayAccount = decision.DisplayAccount,
                        Success = true,
                    };

                case InspectionAction.Disable:
                    await _cpa.DisableAccountAsync(settings, decision.AccountName, ct);
                    _store.UpdateAccountDisabledState(decision.AccountName, disabled: true, siteId);
                    return new ActionOutcome
                    {
                        Action = "disable",
                        FileName = decision.AccountName,
                        DisplayAccount = decision.DisplayAccount,
                        Success = true,
                    };

                case InspectionAction.Enable:
                    await _cpa.EnableAccountAsync(settings, decision.AccountName, ct);
                    _store.UpdateAccountDisabledState(decision.AccountName, disabled: false, siteId);
                    return new ActionOutcome
                    {
                        Action = "enable",
                        FileName = decision.AccountName,
                        DisplayAccount = decision.DisplayAccount,
                        Success = true,
                    };

                default:
                    return new ActionOutcome
                    {
                        Action = "keep",
                        FileName = decision.AccountName,
                        DisplayAccount = decision.DisplayAccount,
                        Success = true,
                    };
            }
        }
        catch (Exception ex)
        {
            _store.AddExceptionLog("inspection", "executeAction", "engine", ex, $"执行动作异常：{decision.Action}", accountName: decision.AccountName, displayAccount: decision.DisplayAccount, siteId: siteId);
            return new ActionOutcome
            {
                Action = decision.Action.ToString().ToLowerInvariant(),
                FileName = decision.AccountName,
                DisplayAccount = decision.DisplayAccount,
                Success = false,
                Error = ex.Message,
            };
        }
    }

    /// <summary>
    /// 根据巡检结果和额度状态决定应执行的动作（删除/禁用/启用/保留）。
    /// </summary>
    private InspectionDecision ResolveDecision(
        AuthFileItem file,
        int statusCode,
        CodexQuotaSnapshot quota,
        int threshold)
    {
        var displayAccount = ResolveDisplayAccount(file);
        var weeklyPercent = CodexQuotaParser.GetWeeklyUsedPercent(quota);
        var fiveHourPercent = CodexQuotaParser.GetFiveHourUsedPercent(quota);
        var isQuotaReached = CodexQuotaParser.IsQuotaReached(quota);
        var weeklyOverThreshold = weeklyPercent.HasValue && weeklyPercent.Value >= threshold;
        var fiveHourOverThreshold = fiveHourPercent.HasValue && fiveHourPercent.Value >= threshold;

        // 401 表示认证失效，建议删除。
        if (statusCode == 401)
        {
            return new InspectionDecision
            {
                AccountName = file.Name,
                DisplayAccount = displayAccount,
                AuthIndex = file.Auth_Index ?? file.AuthIndex ?? "",
                Action = InspectionAction.Delete,
                Reason = "接口返回 401，建议删除失效账号",
                StatusCode = statusCode,
                UsedPercent = weeklyPercent,
                IsQuotaReached = false,
                Disabled = file.Disabled,
            };
        }

        // 有周额度数据时，按阈值判断禁用/启用。
        if (weeklyPercent.HasValue)
        {
            if (weeklyOverThreshold)
            {
                if (file.Disabled)
                {
                    return BuildDecision(file, InspectionAction.Keep,
                        "周额度达到阈值，但账号已禁用", statusCode, weeklyPercent, true);
                }
                return BuildDecision(file, InspectionAction.Disable,
                    "周额度达到阈值，建议禁用账号", statusCode, weeklyPercent, true);
            }

            if (file.Disabled)
            {
                return BuildDecision(file, InspectionAction.Enable,
                    fiveHourOverThreshold
                        ? "5 小时额度达到阈值，但周额度仍可用，建议立即启用账号"
                        : "周额度仍可用，建议立即启用账号",
                    statusCode, weeklyPercent, false);
            }

            if (fiveHourOverThreshold)
            {
                return BuildDecision(file, InspectionAction.Keep,
                    "5 小时额度达到阈值，但周额度仍可用，暂不禁用账号",
                    statusCode, weeklyPercent, false);
            }

            return BuildDecision(file, InspectionAction.Keep,
                "周额度仍可用，无需处理", statusCode, weeklyPercent, false);
        }

        // 无周额度数据但有额度耗尽标记时，按耗尽处理。
        var overThreshold = isQuotaReached;
        if (isQuotaReached || overThreshold)
        {
            if (file.Disabled)
            {
                return BuildDecision(file, InspectionAction.Keep,
                    "额度已耗尽，但账号已禁用", statusCode, null, true);
            }
            return BuildDecision(file, InspectionAction.Disable,
                "额度已耗尽，建议禁用账号", statusCode, null, true);
        }

        // 账号恢复健康且当前禁用，建议启用。
        if (statusCode == 200 && file.Disabled)
        {
            return BuildDecision(file, InspectionAction.Enable,
                "账号恢复健康，建议重新启用", statusCode, null, false);
        }

        return BuildDecision(file, InspectionAction.Keep,
            "无需处理", statusCode, null, false);
    }

    /// <summary>
    /// 构建巡检决策结果对象。
    /// </summary>
    private InspectionDecision BuildDecision(
        AuthFileItem file,
        InspectionAction action,
        string reason,
        int statusCode,
        double? usedPercent,
        bool isQuotaReached)
    {
        return new InspectionDecision
        {
            AccountName = file.Name,
            DisplayAccount = ResolveDisplayAccount(file),
            AuthIndex = file.Auth_Index ?? file.AuthIndex ?? "",
            Action = action,
            Reason = reason,
            StatusCode = statusCode,
            UsedPercent = usedPercent,
            IsQuotaReached = isQuotaReached,
            Disabled = file.Disabled,
        };
    }

    /// <summary>
    /// 根据自动动作模式筛选需要执行的决策，将 Delete 模式降级为 Disable。
    /// </summary>
    public static List<InspectionDecision> FilterAutoActionItems(
        AutoActionMode mode,
        bool autoEnable,
        List<InspectionDecision> decisions)
    {
        var result = new List<InspectionDecision>();

        if (mode == AutoActionMode.Disable)
        {
            result.AddRange(decisions
                .Where(decision => decision.Action is InspectionAction.Delete or InspectionAction.Disable)
                .Select(decision =>
                {
                    if (decision.Action == InspectionAction.Delete)
                    {
                        decision.Action = InspectionAction.Disable;
                        decision.Reason = $"{decision.Reason}；自动禁用策略改为禁用账号";
                    }
                    return decision;
                }));
        }
        else if (mode == AutoActionMode.Delete)
        {
            result.AddRange(decisions
                .Where(decision => decision.Action is InspectionAction.Delete or InspectionAction.Disable));
        }

        if (autoEnable)
        {
            result.AddRange(decisions.Where(decision => decision.Action == InspectionAction.Enable));
        }

        return result;
    }

    /// <summary>
    /// 检查账号文件的 type 或 provider 是否匹配目标供应商。
    /// </summary>
    private static bool MatchesProvider(AuthFileItem file, string targetProvider)
    {
        var type = (file.Type ?? "").Trim().ToLowerInvariant();
        var provider = (file.Provider ?? "").Trim().ToLowerInvariant();
        var target = targetProvider.Trim().ToLowerInvariant();
        return type == target || provider == target;
    }

    /// <summary>
    /// 从账号文件中提取显示名称，按 account、email、label、name 优先级依次尝试。
    /// </summary>
    private static string ResolveDisplayAccount(AuthFileItem file)
    {
        var account = (file.Account ?? "").Trim();
        if (account.Length > 0)
        {
            return account;
        }

        var email = (file.Email ?? "").Trim();
        if (email.Length > 0)
        {
            return email;
        }

        var label = (file.Label ?? "").Trim();
        if (label.Length > 0)
        {
            return label;
        }

        return file.Name;
    }

    /// <summary>
    /// 提取 ChatGPT 账号 ID，用于补充请求头。
    /// </summary>
    private static string? ResolveAccountId(AuthFileItem file)
    {
        var id = (file.Chatgpt_Account_Id ?? file.ChatgptAccount_Id ?? "").Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
