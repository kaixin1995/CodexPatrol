# 账号优先级路由功能 — 设计文档

> 本文档描述"账号优先级路由"功能的完整改造点，实现前请确保理解每一条改动及约束。
> 核心原则：**优先级路由不开启时，所有现有功能必须完全不受影响。**

---

## 目录

- [需求背景](#需求背景)
- [功能概述](#功能概述)
- [当前实现补充（与额度巡检联动）](#当前实现补充与额度巡检联动)
- [开关与组合](#开关与组合)
- [数据模型改造](#数据模型改造)
- [持久化改造](#持久化改造)
- [后端逻辑改造](#后端逻辑改造)
  - [RuntimeStore](#runtimestore)
  - [InspectionEngine](#inspectionengine)
  - [AutoPollingService](#autopollingservice)
  - [WarmupStartupQuotasAsync](#warmupstartupquotasasync)
- [API 改造](#api-改造)
- [前端改造](#前端改造)
- [操作日志增强](#操作日志增强)
- [测试要点](#测试要点)
- [兼容性与迁移](#兼容性与迁移)
- [依次关闭无影响验证清单](#依次关闭无影响验证清单)

---

## 需求背景

当前 CodexPatrol 的自动巡检功能只关注"额度安全"：
- 额度达到阈值 → 自动禁用
- 额度恢复 → 自动启用

实际使用场景中，同一站点下有免费号和不同等级的收费号，用户需要按优先级顺序消费：
1. 先用指定的免费号
2. 免费号耗尽后，再用指定的收费号
3. 收费号之间也有等级先后

如果直接在现有系统上靠手工启用/禁用来保证顺序，会和自动巡检的"恢复即启用"逻辑互相抢控制权，导致顺序被打乱。

---

## 功能概述

新增**账号优先级路由**功能（下称"优先级路由"），核心语义：

| 概念 | 说明 |
|---|---|
| 优先级 | 每个账号可设置一个数值，越小越优先（1 最先，2 其次，以此类推） |
| 禁用原因 | 每个被禁用的账号需要区分"为什么禁用"，避免巡检误操作 |
| 恢复条件 | 优先级路由开启时，账号恢复启用 = 额度已恢复 **且** 优先级轮到了 |
| 最少保持启用数 | 优先级路由开启时，**至少保持 2 个优先级账号处于启用状态**，防止当前账号额度在两次巡检之间耗尽后 CPA 请求失败 |

**优先级路由与巡检的职责划分：**

| 职责 | 负责模块 | 说明 |
|---|---|---|
| 探测额度状态 | 巡检 | 仍然负责真实请求、解析额度、判断是否超阈值 |
| 超阈值时禁用 | 巡检 | 不变 |
| 恢复时是否启用 | 优先级路由（开启时） | 不再无条件启用，需满足"额度恢复 + 优先级到了" |
| 当前应启用哪些账号 | 优先级路由（开启时） | 按优先级从高到低，在手动巡检或自动巡检完成后决定应启用账号，并跳过例外账号 |

---

## 当前实现补充（与额度巡检联动）

| 主题 | 当前实现 |
|---|---|
| 收费号额度规则 | 周额度或 5 小时额度任一达到阈值即禁用；恢复后再按优先级是否轮到决定启用 |
| 免费号额度规则 | 仅按周额度处理，5 小时额度不作为禁用依据 |
| 例外账号 | 不参与巡检候选、不参与优先级路由调度、不参与 10 小时保鲜真实刷新 |
| 时间字段 | `CheckedAt` = 最近检查时间；`RefreshedAt` = 最近真实刷新时间 |
| 真实刷新保鲜 | 非例外账号最长 10 小时一次真实请求，并分散到 8 小时 ~ 9 小时 50 分窗口 |
| 保存后同步 CPA | 保存优先级配置后会立即增量同步 CPA `priority`，只改顺序不改 `disabled`；失败只告警，不回滚本地配置 |

---

## 开关与组合

### 两个独立开关

| 开关 | 字段名 | 控制范围 |
|---|---|---|
| 自动巡检 | `AutoPollingEnabled`（已有） | 是否自动按间隔巡检、是否自动执行禁用/删除动作 |
| 优先级路由 | `PriorityRoutingEnabled`（**新增**） | 是否按优先级顺序调度账号启用/禁用 |

### 四种组合行为

| 优先级路由 | 自动巡检 | 行为 |
|---|---|---|
| ✗ | ✗ | 纯手动：用户自己管控一切 |
| ✗ | ✓ | **现有行为，完全不变**：超阈值自动禁用，恢复自动启用，无顺序控制 |
| ✓ | ✗ | 可配置优先级，但没有后台自动调度；需手动触发一次巡检后才会执行优先级调度 |
| ✓ | ✓ | **完整模式**：自动探测额度 + 按优先级调度，恢复需额度和优先级同时满足 |

### 关键约束

- `PriorityRoutingEnabled = false` 时，所有优先级路由相关逻辑**完全不执行**，等于代码不存在。
- 没有 `accountPriorities` 数据时，即使开关开启，所有账号优先级视为 `0`（同等优先级），行为退化为"先到先得"。
- `autoEnableRecovered` 在优先级路由开启时**行为变化**：不再直接启用所有恢复的账号，而是由优先级逻辑决定。
- 例外账号不参与优先级路由，也不参与额度保鲜真实刷新。
- `PriorityRoutingEnabled = true` 且 `AutoPollingEnabled = false` 时，不会有后台自动调度；需手动触发巡检后才会执行优先级调度。
- **容错约束**：优先级路由开启时，始终保持至少 `minActiveCount`（默认 2）个优先级账号启用。只有可用账号不足 2 个时才允许只启用 1 个；如果连 1 个都没有则标记全部耗尽。这样做是为了防止主账号在两次巡检之间额度突然耗尽，CPA 没有可用账号导致请求失败。

---

## 数据模型改造

### 1. `PatrolSiteSettings` — 新增字段

```csharp
// src/CodexPatrol/Models/PatrolSiteSettings.cs

/// <summary>
/// 是否启用优先级路由。关闭时所有账号同等对待，启用时按优先级顺序调度。
/// </summary>
[JsonPropertyName("priorityRoutingEnabled")]
public bool PriorityRoutingEnabled { get; set; }

/// <summary>
/// 优先级路由最少保持启用的账号数量，默认 2。
/// 防止当前账号在两次巡检之间额度耗尽导致 CPA 无可用账号。
/// </summary>
[JsonPropertyName("priorityMinActiveCount")]
public int PriorityMinActiveCount { get; set; } = 2;
```

### 2. `PatrolSiteConfig` — 新增优先级列表

```csharp
// src/CodexPatrol/Models/PatrolSiteSettings.cs → PatrolSiteConfig 类

/// <summary>
/// 该站点的账号优先级配置，数值越小越优先。
/// </summary>
[JsonPropertyName("accountPriorities")]
public List<AccountPriority> AccountPriorities { get; set; } = [];
```

### 3. 新增 `AccountPriority` 模型

```csharp
// src/CodexPatrol/Models/PatrolSiteSettings.cs（可放在同一文件）

/// <summary>
/// 单个账号的优先级配置。
/// </summary>
public sealed class AccountPriority
{
    /// <summary>
    /// 账号名称，与 AuthFileItem.Name 对应。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 优先级数值，越小越优先。0 表示未配置同等优先。
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}
```

### 4. 新增 `DisableReason` 枚举

```csharp
// src/CodexPatrol/Models/InspectionDecision.cs（可放在同一文件）

/// <summary>
/// 账号禁用原因。
/// </summary>
public enum DisableReason
{
    /// <summary>
    /// 未禁用。
    /// </summary>
    None,

    /// <summary>
    /// 巡检策略禁用：额度达到阈值。
    /// </summary>
    QuotaExhausted,

    /// <summary>
    /// 顺序策略待命：为了优先级排队，暂不启用。
    /// </summary>
    OrderedStandby,

    /// <summary>
    /// 手动禁用：用户在前端手动操作。
    /// </summary>
    ManualDisabled,

    /// <summary>
    /// 异常禁用：探测失败等异常情况。
    /// </summary>
    ErrorDisabled,
}
```

### 5. `SiteRuntimeState` — 新增禁用原因存储

```csharp
// src/CodexPatrol/Services/SiteRuntimeState.cs

/// <summary>
/// 账号禁用原因映射（账号名 → 禁用原因），仅运行时状态，不持久化。
/// </summary>
public ConcurrentDictionary<string, DisableReason> DisableReasons { get; } = new();
```

> 为什么不持久化 `DisableReason`？
> - `QuotaExhausted`：可从额度快照推断（周额度超阈值）
> - `OrderedStandby`：可从优先级配置 + 当前激活状态推断
> - `ManualDisabled`：启动时从 CPA 同步的 `disabled=true` 即可知，标记为手动
> - `ErrorDisabled`：可从探测异常状态推断
>
> 如果后续需要重启后保持精确原因，再考虑持久化。初期仅内存维护即可。

### 6. `InspectionDecision` — 新增禁用原因字段

```csharp
// src/CodexPatrol/Models/InspectionDecision.cs

/// <summary>
/// 决策对应的禁用原因（仅当 Action 为 Disable 或 Enable 时有意义）。
/// </summary>
[JsonPropertyName("disableReason")]
public DisableReason DisableReason { get; set; } = DisableReason.None;
```

### 7. `PersistedPatrolSettings` — 新增字段

```csharp
// src/CodexPatrol/Models/PatrolConfig.cs → PersistedPatrolSettings 类

/// <summary>
/// 是否启用优先级路由。
/// </summary>
[JsonPropertyName("priorityRoutingEnabled")]
public bool PriorityRoutingEnabled { get; set; }

/// <summary>
/// 优先级路由最少保持启用的账号数量。
/// </summary>
[JsonPropertyName("priorityMinActiveCount")]
public int PriorityMinActiveCount { get; set; } = 2;
```

`FromRuntime` / `ApplyTo` 方法需同步映射这两个字段。

### 8. DTO 改造

#### `SettingsResponse` 新增字段

```csharp
// src/CodexPatrol/Models/ApiDtos.cs → SettingsResponse

/// <summary>
/// 是否启用优先级路由。
/// </summary>
[JsonPropertyName("priorityRoutingEnabled")]
public bool PriorityRoutingEnabled { get; set; }

/// <summary>
/// 优先级路由最少保持启用的账号数量。
/// </summary>
[JsonPropertyName("priorityMinActiveCount")]
public int PriorityMinActiveCount { get; set; }
```

#### `SaveSettingsRequest` 新增字段

```csharp
// src/CodexPatrol/Models/ApiDtos.cs → SaveSettingsRequest

/// <summary>
/// 是否启用优先级路由。
/// </summary>
[JsonPropertyName("priorityRoutingEnabled")]
public bool PriorityRoutingEnabled { get; set; }

/// <summary>
/// 优先级路由最少保持启用的账号数量。
/// </summary>
[JsonPropertyName("priorityMinActiveCount")]
public int PriorityMinActiveCount { get; set; }
```

#### 新增 `AccountPriorityResponse`

```csharp
// src/CodexPatrol/Models/ApiDtos.cs

/// <summary>
/// 账号优先级条目。
/// </summary>
public sealed class AccountPriorityResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

/// <summary>
/// 优先级路由状态响应。
/// </summary>
public sealed class PriorityRoutingStatusResponse
{
    /// <summary>
    /// 是否启用优先级路由。
    /// </summary>
    [JsonPropertyName("priorityRoutingEnabled")]
    public bool PriorityRoutingEnabled { get; set; }

    /// <summary>
    /// 当前激活的账号名（优先级路由开启时有效）。
    /// </summary>
    [JsonPropertyName("activeAccountName")]
    public string ActiveAccountName { get; set; } = "";

    /// <summary>
    /// 账号优先级列表。
    /// </summary>
    [JsonPropertyName("accountPriorities")]
    public List<AccountPriorityResponse> AccountPriorities { get; set; } = [];

    /// <summary>
    /// 最少保持启用的账号数量。
    /// </summary>
    [JsonPropertyName("priorityMinActiveCount")]
    public int PriorityMinActiveCount { get; set; }
}
```

---

## 持久化改造

### `patrol-config.json`

当前格式：
```json
{
  "sites": [
    {
      "siteId": "default",
      "exceptions": ["account-x"],
      "settings": { "autoPollingEnabled": true, ... }
    }
  ]
}
```

改造后：
```json
{
  "sites": [
    {
      "siteId": "default",
      "exceptions": ["account-x"],
      "accountPriorities": [
        { "name": "free-account-1", "priority": 1 },
        { "name": "free-account-2", "priority": 2 },
        { "name": "paid-account-1", "priority": 3 }
      ],
      "settings": { "priorityRoutingEnabled": false, "autoPollingEnabled": true, ... }
    }
  ]
}
```

**兼容性**：
- `accountPriorities` 缺失时默认为空数组，等同于所有账号优先级为 0
- `priorityRoutingEnabled` 缺失时默认为 `false`
- `priorityMinActiveCount` 缺失时默认为 `2`
- 旧配置文件加载不会出错

### `SavePatrolConfig` 方法改动

`RuntimeStore.SavePatrolConfig()` 序列化 `PatrolSiteConfig` 时需新增 `AccountPriorities` 字段，与 `Exceptions` 类似，从 `SiteRuntimeState` 读取。

---

## 后端逻辑改造

### RuntimeStore

#### 新增方法

| 方法 | 说明 |
|---|---|
| `List<AccountPriority> GetAccountPriorities(string? siteId)` | 获取优先级配置列表 |
| `void SetAccountPriorities(List<AccountPriority> priorities, string? siteId)` | 设置优先级配置并持久化 |
| `DisableReason GetDisableReason(string accountName, string? siteId)` | 获取账号禁用原因 |
| `void SetDisableReason(string accountName, DisableReason reason, string? siteId)` | 设置账号禁用原因 |
| `void ClearDisableReason(string accountName, string? siteId)` | 清除账号禁用原因 |
| `string? GetActivePriorityAccount(string? siteId)` | 获取当前应激活的优先级最高且额度可用的账号名 |

#### `UpdateAccountDisabledState` 改动

当前签名：
```csharp
public bool UpdateAccountDisabledState(string accountName, bool disabled, string? siteId = null)
```

新增重载：
```csharp
public bool UpdateAccountDisabledState(string accountName, bool disabled, DisableReason reason, string? siteId = null)
```

- `disabled = true` 时自动设置 `DisableReason`
- `disabled = false` 时自动清除 `DisableReason`
- 保留原方法签名（无 `reason` 参数），内部默认设为 `DisableReason.ManualDisabled` 或 `DisableReason.None`

#### 优先级激活逻辑 — `ResolveActivePriorityAccounts`

> 注意：返回的是**列表**而非单个账号，因为需要至少保持 `minActiveCount` 个可用账号。

伪代码：
```
1. 如果 PriorityRoutingEnabled = false，返回空列表（不走优先级逻辑）
2. 获取优先级配置，按 priority 升序排列
3. 获取 minActiveCount = settings.PriorityMinActiveCount（至少 1，默认 2）
4. 遍历有优先级配置的账号（按优先级升序）：
   a. 查找其额度快照
   b. 如果额度快照存在且周额度未超阈值 → 加入激活列表
   c. 当激活列表数量达到 minActiveCount → 停止
5. 返回激活列表（如果整个列表为空说明全部耗尽）
6. 没有优先级配置的账号不参与优先级激活，不受影响
```

示例（minActiveCount = 2）：
```
P1 = free-1  (周额度 60%)  → 激活
P2 = free-2  (周额度 30%)  → 激活（达到 2 个，停止）
P3 = pro-1   (周额度 10%)  → 不激活，保持待命
P4 = pro-2   (周额度 80%)  → 不激活，保持待命

结果：free-1 和 free-2 启用，pro-1 和 pro-2 禁用标记 OrderedStandby
```

耗尽场景（minActiveCount = 2）：
```
P1 = free-1  (周额度 100%) → 跳过
P2 = free-2  (周额度 98%)  → 跳过
P3 = pro-1   (周额度 20%)  → 激活
P4 = pro-2   (周额度 15%)  → 激活（达到 2 个，停止）

结果：free-1 和 free-2 禁用标记 QuotaExhausted，pro-1 和 pro-2 启用
```

#### 初始化账号时标记禁用原因

`SetAccounts` 方法中，当从 CPA 同步账号列表时，需要根据账号的 `Disabled` 状态初始化 `DisableReasons`：
- 如果 `disabled = true` 且无已知禁用原因 → 标记为 `ManualDisabled`
- 如果 `disabled = false` → 确保清除原因

### InspectionEngine

#### `ResolveDecision` 改动

**核心改动：优先级路由开启时，Enable 决策不再无条件给出。**

当前逻辑（`InspectionEngine.cs:577-663`）：

```
如果 weeklyPercent 未超阈值 且 file.Disabled → 建议启用
```

改造后：

```csharp
private InspectionDecision ResolveDecision(
    AuthFileItem file,
    int statusCode,
    CodexQuotaSnapshot quota,
    int threshold,
    bool priorityRoutingEnabled,    // 新增参数
    int? accountPriority)           // 新增参数：该账号的优先级，null 表示未配置
{
    // ... 401 处理不变 ...

    if (weeklyPercent.HasValue)
    {
        if (weeklyOverThreshold)
        {
            if (file.Disabled)
            {
                // 已禁用且超阈值 → 保持禁用
                return BuildDecision(file, InspectionAction.Keep,
                    "周额度达到阈值，但账号已禁用", statusCode, weeklyPercent, true)
                    { DisableReason = DisableReason.QuotaExhausted };
            }
            // 未禁用且超阈值 → 禁用
            return BuildDecision(file, InspectionAction.Disable,
                "周额度达到阈值，建议禁用账号", statusCode, weeklyPercent, true)
                    { DisableReason = DisableReason.QuotaExhausted };
        }

        // 【关键改动点】金额度恢复 且 账号已禁用
        if (file.Disabled)
        {
            if (!priorityRoutingEnabled)
            {
                // 优先级路由关闭 → 现有行为，直接建议启用
                return BuildDecision(file, InspectionAction.Enable,
                    "周额度仍可用，建议立即启用账号", statusCode, weeklyPercent, false)
                    { DisableReason = DisableReason.None };
            }

            // 优先级路由开启 → 检查优先级是否轮到
            // 这个账号是否能成为当前激活账号，由 AutoPollingService 的
            // 优先级调度逻辑统一判断，这里只标记"可恢复待命"
            return BuildDecision(file, InspectionAction.Keep,
                "周额度可用，但优先级路由开启，等待优先级调度", statusCode, weeklyPercent, false)
                { DisableReason = DisableReason.OrderedStandby };
        }

        // ... 5小时超阈值、正常等分支不变 ...
    }

    // ... 后续 fallback 逻辑不变 ...
}
```

#### `InspectAccountsAsync` 签名改动

需要把 `priorityRoutingEnabled` 和优先级数据传入，以便传给 `ResolveDecision`。

当前签名：
```csharp
public async Task<List<InspectionDecision>> InspectAccountsAsync(
    string? siteId, List<AuthFileItem> files, ...)
```

不需要改动 `InspectAccountsAsync` 方法签名，因为 `ResolveDecision` 是私有方法，可以直接在方法内部通过 `_store.GetSettings(siteId).PriorityRoutingEnabled` 和 `_store.GetAccountPriorities(siteId)` 获取配置。

#### `FilterAutoActionItems` 改动

当前签名：
```csharp
public static List<InspectionDecision> FilterAutoActionItems(
    AutoActionMode mode, bool autoEnable, List<InspectionDecision> decisions)
```

优先级路由开启时，`Enable` 类型的决策**不在此处处理**，而是由优先级调度逻辑统一接管。

改造后逻辑：
```csharp
public static List<InspectionDecision> FilterAutoActionItems(
    AutoActionMode mode, bool autoEnable, bool priorityRoutingEnabled,
    List<InspectionDecision> decisions)
{
    // Disable/Delete 处理逻辑不变 ...

    // Enable 处理
    if (autoEnable && !priorityRoutingEnabled)
    {
        // 优先级路由关闭 → 现有行为，自动启用所有恢复的账号
        result.AddRange(decisions.Where(d => d.Action == InspectionAction.Enable));
    }
    // 优先级路由开启时，Enable 决策由优先级调度逻辑处理，这里不自动添加
}
```

### AutoPollingService

#### 新增：优先级调度逻辑

在每轮巡检完成后，如果 `PriorityRoutingEnabled = true`，执行优先级调度：

```csharp
private async Task ApplyPriorityRoutingAsync(
    string siteId, PatrolSiteSettings settings,
    List<InspectionDecision> decisions, CancellationToken ct)
{
    if (!settings.PriorityRoutingEnabled) return;

    var priorities = _store.GetAccountPriorities(siteId);
    if (priorities.Count == 0) return;

    var minActiveCount = Math.Max(1, settings.PriorityMinActiveCount);

    // 按优先级排序（数值越小越优先）
    var sortedByPriority = priorities
        .OrderBy(p => p.Priority)
        .ToList();

    // 找到当前应激活的账号列表（至少 minActiveCount 个）
    var activeAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var priority in sortedByPriority)
    {
        var account = _store.GetAccount(priority.Name, siteId);
        if (account == null) continue;
        // 跳过异常列表中的账号
        if (_store.GetExceptions(siteId).Contains(priority.Name)) continue;

        var quota = _store.GetQuota(priority.Name, siteId);
        var weeklyPercent = quota != null ? CodexQuotaParser.GetWeeklyUsedPercent(quota) : null;

        // 额度可用（未超阈值）→ 加入激活列表
        if (!weeklyPercent.HasValue || weeklyPercent.Value < settings.UsedPercentThreshold)
        {
            activeAccountNames.Add(priority.Name);
            if (activeAccountNames.Count >= minActiveCount) break;
        }
    }

    // 全部耗尽日志
    if (activeAccountNames.Count == 0)
    {
        _store.AddOperationLog("account", "priorityRouting", "auto",
            "优先级路由：所有配置优先级的账号额度已耗尽", "warning",
            siteId: siteId);
    }

    // 确保只有激活列表中的账号处于启用状态
    var accounts = _store.GetAccounts(siteId);
    foreach (var account in accounts)
    {
        var priorityEntry = priorities.FirstOrDefault(
            p => string.Equals(p.Name, account.Name, StringComparison.OrdinalIgnoreCase));
        // 没有配置优先级的账号不受优先级路由影响
        if (priorityEntry == null) continue;

        if (activeAccountNames.Contains(account.Name))
        {
            // 应激活的账号 → 如果被优先级路由禁用了，现在恢复
            if (account.Disabled &&
                _store.GetDisableReason(account.Name, siteId) == DisableReason.OrderedStandby)
            {
                await _cpa.EnableAccountAsync(settings, account.Name, ct);
                _store.UpdateAccountDisabledState(account.Name, false, siteId);
                _store.ClearDisableReason(account.Name, siteId);
                _store.AddOperationLog("account", "priorityRouting", "auto",
                    $"优先级路由恢复启用：{account.Name}（优先级 {priorityEntry.Priority}）",
                    siteId: siteId);
            }
        }
        else
        {
            // 非当前激活账号 → 如果正在使用但优先级不够，禁用并标记为 OrderedStandby
            if (!account.Disabled &&
                _store.GetDisableReason(account.Name, siteId) == DisableReason.None)
            {
                await _cpa.DisableAccountAsync(settings, account.Name, ct);
                _store.UpdateAccountDisabledState(account.Name, true, siteId);
                _store.SetDisableReason(account.Name, DisableReason.OrderedStandby, siteId);
                _store.AddOperationLog("account", "priorityRouting", "auto",
                    $"优先级路由待命禁用：{account.Name}（优先级 {priorityEntry.Priority}）",
                    siteId: siteId);
            }
        }
    }
}
```

#### `RunInspectionAsync` 改动

在现有巡检动作执行完成后（`ExecuteActionsAsync` 之后、刷新账号列表之前），新增优先级调度调用：

```csharp
// 现有代码：执行自动动作 ...
outcomes = await _engine.ExecuteActionsAsync(...);

// 新增：优先级路由调度
if (settings.PriorityRoutingEnabled)
{
    await ApplyPriorityRoutingAsync(siteId, settings, decisions, ct);
}

// 现有代码：刷新账号列表 ...
var files = await _engine.LoadCandidatesAsync(siteId, includeExceptions: true, ct);
_store.SetAccounts(files, siteId);
```

#### `TryHandleReachedQuotaResetAsync` 改动

当前行为：额度恢复后如果 `AutoEnableRecovered = true`，直接启用恢复账号。

改造后：
- 优先级路由**关闭** → 保持现有行为
- 优先级路由**开启** → 不直接启用，而是交给 `ApplyPriorityRoutingAsync` 在下一轮巡检中统一处理

```csharp
// 在 TryHandleReachedQuotaResetAsync 中
if (!settings.AutoEnableRecovered)
{
    // 现有逻辑不变 ...
    return true;
}

// 优先级路由开启时，不在此处直接启用恢复的账号
// 而是由优先级调度逻辑统一判断
if (settings.PriorityRoutingEnabled)
{
    _store.AddOperationLog("inspection", "inspection", "auto",
        "额度重置检测完成，优先级路由开启，恢复账号将由优先级调度统一处理",
        siteId: siteId);
    // 标记可恢复但不动启用
    foreach (var candidate in dueCandidates.Where(c => c.Decision.Action == InspectionAction.Enable))
    {
        _store.SetDisableReason(candidate.Account.Name, DisableReason.OrderedStandby, siteId);
    }
    // ... 继续常规调度 nextScheduledAt 和 nextResetCheckAt ...
    return true;
}

// 优先级路由关闭 → 现有行为
var mode = ResolveAutoActionMode(settings.AutoActionMode);
var actionItems = InspectionEngine.FilterAutoActionItems(mode, settings.AutoEnableRecovered, decisions);
// ... 现有执行逻辑 ...
```

### WarmupStartupQuotasAsync 改动

当前行为：启动预热只探测前 3 个启用账号。

改造后：
- 优先级路由**关闭** → 保持现有行为（取前 3 个启用账号）
- 优先级路由**开启** → 按优先级顺序取前 3 个账号（不论是否启用，因为优先级路由可能需要知道已禁用账号的额度恢复情况）

```csharp
private async Task WarmupStartupQuotasAsync(
    string siteId, PatrolSiteSettings settings,
    List<AuthFileItem> accounts, CancellationToken ct)
{
    List<AuthFileItem> warmupAccounts;
    string warmupReason;

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
        warmupReason = priorities.Count > 0
            ? $"按优先级顺序预热水印"
            : "无优先级配置，按启用账号顺序预热";
    }
    else
    {
        warmupAccounts = accounts.Where(a => !a.Disabled).Take(3).ToList();
        warmupReason = "按启用账号顺序预热";
    }

    // ... 后续探测逻辑不变，使用 warmupAccounts ...
}
```

---

## API 改造

### 1. 设置 API — 已有端点扩展

`GET /api/settings/` 和 `PUT /api/settings/` 的请求/响应体新增 `priorityRoutingEnabled` 字段。

### 2. 新增优先级路由 API

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/settings/priority-routing` | 获取优先级路由状态和配置 |
| `PUT` | `/api/settings/priority-routing` | 更新优先级路由配置（开关 + 优先级列表） |

`PUT /api/settings/priority-routing` 请求体：
```json
{
  "priorityRoutingEnabled": true,
  "priorityMinActiveCount": 2,
  "accountPriorities": [
    { "name": "free-1", "priority": 1 },
    { "name": "free-2", "priority": 2 },
    { "name": "pro-1", "priority": 3 }
  ]
}
```

响应体：同 `PriorityRoutingStatusResponse`，当前实现额外可能返回 `cpaPrioritySyncWarning`，用于提示“本地已保存但 CPA 优先级同步失败”。

当前实现中，`PUT /api/settings/priority-routing` 在本地保存成功后还会：

- 调用 `GET /v0/management/auth-files` 读取 CPA 当前账号和 `priority`
- 按账号名大小写不敏感匹配本地顺序与 CPA 账号
- 将本地“越小越优先”的顺序换算为 CPA“越大越优先”的 `N..1` 数值
- 仅对发生变化的账号调用 `PATCH /v0/management/auth-files/fields`
- PATCH 只发送 `name` + `priority`，不写 `disabled` / `status`
- 会自动跳过例外账号和 `PendingFirstInspection = true` 的待首检账号，避免它们提前影响 CPA 远端优先级
- 如遇 CPA 重名账号、账号缺失或远端请求失败，则停止远端同步并返回告警，不回滚本地配置

**校验规则**：

- `accountPriorities` 中的账号名必须存在于当前站点的账号列表中，不存在的给出警告但忽略
- `priority` 值必须 ≥ 1，0 或空等同于未配置
- `priorityMinActiveCount` 必须 ≥ 1 且 ≤ 10，超出范围时 clamp（`RuntimeStore.NormalizeSiteSettings` 中处理）
- 重复 `name` 取最后出现的条目

### 3. 现有账号 API 扩展

`GET /api/accounts/` 返回的账号列表需要附加禁用原因：

当前 `AuthFileItem` 已有 `disabled` 字段。需要在返回的额度快照 `CodexQuotaSnapshot` 中附带 `disableReason`。

`CodexQuotaSnapshot` 新增字段：
```csharp
/// <summary>
/// 禁用原因，仅优先级路由开启时有效。
/// </summary>
[JsonPropertyName("disableReason")]
public string DisableReason { get; set; } = "";
```

在 `GetQuotas` / `GetQuota` 返回快照时，从 `SiteRuntimeState.DisableReasons` 填充此字段。

---

## 前端改造

### 1. 设置页面 — 新增优先级路由开关

在"策略"选项卡中，`autoEnableRecovered` 开关下方新增：

```
☑ 启用优先级路由
最少保持启用数：[  2  ]（1-10，默认 2，防止当前账号在两次巡检之间耗尽后 CPA 请求失败）
提示：开启后账号按优先级顺序消费，至少保持指定数量的可用账号同时启用
```

交互约束：
- 开启 `priorityRoutingEnabled` 时，自动提示建议同时开启 `autoPollingEnabled`，但不强制
- `priorityRoutingEnabled = false` 时，隐藏优先级配置区域

### 2. 设置页面 — 优先级配置

开启 `priorityRoutingEnabled` 后显示：

- 账号优先级列表（可拖拽排序或手动输入数值）
- 每行：`账号名 | 显示名 | 优先级数值 | ▲▼ 操作`
- "自动排列"按钮：按当前列表顺序从 1 开始自动编号
- "清空优先级"按钮

### 3. 额度页面 — 展示优先级和禁用原因

- 额度卡片新增标签：
  - 优先级标签：`P1` `P2` `P3` ...
  - 禁用原因标签：`额度耗尽` `排队待命` `手动禁用` `异常禁用`
- 优先级路由开启时，按优先级排序显示账号（而非字母序）
- 当前激活账号高亮显示

### 4. 账号操作交互变化

优先级路由开启时：
- 手动禁用账号 → 禁用原因标记为 `ManualDisabled`
- 手动启用已禁用账号 → 提示"优先级路由模式下，手动启用的账号可能在下一轮巡检中被重新调整为待命状态"

---

## 操作日志增强

优先级路由相关操作需生成日志：

| 场景 | 日志 category | 日志 operationType | 示例消息 |
|---|---|---|---|
| 优先级路由恢复启用 | account | priorityRouting | `优先级路由恢复启用：free-1（优先级 1）` |
| 优先级路由待命禁用 | account | priorityRouting | `优先级路由待命禁用：free-2（优先级 2）` |
| 所有账号耗尽 | account | priorityRouting | `优先级路由：所有配置优先级的账号额度已耗尽` |
| 可用账号不足最少保持数 | account | priorityRouting | `优先级路由：仅剩 N 个可用账号，不足最少保持数 M` |
| 额度恢复但未轮到 | inspection | inspection | `额度恢复但优先级路由开启，等待优先级调度：pro-1（优先级 3）` |
| 优先级路由配置更新 | system | settings | `优先级路由配置已更新，共 N 个账号` |
| CPA 优先级同步成功 | system | priorityRouting | `优先级路由配置已保存，并已同步 CPA 优先级，共更新 N 个账号` |
| CPA 优先级同步告警 | system | priorityRouting | `本地配置已保存，但同步 CPA 优先级失败：...` |
| 启动预热按优先级 | quota | startupWarmup | `启动预热按优先级顺序开始，将对最多 3 个账号做真实额度检测` |

---

## 测试要点

### 必须通过的回归测试（优先级路由关闭）

| 场景 | 预期 |
|---|---|
| `PriorityRoutingEnabled = false` 时，自动巡检禁用超阈值账号 | 行为与改造前完全一致 |
| `PriorityRoutingEnabled = false` 时，恢复的账号被自动启用 | 行为与改造前完全一致 |
| `PriorityRoutingEnabled = false` 时，手动禁用/启用账号 | 行为与改造前完全一致 |
| `PriorityRoutingEnabled = false` 时，启动预热 | 只探测前 3 个启用账号，与改造前一致 |
| `PriorityRoutingEnabled = false` 时，额度重置检测 | 行为与改造前完全一致 |
| 旧配置文件（无 `priorityRoutingEnabled` / `accountPriorities` 字段）加载 | 默认值为 `false`/空列表，不报错 |

### 优先级路由专项测试

| 场景 | 预期 |
|---|---|
| 开启优先级路由，配置 P1→A1, P2→A2, P3→A3（均额度可用，minActiveCount=2） | A1 和 A2 启用，A3 被禁用标记 OrderedStandby |
| A1 额度超阈值后（minActiveCount=2） | A1 禁用标记 QuotaExhausted，A2 仍启用，A3 被启用（补足 2 个） |
| A1 和 A2 都超阈值后（minActiveCount=2） | A3 启用（仅剩 1 个可用，不足 2 个但全部启用），日志标记仅剩 1 个 |
| 3 个都超阈值后 | 全部禁用，日志记录"所有优先级账号耗尽" |
| A1 额度恢复（minActiveCount=2） | A1 重新启用，A3 被禁用回到待命（保持 A1+A2 共 2 个） |
| minActiveCount=1 时 | 只启用最高优先级的 1 个可用账号 |
| 有账号无优先级配置 | 这些账号不受优先级路由影响 |
| 待首检账号存在，但尚未完成首检 | 不参与 active 选择，也不参与 CPA 优先级同步 |
| 例外账号仍保留在优先级列表 | 不参与优先级调度，不会被自动启用/禁用，也不参与 CPA 同步 |
| 优先级路由开启 + 自动巡检关闭 | 优先级路由可以手动触发巡检后执行调度 |
| 优先级路由关闭时完成巡检 | 不自动重排本地顺序，也不自动同步 CPA 优先级 |
| 手动禁用已启用账号 | 标记为 ManualDisabled，不会被优先级路由自动恢复 |
| 账号不存在于优先级列表 | 不参与优先级调度，但不被禁用 |

### 禁用原因测试

| 场景 | 预期 DisableReason |
|---|---|
| 额度超阈值自动禁用 | QuotaExhausted |
| 优先级路由把非当前账号禁用 | OrderedStandby |
| 用户在前端手动禁用 | ManualDisabled |
| 探测异常（401 等）自动禁用 | ErrorDisabled |
| 正常启用状态 | None |

---

## 兼容性与迁移

### 配置文件向后兼容

- `priorityRoutingEnabled` 缺失 → 默认 `false`
- `accountPriorities` 缺失 → 默认 `[]`
- `priorityMinActiveCount` 缺失 → 默认 `2`
- 旧版本配置文件加载时不报错、不丢失数据

### API 向后兼容

- 所有新增字段在旧客户端不发送时使用默认值
- `GET /api/settings/` 新增 `priorityRoutingEnabled` 字段，旧客户端忽略
- `PUT /api/settings/` 新增 `priorityRoutingEnabled` 字段，旧客户端不发送不改变

### 运行时状态恢复

- `DisableReasons` 为纯内存状态，重启后从 CPA 同步账号列表时重建：
  - `disabled = true` 且无法从额度数据推断原因 → 标记为 `ManualDisabled`
  - `disabled = true` 且额度数据显示超阈值 → 标记为 `QuotaExhausted`
- 优先级路由开启时，启动预热完成后第一次巡检会自动修正禁用原因

---

## 依次关闭无影响验证清单

改造完成后，必须逐项确认以下场景**与改造前行为完全一致**：

1. **`PriorityRoutingEnabled = false`（默认值）**
   - [ ] 自动巡检超阈值禁用账号 → 行为不变
   - [ ] 自动巡检恢复后启用账号 → 行为不变
   - [ ] 启动预热只探测前 3 个启用账号 → 行为不变
   - [ ] 额度重置检测走原逻辑 → 行为不变
   - [ ] 手动禁用/启用账号 → 行为不变
   - [ ] 操作日志无优先级路由相关条目
   - [ ] 设置页面无优先级路由相关控件
   - [ ] 所有现有单元测试通过

2. **`PriorityRoutingEnabled = true` + `accountPriorities = []`**
   - [ ] 没有优先级配置 → 所有账号同等对待，不产生任何优先级调度动作
   - [ ] 现有巡检行为不受影响

3. **新功能基本验证**
   - [ ] 配置优先级后，至少保持 `priorityMinActiveCount`（默认 2）个高优先级且额度可用的账号被启用
   - [ ] 只有超过激活数的低优先级账号被禁用标记 OrderedStandby
   - [ ] 当前账号超阈值后，自动补足下一个优先级的可用账号以维持最少保持数
   - [ ] 禁用原因正确标记在额度页面和操作日志中
   - [ ] 手动禁用不会被优先级路由自动恢复
   - [ ] 全部账号耗尽时正确记录日志