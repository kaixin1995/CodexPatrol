import { api, escapeHtml, getAccounts, getDisplayAccount, getSelectedSiteId, refreshAccounts, setLoading, showToast } from './common.js';
import { renderLayout } from './layout.js';

let priorityEntries = [];
let cachedAccounts = [];
let cachedQuotas = [];
let priorityRoutingEnabled = false;
let priorityMinActiveCount = 2;
let currentSiteId = '';

function getQuotaWindow(quota, seconds) {
  return quota?.windows?.find(window => Number(window.limitWindowSeconds) === seconds) || null;
}

function formatUsedPercent(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)}%` : '--';
}

function buildPriorityQuotaText(quota) {
  if (!quota) {
    return '暂无额度';
  }

  const weeklyWindow = getQuotaWindow(quota, 604800);
  const fiveHourWindow = getQuotaWindow(quota, 18000);
  const parts = [];

  if (weeklyWindow) {
    parts.push(`已用周额度 ${formatUsedPercent(weeklyWindow.usedPercent)}`);
  }

  if (!String(quota.planType || '').trim().toLowerCase().startsWith('free') && fiveHourWindow) {
    parts.push(`已用 5 小时额度 ${formatUsedPercent(fiveHourWindow.usedPercent)}`);
  }

  if (parts.length > 0) {
    return parts.join(' · ');
  }

  return quota.errorMessage ? '额度异常' : '暂无额度';
}

function renderPage() {
  renderLayout('priority', '优先级路由', `
    <div class="page-header" style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px">
      <h1>优先级路由</h1>
      <button type="button" class="btn btn-primary" id="btn-priority-save">保存配置</button>
    </div>

    <div class="card" style="margin-bottom:16px">
      <div style="display:flex;align-items:center;gap:16px;flex-wrap:wrap">
        <label class="checkbox-label" style="margin:0">
          <input type="checkbox" id="priority-enabled">
          启用优先级路由
        </label>
        <span class="help-tip" tabindex="0" id="priority-help">?<span class="help-tip-content">开启后，账号按下方排列顺序依次消费；排在前面的账号额度耗尽后自动轮转到下一个。<br>至少保持指定数量的账号同时启用以保证可用性。</span></span>
        <label style="font-size:12px;display:inline-flex;align-items:center;gap:6px;margin:0">最少保持启用数
          <input type="number" id="priority-min-active" min="1" max="10" value="2" style="width:72px;padding:5px 8px">
        </label>
      </div>
    </div>

    <div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px">
        <label style="font-size:14px;font-weight:500">账号排列顺序 <span style="color:var(--text-muted);font-size:12px">（拖拽调整，排在前面优先使用）</span></label>
        <button type="button" class="btn" id="btn-priority-reset" style="font-size:12px;padding:4px 12px">恢复默认顺序</button>
      </div>
      <div id="priority-list"></div>
    </div>
  `);
}

async function loadPriorityData() {
  currentSiteId = getSelectedSiteId() || '';

  // 先刷新账号列表，再获取缓存
  try { await refreshAccounts(); } catch {}
  cachedAccounts = await getAccounts() || [];
  try {
    cachedQuotas = await api('/api/quotas') || [];
  } catch {
    cachedQuotas = [];
  }

  // 尝试加载已保存的优先级配置
  let savedPriorities = [];
  try {
    if (currentSiteId) {
      const data = await api(`/api/settings/priority-routing?siteId=${encodeURIComponent(currentSiteId)}`);
      priorityRoutingEnabled = data.priorityRoutingEnabled ?? false;
      priorityMinActiveCount = data.priorityMinActiveCount ?? 2;
      savedPriorities = data.accountPriorities || [];
    }
  } catch {
    // 首次访问或无配置时忽略
  }

  if (savedPriorities.length > 0) {
    // 已有配置：使用保存的顺序，补充新增账号到末尾
    priorityEntries = savedPriorities.map(p => ({ name: p.name, priority: p.priority }));
    const existingNames = new Set(priorityEntries.map(e => e.name.toLowerCase()));
    let nextPriority = priorityEntries.length;
    for (const a of cachedAccounts) {
      if (!existingNames.has(a.name.toLowerCase())) {
        nextPriority++;
        priorityEntries.push({ name: a.name, priority: nextPriority });
      }
    }
  } else {
    // 从未配置：用全部账号按默认顺序初始化
    priorityEntries = cachedAccounts.map((a, i) => ({ name: a.name, priority: i + 1 }));
  }

  document.getElementById('priority-enabled').checked = priorityRoutingEnabled;
  document.getElementById('priority-min-active').value = priorityMinActiveCount;
  renderPriorityList();
}

function renderPriorityList() {
  const container = document.getElementById('priority-list');
  if (!container) return;

  if (priorityEntries.length === 0) {
    container.innerHTML = `<div style="color:var(--text-muted);font-size:13px;padding:12px 0">暂无账号，请先在系统设置中配置站点并同步账号</div>`;
    return;
  }

  const accountMap = {};
  const quotaMap = {};
  cachedAccounts.forEach(a => { accountMap[a.name] = a; });
  cachedQuotas.forEach(q => { quotaMap[q.accountName] = q; });

  let html = '';
  priorityEntries.forEach((entry, index) => {
    const account = accountMap[entry.name];
    const quota = quotaMap[entry.name];
    const displayName = account
      ? (getDisplayAccount(account, quota) || account.account || account.email || account.label || account.name)
      : entry.name;
    const isDisabled = account?.disabled;
    const statusClass = isDisabled ? 'badge-bad' : 'badge-good';
    const statusText = isDisabled ? '已禁用' : '启用中';
    const planType = quota?.planType?.trim();
    const quotaText = buildPriorityQuotaText(quota);

    html += `<div class="priority-row" draggable="true" data-index="${index}">
      <span class="drag-handle" title="拖拽排序">⠿</span>
      <span class="priority-badge">P${index + 1}</span>
      <div class="priority-main" title="${escapeHtml(entry.name)}">
        <span class="priority-label">${escapeHtml(displayName)}</span>
        <span class="priority-quota">${escapeHtml(quotaText)}</span>
      </div>
      ${planType ? `<span class="priority-plan" title="套餐：${escapeHtml(planType)}">${escapeHtml(planType)}</span>` : ''}
      <span class="badge ${statusClass}" style="font-size:10px;padding:2px 6px;border-radius:10px">${statusText}</span>
    </div>`;
  });

  container.innerHTML = html;
  bindDragSort(container);
}

function bindDragSort(container) {
  let dragIndex = null;

  container.querySelectorAll('.priority-row[draggable]').forEach(row => {
    row.addEventListener('dragstart', (e) => {
      dragIndex = parseInt(row.dataset.index);
      row.classList.add('dragging');
      e.dataTransfer.effectAllowed = 'move';
      try { e.dataTransfer.setData('text/plain', ''); } catch {}
    });

    row.addEventListener('dragend', () => {
      row.classList.remove('dragging');
      container.querySelectorAll('.priority-row').forEach(r => r.classList.remove('drag-over'));
      dragIndex = null;
    });

    row.addEventListener('dragover', (e) => {
      e.preventDefault();
      e.dataTransfer.dropEffect = 'move';
      container.querySelectorAll('.priority-row').forEach(r => r.classList.remove('drag-over'));
      row.classList.add('drag-over');
    });

    row.addEventListener('dragleave', () => {
      row.classList.remove('drag-over');
    });

    row.addEventListener('drop', (e) => {
      e.preventDefault();
      const dropIndex = parseInt(row.dataset.index);
      if (dragIndex === null || dragIndex === dropIndex) return;

      const item = priorityEntries.splice(dragIndex, 1)[0];
      priorityEntries.splice(dropIndex, 0, item);
      renderPriorityList();
    });
  });
}

async function savePriorityConfig() {
  if (!currentSiteId) {
    showToast('未选择站点，请先在顶栏选择站点', 'error');
    return;
  }

  try {
    setLoading(true);
    const enabled = document.getElementById('priority-enabled').checked;
    const minActive = Number(document.getElementById('priority-min-active').value) || 2;

    const entries = priorityEntries.map((entry, index) => ({
      name: entry.name,
      priority: index + 1,
    }));

    await api(`/api/settings/priority-routing?siteId=${encodeURIComponent(currentSiteId)}`, {
      method: 'PUT',
      body: JSON.stringify({
        priorityRoutingEnabled: enabled,
        priorityMinActiveCount: minActive,
        accountPriorities: entries,
      }),
    });

    priorityRoutingEnabled = enabled;
    priorityMinActiveCount = minActive;
    showToast('优先级路由配置已保存', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

function resetToDefaultOrder() {
  priorityEntries = cachedAccounts.map((a, i) => ({ name: a.name, priority: i + 1 }));
  renderPriorityList();
}

function bindEvents() {
  document.getElementById('btn-priority-save').addEventListener('click', savePriorityConfig);
  document.getElementById('btn-priority-reset').addEventListener('click', resetToDefaultOrder);
}

async function init() {
  renderPage();
  bindEvents();
  await loadPriorityData();
}

init();
