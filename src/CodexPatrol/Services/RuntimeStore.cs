using System.Collections.Concurrent;
using System.Text.Json;
using CodexPatrol.Models;
using CodexPatrol.Serialization;

namespace CodexPatrol.Services;

/// <summary>
/// 内存状态仓库：按站点管理运行时额度数据、巡检结果、例外名单等。
/// </summary>
public sealed class RuntimeStore
{
    /// <summary>
    /// 单个站点最大保留的操作日志条数。
    /// </summary>
    private const int MaxOperationLogs = 200;

    /// <summary>
    /// 带缩进的 JSON 序列化上下文，用于持久化文件的可读性。
    /// </summary>
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = AppJsonContext.Default,
    };

    /// <summary>
    /// 获取泛型类型的缩进 JSON 序列化信息。
    /// </summary>
    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> GetIndentedTypeInfo<T>() where T : class
    {
        return (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)IndentedOptions.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// 应用根目录，用于定位配置文件。
    /// </summary>
    private readonly string _baseDirectory;

    /// <summary>
    /// patrol-config.json 文件路径。
    /// </summary>
    private readonly string _configFilePath;

    /// <summary>
    /// connection.json 文件路径。
    /// </summary>
    private readonly string _connectionFilePath;

    /// <summary>
    /// quota-cache.json 文件路径。
    /// </summary>
    private readonly string _quotaCacheFilePath;

    /// <summary>
    /// 配置文件读写锁。
    /// </summary>
    private readonly object _configLock = new();

    /// <summary>
    /// 日志文件写入器。
    /// </summary>
    private readonly OperationLogFileWriter _logFileWriter;

    /// <summary>
    /// 从 appsettings.json 加载的默认配置，作为新建站点的初始值。
    /// </summary>
    private readonly PatrolSettings _legacyDefaults;

    /// <summary>
    /// 按站点 ID 索引的运行时状态字典。
    /// </summary>
    private readonly ConcurrentDictionary<string, SiteRuntimeState> _siteStates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造 RuntimeStore：加载配置文件、初始化站点状态、恢复额度缓存。
    /// </summary>
    public RuntimeStore(PatrolSettings settings, OperationLogFileWriter logFileWriter, string? baseDirectory = null)
    {
        _legacyDefaults = settings;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _logFileWriter = logFileWriter;

        _configFilePath = Path.Combine(_baseDirectory, "patrol-config.json");
        _connectionFilePath = Path.Combine(_baseDirectory, "connection.json");
        _quotaCacheFilePath = Path.Combine(_baseDirectory, "quota-cache.json");

        var patrolConfig = LoadPatrolConfig();
        var connectionConfig = LoadConnectionConfig();
        InitializeSiteStates(patrolConfig, connectionConfig);
        ApplyPersistedQuotaState(LoadQuotaState());

        // 确保必要的配置文件存在，缺失则用当前默认值生成。
        EnsureConfigFilesExist();
    }

    // ========== Sites ==========

    /// <summary>
    /// 获取第一个已启用站点的 SiteId，若均未启用则取第一个站点，兜底返回 "default"。
    /// </summary>
    public string GetDefaultSiteId()
    {
        var enabled = _siteStates.Values
            .Select(state => state.Settings)
            .Where(site => site.Enabled)
            .OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (enabled is not null)
        {
            return enabled.SiteId;
        }

        var first = _siteStates.Values
            .Select(state => state.Settings)
            .OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return first?.SiteId ?? "default";
    }

    /// <summary>
    /// 解析站点 ID：请求的 ID 有效则使用，否则回退到默认站点。
    /// </summary>
    public string ResolveSiteId(string? requestedSiteId)
    {
        if (!string.IsNullOrWhiteSpace(requestedSiteId) && _siteStates.ContainsKey(requestedSiteId))
        {
            return requestedSiteId;
        }

        return GetDefaultSiteId();
    }

    /// <summary>
    /// 获取所有站点的配置副本列表，按名称排序。
    /// </summary>
    public List<PatrolSiteSettings> GetSites()
    {
        return _siteStates.Values
            .Select(state => state.Settings.Clone())
            .OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 检查指定站点是否存在。
    /// </summary>
    public bool HasSite(string siteId)
    {
        return !string.IsNullOrWhiteSpace(siteId) && _siteStates.ContainsKey(siteId.Trim());
    }

    /// <summary>
    /// 创建新站点，生成唯一 ID 并持久化配置。
    /// </summary>
    public PatrolSiteSettings CreateSite(SaveSettingsRequest payload)
    {
        lock (_configLock)
        {
            var siteId = BuildUniqueSiteId(payload.SiteId, payload.SiteName);
            var site = BuildDefaultSiteSettings(siteId);
            ApplyPayload(site, payload, isNewSite: true);
            NormalizeSiteSettings(site);
            site.SiteId = siteId;

            var state = new SiteRuntimeState
            {
                Settings = site,
            };
            _siteStates[siteId] = state;
            SavePatrolConfig();
            SaveConnectionInfo();
            SaveQuotaState();
            return site.Clone();
        }
    }

    /// <summary>
    /// 删除站点，要求至少保留一个站点，且站点不能处于忙碌状态。
    /// </summary>
    public bool DeleteSite(string siteId, out string error)
    {
        lock (_configLock)
        {
            if (_siteStates.Count <= 1)
            {
                error = "至少保留一个站点";
                return false;
            }

            if (!_siteStates.TryGetValue(siteId, out var state))
            {
                error = "站点不存在";
                return false;
            }

            if (IsSiteBusy(state))
            {
                error = "站点正在执行任务，暂时不能删除";
                return false;
            }

            if (!_siteStates.TryRemove(siteId, out _))
            {
                error = "站点不存在";
                return false;
            }

            SavePatrolConfig();
            SaveConnectionInfo();
            SaveQuotaState();
            error = string.Empty;
            return true;
        }
    }

    // ========== Settings ==========

    /// <summary>
    /// 获取指定站点的配置副本。
    /// </summary>
    public PatrolSiteSettings GetSettings(string? siteId = null)
    {
        return GetState(siteId).Settings.Clone();
    }

    /// <summary>
    /// 使用默认站点更新配置，通过委托直接修改配置对象后自动持久化。
    /// </summary>
    public void UpdateSettings(Action<PatrolSiteSettings> updater)
    {
        UpdateSettings(null, updater);
    }

    /// <summary>
    /// 使用指定站点更新配置，通过委托直接修改配置对象后自动持久化。
    /// </summary>
    public void UpdateSettings(string? siteId, Action<PatrolSiteSettings> updater)
    {
        lock (_configLock)
        {
            var state = GetState(siteId);
            updater(state.Settings);
            NormalizeSiteSettings(state.Settings);
            SavePatrolConfig();
            SaveConnectionInfo();
        }
    }

    /// <summary>
    /// 将前端提交的配置请求应用到指定站点并持久化。
    /// </summary>
    public void ApplySettings(SaveSettingsRequest payload)
    {
        lock (_configLock)
        {
            var state = GetState(payload.SiteId);
            ApplyPayload(state.Settings, payload, isNewSite: false);
            NormalizeSiteSettings(state.Settings);
            SavePatrolConfig();
            SaveConnectionInfo();
        }
    }

    // ========== Exceptions ==========

    /// <summary>
    /// 获取指定站点的例外账号名单副本。
    /// </summary>
    public HashSet<string> GetExceptions(string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.SyncRoot)
        {
            return state.Exceptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 添加一个例外账号。
    /// </summary>
    public void AddException(string accountName, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            if (state.Exceptions.Add(accountName))
            {
                SavePatrolConfig();
            }
        }
    }

    /// <summary>
    /// 移除一个例外账号。
    /// </summary>
    public void RemoveException(string accountName, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            state.Exceptions.Remove(accountName);
            SavePatrolConfig();
        }
    }

    /// <summary>
    /// 整体替换指定站点的例外账号名单。
    /// </summary>
    public void SetExceptions(List<string> accountNames, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            state.Exceptions.Clear();
            foreach (var name in accountNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                state.Exceptions.Add(name);
            }
            SavePatrolConfig();
        }
    }

    // ========== Accounts ==========

    /// <summary>
    /// 整体替换指定站点的账号列表，同步更新额度快照（新增补占位、移除清理）。
    /// </summary>
    public void SetAccounts(List<AuthFileItem> files, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            foreach (var kvp in state.Accounts.Keys.ToList())
            {
                state.Accounts.TryRemove(kvp, out _);
            }

            var accountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                state.Accounts[file.Name] = file;
                accountNames.Add(file.Name);

                // 初始化禁用原因：CPA 同步的 disabled 状态且无已知原因时标记为 ManualDisabled
                if (file.Disabled && !state.DisableReasons.ContainsKey(file.Name))
                {
                    state.DisableReasons[file.Name] = DisableReason.ManualDisabled;
                }
                else if (!file.Disabled)
                {
                    state.DisableReasons.TryRemove(file.Name, out _);
                }

                if (state.Quotas.TryGetValue(file.Name, out var quota))
                {
                    quota.AccountName = file.Name;
                    quota.DisplayAccount = ResolveDisplayAccount(file);
                    quota.Disabled = file.Disabled;
                }
                else
                {
                    state.Quotas[file.Name] = BuildPlaceholderQuota(file);
                }
            }

            foreach (var key in state.Quotas.Keys.ToList())
            {
                if (!accountNames.Contains(key))
                {
                    state.Quotas.TryRemove(key, out _);
                }
            }

            MergeAccountsIntoPriorityRouting(state, files);
            SaveQuotaState();
        }
    }

    /// <summary>
    /// 获取指定站点的所有账号。
    /// </summary>
    public List<AuthFileItem> GetAccounts(string? siteId = null) => GetState(siteId).Accounts.Values.ToList();

    /// <summary>
    /// 按名称获取单个账号。
    /// </summary>
    public AuthFileItem? GetAccount(string name, string? siteId = null)
    {
        return GetState(siteId).Accounts.TryGetValue(name, out var item) ? item : null;
    }

    /// <summary>
    /// 更新账号的禁用状态，同步更新账号对象和额度快照。
    /// </summary>
    public bool UpdateAccountDisabledState(string accountName, bool disabled, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            var changed = false;

            if (state.Accounts.TryGetValue(accountName, out var account))
            {
                account.Disabled = disabled;
                account.Status = disabled ? "disabled" : "active";
                changed = true;
            }

            if (state.Quotas.TryGetValue(accountName, out var quota))
            {
                quota.Disabled = disabled;
            }

            SaveQuotaState();
            return changed;
        }
    }

    /// <summary>
    /// 更新账号的禁用状态并同步设置禁用原因。
    /// </summary>
    public bool UpdateAccountDisabledState(string accountName, bool disabled, DisableReason reason, string? siteId = null)
    {
        var result = UpdateAccountDisabledState(accountName, disabled, siteId);
        if (disabled)
        {
            SetDisableReason(accountName, reason, siteId);
        }
        else
        {
            ClearDisableReason(accountName, siteId);
        }
        return result;
    }

    // ========== Priority Routing ==========

    /// <summary>
    /// 获取指定站点的账号优先级配置列表。
    /// </summary>
    public List<AccountPriority> GetAccountPriorities(string? siteId = null)
    {
        var state = GetState(siteId);
        return GetOrderedAccountPriorities(state);
    }

    /// <summary>
    /// 设置指定站点的账号优先级配置并持久化。
    /// </summary>
    public void SetAccountPriorities(List<AccountPriority> priorities, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            SaveOrderedAccountPriorities(state,
                NormalizePrioritySequence(priorities
                    .OrderBy(p => p.Priority)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)));
        }
    }

    /// <summary>
    /// 巡检完成后按既定规则自动重建优先级顺序，并确保优先级唯一。
    /// </summary>
    public bool TryAutoReorderAccountPriorities(
        IReadOnlyCollection<string> inspectedAccountNames,
        int threshold,
        out List<AccountPriority> priorities,
        string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            var ordered = GetOrderedAccountPriorities(state);
            if (ordered.Count == 0)
            {
                priorities = [];
                return false;
            }

            var inspected = new HashSet<string>(
                inspectedAccountNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            var baseline = new List<AccountPriority>();
            var pendingUninspected = new List<AccountPriority>();
            var newFreeEntries = new List<(AccountPriority Priority, double UsedPercent)>();
            var newOtherEntries = new List<AccountPriority>();
            var bottomEntries = new List<AccountPriority>();

            foreach (var entry in ordered)
            {
                if (state.Exceptions.Contains(entry.Name))
                {
                    baseline.Add(CloneAccountPriority(entry));
                    continue;
                }

                var wasPending = entry.PendingFirstInspection;
                var quota = state.Quotas.TryGetValue(entry.Name, out var snapshot) ? snapshot : null;
                var isFreePlan = string.Equals(quota?.PlanType, "Free", StringComparison.OrdinalIgnoreCase);
                var weeklyPercent = quota is not null ? CodexQuotaParser.GetWeeklyUsedPercent(quota) : null;
                var isFreeExhausted = isFreePlan && weeklyPercent.HasValue && weeklyPercent.Value >= threshold;

                if (wasPending && inspected.Contains(entry.Name))
                {
                    if (isFreePlan && !weeklyPercent.HasValue)
                    {
                        pendingUninspected.Add(CloneAccountPriority(entry));
                        continue;
                    }

                    entry.PendingFirstInspection = false;
                }

                if (entry.PendingFirstInspection)
                {
                    pendingUninspected.Add(CloneAccountPriority(entry));
                    continue;
                }

                if (isFreeExhausted)
                {
                    bottomEntries.Add(CloneAccountPriority(entry));
                    continue;
                }

                if (wasPending)
                {
                    if (isFreePlan && weeklyPercent.HasValue)
                    {
                        newFreeEntries.Add((CloneAccountPriority(entry), weeklyPercent.Value));
                    }
                    else
                    {
                        newOtherEntries.Add(CloneAccountPriority(entry));
                    }

                    continue;
                }

                baseline.Add(CloneAccountPriority(entry));
            }

            var insertAfterIndex = baseline.FindLastIndex(entry =>
            {
                if (!state.Quotas.TryGetValue(entry.Name, out var quota)
                    || !string.Equals(quota.PlanType, "Free", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var weeklyPercent = CodexQuotaParser.GetWeeklyUsedPercent(quota);
                return weeklyPercent.HasValue && weeklyPercent.Value < threshold;
            });

            var finalOrder = new List<AccountPriority>();
            if (insertAfterIndex >= 0)
            {
                finalOrder.AddRange(baseline.Take(insertAfterIndex + 1));
            }
            else
            {
                finalOrder.AddRange(baseline);
            }

            finalOrder.AddRange(newFreeEntries
                .OrderBy(item => item.UsedPercent)
                .ThenBy(item => item.Priority.Priority)
                .Select(item => item.Priority));
            finalOrder.AddRange(newOtherEntries);

            if (insertAfterIndex >= 0)
            {
                finalOrder.AddRange(baseline.Skip(insertAfterIndex + 1));
            }

            finalOrder.AddRange(pendingUninspected);
            finalOrder.AddRange(bottomEntries);

            var normalized = NormalizePrioritySequence(finalOrder);
            var changed = !AreSamePrioritySequence(ordered, normalized);
            priorities = normalized.Select(CloneAccountPriority).ToList();
            if (!changed)
            {
                return false;
            }

            SaveOrderedAccountPriorities(state, normalized);
            return true;
        }
    }

    /// <summary>
    /// 获取指定账号的优先级数值，未配置时返回 null。
    /// </summary>
    public int? GetAccountPriority(string accountName, string? siteId = null)
    {
        var state = GetState(siteId);
        return state.AccountPriorities.TryGetValue(accountName, out var priority) ? priority.Priority : null;
    }

    /// <summary>
    /// 将当前账号列表并入优先级配置：新增账号补到末尾并标记为待首检，移除的账号则自动清理。
    /// </summary>
    private void MergeAccountsIntoPriorityRouting(SiteRuntimeState state, IReadOnlyList<AuthFileItem> files)
    {
        if (state.AccountPriorities.Count == 0 && !state.Settings.PriorityRoutingEnabled)
        {
            return;
        }

        var currentNames = new HashSet<string>(files
            .Where(file => !string.IsNullOrWhiteSpace(file.Name))
            .Select(file => file.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var merged = GetOrderedAccountPriorities(state)
            .Where(priority => currentNames.Contains(priority.Name))
            .ToList();
        var changed = merged.Count != state.AccountPriorities.Count;
        var knownNames = merged.Select(priority => priority.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var name = file.Name.Trim();
            if (name.Length == 0 || knownNames.Contains(name) || state.Exceptions.Contains(name))
            {
                continue;
            }

            merged.Add(new AccountPriority
            {
                Name = name,
                Priority = merged.Count + 1,
                PendingFirstInspection = true,
            });
            knownNames.Add(name);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        SaveOrderedAccountPriorities(state, NormalizePrioritySequence(merged));
    }

    /// <summary>
    /// 返回当前站点按优先级升序排列的账号优先级条目副本。
    /// </summary>
    private static List<AccountPriority> GetOrderedAccountPriorities(SiteRuntimeState state)
    {
        return state.AccountPriorities.Values
            .Where(priority => !string.IsNullOrWhiteSpace(priority.Name) && priority.Priority > 0)
            .OrderBy(priority => priority.Priority)
            .ThenBy(priority => priority.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CloneAccountPriority)
            .ToList();
    }

    /// <summary>
    /// 按当前枚举顺序重新编号为唯一的 1..N 优先级。
    /// </summary>
    private static List<AccountPriority> NormalizePrioritySequence(IEnumerable<AccountPriority> priorities)
    {
        var normalized = new List<AccountPriority>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var priority in priorities)
        {
            var name = priority.Name.Trim();
            if (name.Length == 0 || priority.Priority <= 0 || !seenNames.Add(name))
            {
                continue;
            }

            normalized.Add(new AccountPriority
            {
                Name = name,
                Priority = normalized.Count + 1,
                PendingFirstInspection = priority.PendingFirstInspection,
            });
        }

        return normalized;
    }

    /// <summary>
    /// 比较两组优先级序列的名称、顺序和待首检状态是否完全一致。
    /// </summary>
    private static bool AreSamePrioritySequence(IReadOnlyList<AccountPriority> current, IReadOnlyList<AccountPriority> updated)
    {
        if (current.Count != updated.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            var left = current[index];
            var right = updated[index];
            if (!string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                || left.Priority != right.Priority
                || left.PendingFirstInspection != right.PendingFirstInspection)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 用新的有序优先级列表整体替换站点优先级配置并持久化。
    /// </summary>
    private void SaveOrderedAccountPriorities(SiteRuntimeState state, IReadOnlyList<AccountPriority> priorities)
    {
        state.AccountPriorities.Clear();
        foreach (var priority in priorities)
        {
            state.AccountPriorities[priority.Name] = CloneAccountPriority(priority);
        }

        SavePatrolConfig();
    }

    /// <summary>
    /// 克隆优先级条目，避免运行时状态被外部引用直接修改。
    /// </summary>
    private static AccountPriority CloneAccountPriority(AccountPriority priority)
    {
        return new AccountPriority
        {
            Name = priority.Name,
            Priority = priority.Priority,
            PendingFirstInspection = priority.PendingFirstInspection,
        };
    }

    /// <summary>
    /// 获取账号禁用原因。
    /// </summary>
    public DisableReason GetDisableReason(string accountName, string? siteId = null)
    {
        var state = GetState(siteId);
        return state.DisableReasons.TryGetValue(accountName, out var reason) ? reason : DisableReason.None;
    }

    /// <summary>
    /// 设置账号禁用原因。
    /// </summary>
    public void SetDisableReason(string accountName, DisableReason reason, string? siteId = null)
    {
        var state = GetState(siteId);
        state.DisableReasons[accountName] = reason;
    }

    /// <summary>
    /// 清除账号禁用原因。
    /// </summary>
    public void ClearDisableReason(string accountName, string? siteId = null)
    {
        var state = GetState(siteId);
        state.DisableReasons.TryRemove(accountName, out _);
    }

    // ========== Quotas ==========

    /// <summary>
    /// 设置单个账号的额度快照并持久化。
    /// </summary>
    public void SetQuota(string accountName, CodexQuotaSnapshot snapshot, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            PreserveQuotaRuntimeState(
                state.Quotas.TryGetValue(accountName, out var existingQuota) ? existingQuota : null,
                snapshot);
            state.Quotas[accountName] = snapshot;
            SaveQuotaState();
        }
    }

    /// <summary>
    /// 获取指定站点所有有可见数据的额度快照。
    /// </summary>
    public List<CodexQuotaSnapshot> GetQuotas(string? siteId = null)
    {
        var state = GetState(siteId);
        return state.Quotas.Values
            .Where(HasVisibleQuota)
            .Select(quota =>
            {
                var clone = CloneQuotaForOutput(quota);
                clone.DisableReason = state.DisableReasons.TryGetValue(quota.AccountName, out var reason) && reason != DisableReason.None
                    ? reason.ToString()
                    : "";
                return clone;
            })
            .ToList();
    }

    /// <summary>
    /// 获取单个账号的额度快照。
    /// </summary>
    public CodexQuotaSnapshot? GetQuota(string accountName, string? siteId = null)
    {
        return GetState(siteId).Quotas.TryGetValue(accountName, out var quota) ? quota : null;
    }

    /// <summary>
    /// 清除所有额度快照，并为现有账号重建占位快照。
    /// </summary>
    public void ClearQuotas(string? siteId = null)
    {
        var state = GetState(siteId);
        lock (_configLock)
        {
            foreach (var key in state.Quotas.Keys.ToList())
            {
                state.Quotas.TryRemove(key, out _);
            }

            foreach (var account in state.Accounts.Values)
            {
                state.Quotas[account.Name] = BuildPlaceholderQuota(account);
            }

            SaveQuotaState();
        }
    }

    // ========== Last Inspection ==========

    /// <summary>
    /// 获取最近一次巡检结果。
    /// </summary>
    public InspectionRunResult? GetLastRun(string? siteId = null) => GetState(siteId).LastRun;

    /// <summary>
    /// 设置最近一次巡检结果。
    /// </summary>
    public void SetLastRun(InspectionRunResult result, string? siteId = null)
    {
        GetState(siteId).LastRun = result;
    }

    // ========== Polling Runtime ==========

    /// <summary>
    /// 获取指定站点是否正在轮询。
    /// </summary>
    public bool IsPolling(string? siteId = null) => GetState(siteId).IsPolling;

    /// <summary>
    /// 设置指定站点的轮询状态。
    /// </summary>
    public void SetPollingState(bool isPolling, string? siteId = null)
    {
        GetState(siteId).IsPolling = isPolling;
    }

    /// <summary>
    /// 获取下次计划巡检时间。
    /// </summary>
    public DateTime GetNextScheduledAt(string? siteId = null) => GetState(siteId).NextScheduledAt;

    /// <summary>
    /// 设置下次计划巡检时间。
    /// </summary>
    public void SetNextScheduledAt(DateTime value, string? siteId = null)
    {
        GetState(siteId).NextScheduledAt = value;
    }

    /// <summary>
    /// 获取下次额度重置检查时间。
    /// </summary>
    public DateTime GetNextResetCheckAt(string? siteId = null) => GetState(siteId).NextResetCheckAt;

    /// <summary>
    /// 设置下次额度重置检查时间。
    /// </summary>
    public void SetNextResetCheckAt(DateTime value, string? siteId = null)
    {
        GetState(siteId).NextResetCheckAt = value;
    }

    /// <summary>
    /// 获取最近一次巡检开始时间。
    /// </summary>
    public DateTime GetLastRunStartedAt(string? siteId = null) => GetState(siteId).LastRunStartedAt;

    /// <summary>
    /// 设置最近一次巡检开始时间。
    /// </summary>
    public void SetLastRunStartedAt(DateTime value, string? siteId = null)
    {
        GetState(siteId).LastRunStartedAt = value;
    }

    /// <summary>
    /// 获取最近一次巡检完成时间。
    /// </summary>
    public DateTime GetLastRunFinishedAt(string? siteId = null) => GetState(siteId).LastRunFinishedAt;

    /// <summary>
    /// 设置最近一次巡检完成时间。
    /// </summary>
    public void SetLastRunFinishedAt(DateTime value, string? siteId = null)
    {
        GetState(siteId).LastRunFinishedAt = value;
    }

    // ========== Operation Logs ==========

    /// <summary>
    /// 获取操作日志，按 ID 降序返回，最多 MaxOperationLogs 条。
    /// </summary>
    public List<OperationLogEntry> GetOperationLogs(int limit = MaxOperationLogs, string? siteId = null)
    {
        var safeLimit = Math.Clamp(limit, 1, MaxOperationLogs);
        return GetState(siteId).OperationLogs
            .ToArray()
            .OrderByDescending(item => item.Id)
            .Take(safeLimit)
            .ToList();
    }

    /// <summary>
    /// 添加操作日志，同时写入内存队列和本地文件。
    /// </summary>
    public OperationLogEntry AddOperationLog(
        string category,
        string operationType,
        string source,
        string message,
        string level = "info",
        string accountName = "",
        string displayAccount = "",
        string? siteId = null)
    {
        var state = GetState(siteId);
        var entry = new OperationLogEntry
        {
            Id = Interlocked.Increment(ref state.NextLogId),
            CreatedAt = DateTime.UtcNow,
            Level = level,
            Category = category,
            OperationType = operationType,
            Source = source,
            Message = message,
            AccountName = accountName,
            DisplayAccount = displayAccount,
            SiteId = state.Settings.SiteId,
            SiteName = state.Settings.Name,
        };

        state.OperationLogs.Enqueue(entry);
        while (state.OperationLogs.Count > MaxOperationLogs)
        {
            state.OperationLogs.TryDequeue(out _);
        }

        _logFileWriter.Write(entry);
        return entry;
    }

    /// <summary>
    /// 添加异常日志，仅写入本地错误日志文件（不进内存队列）。
    /// </summary>
    public void AddExceptionLog(
        string category,
        string operationType,
        string source,
        Exception exception,
        string message = "",
        string level = "error",
        string accountName = "",
        string displayAccount = "",
        string? siteId = null)
    {
        var resolvedSiteId = siteId;
        if (string.IsNullOrWhiteSpace(resolvedSiteId))
        {
            resolvedSiteId = ResolveSiteId(null);
        }

        var logSiteId = string.Empty;
        var logSiteName = string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedSiteId) && _siteStates.TryGetValue(resolvedSiteId, out var state))
        {
            logSiteId = state.Settings.SiteId;
            logSiteName = state.Settings.Name;
        }

        _logFileWriter.WriteException(exception, category, operationType, source, level, message, logSiteId, logSiteName, accountName, displayAccount);
    }

    // ========== Progress Runtime ==========

    /// <summary>
    /// 获取当前进度状态的快照（线程安全克隆）。
    /// </summary>
    public RuntimeProgressState GetProgress(string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.ProgressLock)
        {
            return CloneProgress(state.Progress);
        }
    }

    /// <summary>
    /// 初始化进度状态，标记为 running。
    /// </summary>
    public void StartProgress(string operationType, string source, int total, string message, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.ProgressLock)
        {
            var now = DateTime.UtcNow;
            state.Progress = new RuntimeProgressState
            {
                OperationType = operationType,
                Source = source,
                Status = "running",
                Stage = "prepare",
                Message = message,
                Total = Math.Max(0, total),
                StartedAt = now,
                UpdatedAt = now,
            };
        }
    }

    /// <summary>
    /// 更新进度状态，包括阶段、进度数和当前账号。
    /// </summary>
    public void UpdateProgress(
        string stage,
        string message,
        int? total = null,
        int? processed = null,
        int? actionTotal = null,
        int? actionProcessed = null,
        string currentAccountName = "",
        string currentDisplayAccount = "",
        string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.ProgressLock)
        {
            state.Progress.Stage = stage;
            state.Progress.Message = message;
            state.Progress.UpdatedAt = DateTime.UtcNow;
            state.Progress.Status = "running";

            if (total.HasValue)
            {
                state.Progress.Total = Math.Max(0, total.Value);
            }

            if (processed.HasValue)
            {
                state.Progress.Processed = Math.Clamp(processed.Value, 0, Math.Max(state.Progress.Total, processed.Value));
            }

            if (actionTotal.HasValue)
            {
                state.Progress.ActionTotal = Math.Max(0, actionTotal.Value);
            }

            if (actionProcessed.HasValue)
            {
                state.Progress.ActionProcessed = Math.Clamp(actionProcessed.Value, 0, Math.Max(state.Progress.ActionTotal, actionProcessed.Value));
            }

            state.Progress.CurrentAccountName = currentAccountName;
            state.Progress.CurrentDisplayAccount = currentDisplayAccount;
            state.Progress.Percent = CalculatePercent(state.Progress);
        }
    }

    /// <summary>
    /// 标记进度为已完成。
    /// </summary>
    public void CompleteProgress(string message, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.ProgressLock)
        {
            state.Progress.Status = "completed";
            state.Progress.Stage = "completed";
            state.Progress.Message = message;
            state.Progress.UpdatedAt = DateTime.UtcNow;
            state.Progress.FinishedAt = state.Progress.UpdatedAt;
            state.Progress.Percent = 100;
            if (state.Progress.Total > 0)
            {
                state.Progress.Processed = state.Progress.Total;
            }
            if (state.Progress.ActionTotal > 0)
            {
                state.Progress.ActionProcessed = state.Progress.ActionTotal;
            }
        }
    }

    /// <summary>
    /// 标记进度为失败。
    /// </summary>
    public void FailProgress(string message, string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.ProgressLock)
        {
            state.Progress.Status = "error";
            state.Progress.Stage = "error";
            state.Progress.Message = message;
            state.Progress.UpdatedAt = DateTime.UtcNow;
            state.Progress.FinishedAt = state.Progress.UpdatedAt;
            state.Progress.Percent = CalculatePercent(state.Progress);
        }
    }

    // ========== Usage Activity ==========

    /// <summary>
    /// 标记指定 auth_index 自上次额度刷新后有调用活动。
    /// </summary>
    public void MarkAccountUsage(string siteId, string authIndex)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.ActiveAuthIndices.Add(authIndex);
        }
    }

    /// <summary>
    /// 查询指定 auth_index 是否有调用活动。
    /// </summary>
    public bool HasAccountUsage(string siteId, string authIndex)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            return state.ActiveAuthIndices.Contains(authIndex);
        }
    }

    /// <summary>
    /// 清除指定 auth_index 的调用活动标记（额度刷新后调用）。
    /// </summary>
    public void ClearAccountUsage(string siteId, string authIndex)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.ActiveAuthIndices.Remove(authIndex);
        }
    }

    /// <summary>
    /// 返回指定站点的 usage-queue 监控是否活跃。
    /// </summary>
    public bool IsUsageMonitorActive(string? siteId = null)
    {
        var state = GetState(siteId);
        lock (state.SyncRoot)
        {
            return state.UsageMonitorActive;
        }
    }

    /// <summary>
    /// 设置指定站点的 usage-queue 监控活跃状态。
    /// </summary>
    public void SetUsageMonitorActive(string siteId, bool active)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.UsageMonitorActive = active;
        }
    }

    /// <summary>
    /// 标记指定站点的 CPA 不支持 usage-queue（404 后不再重试）。
    /// </summary>
    public void MarkUsageQueueUnsupported(string siteId)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.UsageQueueUnsupported = true;
            state.UsageMonitorActive = false;
        }
    }

    /// <summary>
    /// 查询指定站点的 CPA 是否不支持 usage-queue。
    /// </summary>
    public bool IsUsageQueueUnsupported(string siteId)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            return state.UsageQueueUnsupported;
        }
    }

    /// <summary>
    /// 获取所有已启用站点的 SiteId 列表。
    /// </summary>
    public List<string> GetEnabledSiteIds()
    {
        return _siteStates.Values
            .Where(state => state.Settings.Enabled)
            .Select(state => state.Settings.SiteId)
            .OrderBy(siteId => siteId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 更新 usage-queue 监控统计（轮询成功后调用）。
    /// </summary>
    public void RecordUsageMonitorPoll(string siteId, int itemsPopped)
    {
        if (!_siteStates.TryGetValue(siteId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.UsageMonitorPollCount++;
            state.UsageMonitorTotalItemsPopped += itemsPopped;
            state.UsageMonitorLastPollAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 获取所有站点的 usage-queue 监控状态。
    /// </summary>
    public List<UsageMonitorStatus> GetUsageMonitorStatus()
    {
        return _siteStates.Values
            .OrderBy(state => state.Settings.Name, StringComparer.OrdinalIgnoreCase)
            .Select(state =>
            {
                lock (state.SyncRoot)
                {
                    return new UsageMonitorStatus
                    {
                        SiteId = state.Settings.SiteId,
                        SiteName = state.Settings.Name,
                        Active = state.UsageMonitorActive,
                        Unsupported = state.UsageQueueUnsupported,
                        LastPollAt = state.UsageMonitorLastPollAt,
                        PollCount = state.UsageMonitorPollCount,
                        TotalItemsPopped = state.UsageMonitorTotalItemsPopped,
                        ActiveAuthIndexCount = state.ActiveAuthIndices.Count,
                    };
                }
            })
            .ToList();
    }

    // ========== 配置持久化 ==========

    /// <summary>
    /// 从 patrol-config.json 加载巡检配置，兼容旧版单站点格式。
    /// </summary>
    private PatrolConfig LoadPatrolConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return new PatrolConfig();
            }

            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.PatrolConfig);
            if (config is not null)
            {
                return NormalizePatrolConfig(config);
            }

            var legacy = JsonSerializer.Deserialize(json, AppJsonContext.Default.ExceptionConfig);
            if (legacy is not null)
            {
                return new PatrolConfig
                {
                    Exceptions = legacy.Exceptions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                };
            }
        }
        catch
        {
        }

        return new PatrolConfig();
    }

    /// <summary>
    /// 从 connection.json 加载站点连接配置。
    /// </summary>
    private MultiSiteConnectionConfig LoadConnectionConfig()
    {
        try
        {
            if (!File.Exists(_connectionFilePath))
            {
                return new MultiSiteConnectionConfig();
            }

            var json = File.ReadAllText(_connectionFilePath);
            var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.MultiSiteConnectionConfig);
            return NormalizeConnectionConfig(config ?? new MultiSiteConnectionConfig());
        }
        catch
        {
            return new MultiSiteConnectionConfig();
        }
    }

    /// <summary>
    /// 从 quota-cache.json 加载额度缓存。
    /// </summary>
    private PersistedQuotaState LoadQuotaState()
    {
        try
        {
            if (!File.Exists(_quotaCacheFilePath))
            {
                return new PersistedQuotaState();
            }

            var json = File.ReadAllText(_quotaCacheFilePath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.PersistedQuotaState) ?? new PersistedQuotaState();
        }
        catch
        {
            return new PersistedQuotaState();
        }
    }

    /// <summary>
    /// 根据巡检配置和连接配置初始化各站点的运行时状态。
    /// </summary>
    private void InitializeSiteStates(PatrolConfig patrolConfig, MultiSiteConnectionConfig connectionConfig)
    {
        var normalizedPatrol = NormalizePatrolConfig(patrolConfig);
        var normalizedConnection = NormalizeConnectionConfig(connectionConfig);

        var siteConfigMap = normalizedPatrol.Sites
            .Where(site => !string.IsNullOrWhiteSpace(site.SiteId))
            .ToDictionary(site => site.SiteId, StringComparer.OrdinalIgnoreCase);
        var connectionSiteMap = normalizedConnection.Sites
            .Where(site => !string.IsNullOrWhiteSpace(site.SiteId))
            .ToDictionary(site => site.SiteId, StringComparer.OrdinalIgnoreCase);

        var orderedSiteIds = normalizedConnection.Sites
            .Select(site => site.SiteId)
            .Where(siteId => !string.IsNullOrWhiteSpace(siteId))
            .Concat(normalizedPatrol.Sites.Select(site => site.SiteId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedSiteIds.Count == 0)
        {
            orderedSiteIds.Add("default");
        }

        foreach (var siteId in orderedSiteIds)
        {
            connectionSiteMap.TryGetValue(siteId, out var connectionSite);
            siteConfigMap.TryGetValue(siteId, out var siteConfig);

            var settings = BuildDefaultSiteSettings(siteId);
            if (connectionSite is not null)
            {
                settings.SiteId = siteId;
                settings.Name = string.IsNullOrWhiteSpace(connectionSite.Name) ? settings.Name : connectionSite.Name;
                settings.Enabled = connectionSite.Enabled;
                if (!string.IsNullOrWhiteSpace(connectionSite.CpaBaseUrl))
                {
                    settings.CpaBaseUrl = connectionSite.CpaBaseUrl;
                }
                if (!string.IsNullOrWhiteSpace(connectionSite.ManagementKey))
                {
                    settings.ManagementKey = connectionSite.ManagementKey;
                }
                if (!string.IsNullOrWhiteSpace(connectionSite.Provider))
                {
                    settings.Provider = connectionSite.Provider;
                }
            }

            siteConfig?.Settings?.ApplyTo(settings);
            NormalizeSiteSettings(settings);

            var state = new SiteRuntimeState
            {
                Settings = settings,
            };
            foreach (var exception in (siteConfig?.Exceptions ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                state.Exceptions.Add(exception);
            }

            // 恢复账号优先级配置
            foreach (var priority in siteConfig?.AccountPriorities ?? [])
            {
                if (!string.IsNullOrWhiteSpace(priority.Name) && priority.Priority > 0)
                {
                    state.AccountPriorities[priority.Name] = new AccountPriority
                    {
                        Name = priority.Name.Trim(),
                        Priority = priority.Priority,
                        PendingFirstInspection = priority.PendingFirstInspection,
                    };
                }
            }

            _siteStates[settings.SiteId] = state;
        }
    }

    /// <summary>
    /// 将持久化的额度缓存数据恢复到对应站点。
    /// </summary>
    private void ApplyPersistedQuotaState(PersistedQuotaState quotaState)
    {
        foreach (var site in quotaState.Sites ?? [])
        {
            if (string.IsNullOrWhiteSpace(site.SiteId) || !_siteStates.TryGetValue(site.SiteId, out var state))
            {
                continue;
            }

            foreach (var quota in site.Quotas ?? [])
            {
                if (string.IsNullOrWhiteSpace(quota.AccountName))
                {
                    continue;
                }

                state.Quotas[quota.AccountName] = CloneQuotaForPersistence(quota);
            }
        }
    }

    /// <summary>
    /// 将所有站点的巡检配置序列化写入 patrol-config.json。
    /// </summary>
    private void SavePatrolConfig()
    {
        try
        {
            var config = new PatrolConfig
            {
                Sites = _siteStates.Values
                    .Select(state => new PatrolSiteConfig
                    {
                        SiteId = state.Settings.SiteId,
                        Exceptions = state.Exceptions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        AccountPriorities = GetOrderedAccountPriorities(state),
                        Settings = PersistedPatrolSettings.FromRuntime(state.Settings),
                    })
                    .OrderBy(site => site.SiteId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

            var json = JsonSerializer.Serialize(config, GetIndentedTypeInfo<PatrolConfig>());
            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // 写入失败不中断主流程
        }
    }

    /// <summary>
    /// 将所有站点的连接信息序列化写入 connection.json。
    /// </summary>
    private void SaveConnectionInfo()
    {
        try
        {
            var connection = new MultiSiteConnectionConfig
            {
                Sites = _siteStates.Values
                    .Select(state => new CpaConnectionSite
                    {
                        SiteId = state.Settings.SiteId,
                        Name = state.Settings.Name,
                        Enabled = state.Settings.Enabled,
                        CpaBaseUrl = state.Settings.CpaBaseUrl,
                        ManagementKey = state.Settings.ManagementKey,
                        Provider = state.Settings.Provider,
                    })
                    .OrderBy(site => site.SiteId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

            var json = JsonSerializer.Serialize(connection, GetIndentedTypeInfo<MultiSiteConnectionConfig>());
            File.WriteAllText(_connectionFilePath, json);
        }
        catch
        {
            // 写入失败不中断主流程
        }
    }

    /// <summary>
    /// 将所有站点的额度快照序列化写入 quota-cache.json。
    /// </summary>
    private void SaveQuotaState()
    {
        try
        {
            var quotaState = new PersistedQuotaState
            {
                Sites = _siteStates.Values
                    .Select(state => new PersistedQuotaSiteState
                    {
                        SiteId = state.Settings.SiteId,
                        Quotas = state.Quotas.Values
                            .Select(CloneQuotaForPersistence)
                            .OrderBy(snapshot => snapshot.AccountName, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                    })
                    .OrderBy(site => site.SiteId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

            var json = JsonSerializer.Serialize(quotaState, GetIndentedTypeInfo<PersistedQuotaState>());
            File.WriteAllText(_quotaCacheFilePath, json);
        }
        catch
        {
            // 写入失败不中断主流程
        }
    }

    /// <summary>
    /// 获取指定站点的运行时状态，站点不存在时抛出异常。
    /// </summary>
    private SiteRuntimeState GetState(string? siteId)
    {
        var resolvedSiteId = ResolveSiteId(siteId);
        if (_siteStates.TryGetValue(resolvedSiteId, out var state))
        {
            return state;
        }

        // 站点已被删除或配置异常。
        throw new InvalidOperationException($"站点 {resolvedSiteId} 不存在");
    }

    /// <summary>
    /// 检查站点是否处于忙碌状态（正在轮询或进度为 running）。
    /// </summary>
    private static bool IsSiteBusy(SiteRuntimeState state)
    {
        if (state.IsPolling)
        {
            return true;
        }

        lock (state.ProgressLock)
        {
            return string.Equals(state.Progress.Status, "running", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 使用 legacy 默认配置构建指定 ID 的站点配置。
    /// </summary>
    private PatrolSiteSettings BuildDefaultSiteSettings(string siteId)
    {
        var normalizedSiteId = string.IsNullOrWhiteSpace(siteId) ? "default" : siteId.Trim();
        return new PatrolSiteSettings
        {
            SiteId = normalizedSiteId,
            Name = string.Equals(normalizedSiteId, "default", StringComparison.OrdinalIgnoreCase) ? "默认站点" : normalizedSiteId,
            Enabled = true,
            CpaBaseUrl = string.IsNullOrWhiteSpace(_legacyDefaults.CpaBaseUrl) ? "http://localhost:8317" : _legacyDefaults.CpaBaseUrl,
            ManagementKey = _legacyDefaults.ManagementKey,
            AutoPollingEnabled = _legacyDefaults.AutoPollingEnabled,
            PollIntervalMinutes = _legacyDefaults.PollIntervalMinutes,
            PollRandomDelayMinMinutes = _legacyDefaults.PollRandomDelayMinMinutes,
            PollRandomDelayMaxMinutes = _legacyDefaults.PollRandomDelayMaxMinutes,
            ProbeWorkers = _legacyDefaults.ProbeWorkers,
            ProbeBatchDelayMinMs = _legacyDefaults.ProbeBatchDelayMinMs,
            ProbeBatchDelayMaxMs = _legacyDefaults.ProbeBatchDelayMaxMs,
            ActionWorkers = _legacyDefaults.ActionWorkers,
            TimeoutMs = _legacyDefaults.TimeoutMs,
            RetryCount = _legacyDefaults.RetryCount,
            AutoActionMode = _legacyDefaults.AutoActionMode,
            AutoEnableRecovered = _legacyDefaults.AutoEnableRecovered,
            UsedPercentThreshold = _legacyDefaults.UsedPercentThreshold,
            Provider = string.IsNullOrWhiteSpace(_legacyDefaults.Provider) ? "codex" : _legacyDefaults.Provider,
        };
    }

    /// <summary>
    /// 为尚未探测的账号构建占位额度快照。
    /// </summary>
    private static CodexQuotaSnapshot BuildPlaceholderQuota(AuthFileItem file)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = file.Name,
            DisplayAccount = ResolveDisplayAccount(file),
            Disabled = file.Disabled,
            CheckedAt = DateTime.MinValue,
            RefreshedAt = DateTime.MinValue,
            LastUsageAt = DateTime.MinValue,
        };
    }

    /// <summary>
    /// 判断额度快照是否有可见数据（非空占位）。
    /// </summary>
    private static bool HasVisibleQuota(CodexQuotaSnapshot quota)
    {
        return quota.Success
            || quota.StatusCode != 0
            || quota.CheckedAt != DateTime.MinValue
            || quota.RefreshedAt != DateTime.MinValue
            || quota.Windows.Count > 0
            || !string.IsNullOrWhiteSpace(quota.ErrorMessage);
    }

    /// <summary>
    /// 在同一重置点刷新额度时，保留窗口的运行时处理标记，避免重复触发补检。
    /// </summary>
    private static void PreserveQuotaRuntimeState(CodexQuotaSnapshot? existingQuota, CodexQuotaSnapshot nextQuota)
    {
        if (existingQuota is null || existingQuota.Windows.Count == 0 || nextQuota.Windows.Count == 0)
        {
            return;
        }

        foreach (var nextWindow in nextQuota.Windows)
        {
            var existingWindow = existingQuota.Windows.FirstOrDefault(window =>
                string.Equals(window.Id, nextWindow.Id, StringComparison.OrdinalIgnoreCase)
                && window.ResetAtUtc == nextWindow.ResetAtUtc
                && window.LimitWindowSeconds == nextWindow.LimitWindowSeconds);
            if (existingWindow is not null)
            {
                nextWindow.LastResetHandledAt = existingWindow.LastResetHandledAt;
            }
        }
    }

    /// <summary>
    /// 克隆额度快照用于接口输出，并实时重算重置时间标签。
    /// </summary>
    private static CodexQuotaSnapshot CloneQuotaForOutput(CodexQuotaSnapshot quota)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = quota.AccountName,
            DisplayAccount = quota.DisplayAccount,
            PlanType = quota.PlanType,
            Disabled = quota.Disabled,
            CheckedAt = quota.CheckedAt,
            RefreshedAt = quota.RefreshedAt,
            StatusCode = quota.StatusCode,
            Success = quota.Success,
            ErrorMessage = quota.ErrorMessage,
            FromCache = quota.FromCache,
            CacheReason = quota.CacheReason,
            LastUsageAt = quota.LastUsageAt,
            Windows = quota.Windows.Select(window => new CodexQuotaWindowSnapshot
            {
                Id = window.Id,
                Label = window.Label,
                UsedPercent = window.UsedPercent,
                ResetLabel = CodexQuotaParser.FormatResetLabel(window.ResetAtUtc),
                LimitWindowSeconds = window.LimitWindowSeconds,
                ResetAtUtc = window.ResetAtUtc,
            }).ToList(),
        };
    }

    /// <summary>
    /// 克隆额度快照用于持久化，清除运行时字段（FromCache、CacheReason）。
    /// </summary>
    private static CodexQuotaSnapshot CloneQuotaForPersistence(CodexQuotaSnapshot quota)
    {
        return new CodexQuotaSnapshot
        {
            AccountName = quota.AccountName,
            DisplayAccount = quota.DisplayAccount,
            PlanType = quota.PlanType,
            Disabled = quota.Disabled,
            CheckedAt = quota.CheckedAt,
            RefreshedAt = quota.RefreshedAt,
            StatusCode = quota.StatusCode,
            Success = quota.Success,
            ErrorMessage = quota.ErrorMessage,
            FromCache = false,
            CacheReason = "",
            LastUsageAt = quota.LastUsageAt,
            Windows = quota.Windows.Select(window => new CodexQuotaWindowSnapshot
            {
                Id = window.Id,
                Label = window.Label,
                UsedPercent = window.UsedPercent,
                ResetLabel = window.ResetLabel,
                LimitWindowSeconds = window.LimitWindowSeconds,
                ResetAtUtc = window.ResetAtUtc,
            }).ToList(),
        };
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
    /// 将前端提交的配置值应用到站点配置对象，空值不覆盖已有配置。
    /// </summary>
    private void ApplyPayload(PatrolSiteSettings settings, SaveSettingsRequest payload, bool isNewSite)
    {
        if (!string.IsNullOrWhiteSpace(payload.SiteName))
        {
            settings.Name = payload.SiteName.Trim();
        }
        else if (isNewSite && string.IsNullOrWhiteSpace(settings.Name))
        {
            settings.Name = settings.SiteId;
        }

        settings.Enabled = payload.SiteEnabled;

        if (!string.IsNullOrWhiteSpace(payload.CpaBaseUrl))
        {
            settings.CpaBaseUrl = payload.CpaBaseUrl.Trim();
        }

        // ManagementKey 为空时不覆盖，避免清空已有密钥。
        if (!string.IsNullOrWhiteSpace(payload.ManagementKey))
        {
            settings.ManagementKey = payload.ManagementKey.Trim();
        }

        if (payload.PollIntervalMinutes >= 5)
        {
            settings.PollIntervalMinutes = payload.PollIntervalMinutes;
        }

        if (payload.PollRandomDelayMinMinutes.HasValue && payload.PollRandomDelayMinMinutes.Value >= 0)
        {
            settings.PollRandomDelayMinMinutes = payload.PollRandomDelayMinMinutes.Value;
        }

        if (payload.PollRandomDelayMaxMinutes.HasValue && payload.PollRandomDelayMaxMinutes.Value >= 0)
        {
            settings.PollRandomDelayMaxMinutes = Math.Max(settings.PollRandomDelayMinMinutes, payload.PollRandomDelayMaxMinutes.Value);
        }

        if (payload.ProbeWorkers > 0)
        {
            settings.ProbeWorkers = payload.ProbeWorkers;
        }

        if (payload.ProbeBatchDelayMinMs >= 0)
        {
            settings.ProbeBatchDelayMinMs = payload.ProbeBatchDelayMinMs;
        }

        if (payload.ProbeBatchDelayMaxMs >= payload.ProbeBatchDelayMinMs)
        {
            settings.ProbeBatchDelayMaxMs = payload.ProbeBatchDelayMaxMs;
        }
        else if (payload.ProbeBatchDelayMaxMs >= 0)
        {
            settings.ProbeBatchDelayMaxMs = Math.Max(settings.ProbeBatchDelayMinMs, payload.ProbeBatchDelayMaxMs);
        }

        if (payload.ActionWorkers > 0)
        {
            settings.ActionWorkers = payload.ActionWorkers;
        }

        if (payload.TimeoutMs > 0)
        {
            settings.TimeoutMs = payload.TimeoutMs;
        }

        if (payload.RetryCount >= 0)
        {
            settings.RetryCount = payload.RetryCount;
        }

        if (!string.IsNullOrWhiteSpace(payload.AutoActionMode))
        {
            settings.AutoActionMode = payload.AutoActionMode.Trim();
        }

        settings.AutoEnableRecovered = payload.AutoEnableRecovered;

        if (payload.UsedPercentThreshold > 0)
        {
            settings.UsedPercentThreshold = payload.UsedPercentThreshold;
        }

        if (!string.IsNullOrWhiteSpace(payload.Provider))
        {
            settings.Provider = payload.Provider.Trim();
        }

        settings.PriorityRoutingEnabled = payload.PriorityRoutingEnabled;

        if (payload.PriorityMinActiveCount > 0)
        {
            settings.PriorityMinActiveCount = payload.PriorityMinActiveCount;
        }

        settings.DisableCacheRefresh = payload.DisableCacheRefresh;
    }

    /// <summary>
    /// 构建唯一的站点 ID，重复时自动追加数字后缀。
    /// </summary>
    private string BuildUniqueSiteId(string requestedSiteId, string requestedSiteName)
    {
        var seed = !string.IsNullOrWhiteSpace(requestedSiteId) ? requestedSiteId : requestedSiteName;
        var normalized = NormalizeSiteId(seed);
        var unique = normalized;
        var suffix = 2;
        while (_siteStates.ContainsKey(unique))
        {
            unique = $"{normalized}-{suffix}";
            suffix++;
        }
        return unique;
    }

    /// <summary>
    /// 将字符串规范化为站点 ID：小写、非字母数字替换为连字符、合并连续连字符。
    /// 空值时生成随机 ID。
    /// </summary>
    private static string NormalizeSiteId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"site-{Guid.NewGuid():N}";
        }

        var trimmed = value.Trim().ToLowerInvariant();
        var chars = trimmed
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? $"site-{Guid.NewGuid():N}"
            : normalized;
    }

    /// <summary>
    /// 规范化巡检配置：补全空字段、兼容旧版单站点格式。
    /// </summary>
    private static PatrolConfig NormalizePatrolConfig(PatrolConfig config)
    {
        config.Sites ??= [];
        config.Exceptions ??= [];

        var hasLegacySingleSite = config.Sites.Count == 0
            && (config.Exceptions.Count > 0 || config.Settings is not null);
        if (hasLegacySingleSite)
        {
            config.Sites =
            [
                new PatrolSiteConfig
                {
                    SiteId = "default",
                    Exceptions = config.Exceptions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Settings = config.Settings ?? new PersistedPatrolSettings(),
                },
            ];
        }

        config.Settings ??= new PersistedPatrolSettings();

        foreach (var site in config.Sites)
        {
            site.SiteId = string.IsNullOrWhiteSpace(site.SiteId) ? "default" : site.SiteId.Trim();
            site.Exceptions = (site.Exceptions ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            site.Settings ??= new PersistedPatrolSettings();
        }

        return config;
    }

    /// <summary>
    /// 规范化连接配置：补全空字段、兼容旧版单站点连接格式。
    /// </summary>
    private static MultiSiteConnectionConfig NormalizeConnectionConfig(MultiSiteConnectionConfig config)
    {
        config.Sites ??= [];
        if (config.Sites.Count == 0 && !string.IsNullOrWhiteSpace(config.CpaBaseUrl))
        {
            config.Sites =
            [
                new CpaConnectionSite
                {
                    SiteId = "default",
                    Name = "默认站点",
                    Enabled = true,
                    CpaBaseUrl = config.CpaBaseUrl,
                    ManagementKey = config.ManagementKey,
                    Provider = "codex",
                },
            ];
        }

        foreach (var site in config.Sites)
        {
            site.SiteId = string.IsNullOrWhiteSpace(site.SiteId) ? "default" : site.SiteId.Trim();
            site.Name = string.IsNullOrWhiteSpace(site.Name) ? site.SiteId : site.Name.Trim();
            site.Provider = string.IsNullOrWhiteSpace(site.Provider) ? "codex" : site.Provider.Trim();
        }

        return config;
    }

    /// <summary>
    /// 确保必要配置文件存在，缺失时用当前默认值生成。
    /// </summary>
    private void EnsureConfigFilesExist()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                SavePatrolConfig();
            }

            if (!File.Exists(_quotaCacheFilePath))
            {
                SaveQuotaState();
            }
        }
        catch
        {
            // 生成失败不阻塞启动。
        }
    }

    /// <summary>
    /// 规范化站点配置，确保所有数值字段在合法范围内。
    /// </summary>
    private static void NormalizeSiteSettings(PatrolSiteSettings settings)
    {
        settings.SiteId = string.IsNullOrWhiteSpace(settings.SiteId) ? "default" : settings.SiteId.Trim();
        settings.Name = string.IsNullOrWhiteSpace(settings.Name) ? settings.SiteId : settings.Name.Trim();
        settings.Provider = string.IsNullOrWhiteSpace(settings.Provider) ? "codex" : settings.Provider.Trim();
        settings.PollIntervalMinutes = Math.Max(5, settings.PollIntervalMinutes);
        settings.PollRandomDelayMinMinutes = Math.Max(0, settings.PollRandomDelayMinMinutes);
        settings.PollRandomDelayMaxMinutes = Math.Max(settings.PollRandomDelayMinMinutes, settings.PollRandomDelayMaxMinutes);
        settings.ProbeWorkers = Math.Max(1, settings.ProbeWorkers);
        settings.ProbeBatchDelayMinMs = Math.Max(0, settings.ProbeBatchDelayMinMs);
        settings.ProbeBatchDelayMaxMs = Math.Max(settings.ProbeBatchDelayMinMs, settings.ProbeBatchDelayMaxMs);
        settings.ActionWorkers = Math.Max(1, settings.ActionWorkers);
        settings.TimeoutMs = Math.Max(1000, settings.TimeoutMs);
        settings.RetryCount = Math.Max(0, settings.RetryCount);
        settings.UsedPercentThreshold = Math.Clamp(settings.UsedPercentThreshold, 1, 100);
        settings.PriorityMinActiveCount = Math.Clamp(settings.PriorityMinActiveCount, 1, 10);
    }

    /// <summary>
    /// 克隆进度状态，提供线程安全的快照。
    /// </summary>
    private static RuntimeProgressState CloneProgress(RuntimeProgressState progress)
    {
        return new RuntimeProgressState
        {
            OperationType = progress.OperationType,
            Source = progress.Source,
            Status = progress.Status,
            Stage = progress.Stage,
            Message = progress.Message,
            Total = progress.Total,
            Processed = progress.Processed,
            ActionTotal = progress.ActionTotal,
            ActionProcessed = progress.ActionProcessed,
            Percent = progress.Percent,
            StartedAt = progress.StartedAt,
            FinishedAt = progress.FinishedAt,
            UpdatedAt = progress.UpdatedAt,
            CurrentAccountName = progress.CurrentAccountName,
            CurrentDisplayAccount = progress.CurrentDisplayAccount,
        };
    }

    /// <summary>
    /// 根据进度状态计算完成百分比，同时考虑探测和动作两个阶段的进度。
    /// </summary>
    private static double CalculatePercent(RuntimeProgressState progress)
    {
        if (progress.Status == "completed")
        {
            return 100;
        }

        var probeTotal = Math.Max(progress.Total, 0);
        var actionTotal = Math.Max(progress.ActionTotal, 0);

        if (actionTotal > 0)
        {
            var totalSteps = probeTotal + actionTotal;
            if (totalSteps <= 0)
            {
                return 0;
            }

            var completedSteps = Math.Clamp(progress.Processed, 0, probeTotal)
                + Math.Clamp(progress.ActionProcessed, 0, actionTotal);
            return Math.Round(completedSteps * 100d / totalSteps, 1);
        }

        if (probeTotal <= 0)
        {
            return 0;
        }

        return Math.Round(Math.Clamp(progress.Processed, 0, probeTotal) * 100d / probeTotal, 1);
    }
}
