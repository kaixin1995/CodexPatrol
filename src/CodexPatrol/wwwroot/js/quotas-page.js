import {
  api,
  showToast,
  setLoading,
  formatDate,
  escapeHtml,
  getAccountName,
  getAccountDisabled,
  getDisplayAccount,
  refreshAccounts,
  getAccounts,
  loadExceptionNames,
  getRuntimeProgress,
  getOperationLogs,
} from './common.js';
import { renderLayout } from './layout.js';

let cachedAccounts = [];
let cachedExceptions = new Set();
let cachedQuotas = [];
let currentQuotaPage = 1;
const quotaPageSize = 10;
let currentFilter = 'all';

function renderPage() {
  renderLayout('quotas', '额度管理', `
    <div class="page-header">
      <h1>额度管理</h1>
      <div class="actions">
        <button class="btn btn-primary" id="btn-refresh-all">刷新全部额度</button>
        <button class="btn" id="btn-refresh-all-force">真实请求</button>
        <button class="btn" id="btn-refresh-page">刷新本页</button>
      </div>
    </div>
    <div class="settings-tabs" id="quota-filter-tabs">
      <button class="settings-tab active" data-filter="all">显示全部</button>
      <button class="settings-tab" data-filter="disabled">仅禁用</button>
      <button class="settings-tab" data-filter="enabled">仅启用</button>
      <button class="settings-tab" data-filter="error">仅错误</button>
    </div>
    <div class="card">
      <h3>实时进度</h3>
      <div id="quota-progress"></div>
    </div>
    <div class="quota-list-toolbar" id="quota-list-toolbar" style="display:none">
      <div class="quota-list-summary" id="quota-page-summary"></div>
    </div>
    <div class="quota-pagination" id="quota-pagination-top" style="display:none"></div>
    <div class="quota-grid" id="quota-grid"></div>
    <div id="quota-empty" class="empty-state" style="display:none">
      <p>暂无额度数据，请先刷新额度或执行巡检</p>
    </div>
    <div class="card">
      <h3>运行输出</h3>
      <div id="quota-logs" class="log-scroll"></div>
    </div>
  `);
}

function formatResetCountdown(resetAtUtc, fallbackLabel = '-') {
  if (!resetAtUtc) {
    return fallbackLabel;
  }

  const timestamp = Date.parse(resetAtUtc);
  if (Number.isNaN(timestamp)) {
    return fallbackLabel;
  }

  const remainingMs = timestamp - Date.now();
  if (remainingMs <= 0) {
    return '已重置';
  }

  const totalMinutes = Math.floor(remainingMs / 60000);
  const days = Math.floor(totalMinutes / (24 * 60));
  const hours = Math.floor((totalMinutes % (24 * 60)) / 60);
  const minutes = totalMinutes % 60;
  const parts = [];

  if (days > 0) parts.push(`${days}天`);
  if (hours > 0) parts.push(`${hours}小时`);
  if (minutes > 0) parts.push(`${minutes}分`);

  return parts.length > 0 ? `${parts.join('')}后重置` : '<1分钟后重置';
}

function updateQuotaResetCountdowns() {
  document.querySelectorAll('.quota-reset[data-reset-at-utc]').forEach(element => {
    const resetLabel = formatResetCountdown(element.dataset.resetAtUtc || '', element.dataset.fallbackLabel || '-');
    element.textContent = resetLabel;
    element.title = resetLabel;
  });
}

function renderQuotaWindow(window) {
  const percent = window.usedPercent ?? 0;
  const tone = percent >= 100 ? 'bad' : percent >= 80 ? 'warn' : 'good';
  const label = window.label || '-';
  const fallbackResetLabel = window.resetLabel || '-';
  const resetAtUtc = window.resetAtUtc || '';
  const resetLabel = formatResetCountdown(resetAtUtc, fallbackResetLabel);

  return `
    <div class="quota-window">
      <div class="quota-window-label" title="${escapeHtml(label)}">${escapeHtml(label)}</div>
      <div class="quota-window-bar">
        <div class="quota-window-fill ${tone}" style="width:${Math.min(percent, 100)}%"></div>
      </div>
      <div class="quota-percent">${window.usedPercent != null ? window.usedPercent.toFixed(1) + '%' : '--'}</div>
      <div class="quota-reset" data-reset-at-utc="${escapeHtml(resetAtUtc)}" data-fallback-label="${escapeHtml(fallbackResetLabel)}" title="${escapeHtml(resetLabel)}">${escapeHtml(resetLabel)}</div>
    </div>
  `;
}

function renderAccountCard(account, quota) {
  const accountName = getAccountName(account);
  const displayName = getDisplayAccount(account, quota);
  const disabled = quota?.disabled ?? getAccountDisabled(account);
  const isException = cachedExceptions.has(accountName);
  const sourceBadge = quota?.fromCache
    ? '<span class="badge badge-cache">缓存</span>'
    : quota?.refreshedAt
      ? '<span class="badge badge-live">实时</span>'
      : '';

  let windowsHtml = '';
  if (quota?.windows?.length) {
    windowsHtml = quota.windows.map(renderQuotaWindow).join('');
  } else if (quota?.errorMessage) {
    windowsHtml = `<p class="hint" style="color:#dc2626">${escapeHtml(quota.errorMessage)}</p>`;
  } else if (quota) {
    windowsHtml = '<p class="hint">暂无额度窗口数据</p>';
  } else {
    windowsHtml = '<p class="hint">未刷新额度</p>';
  }

  const statusBadge = disabled
    ? '<span class="badge badge-disable">已禁用</span>'
    : '<span class="badge badge-enable">启用中</span>';
  const exceptionBadge = isException
    ? ' <span class="badge badge-keep">例外</span>'
    : '';

  const toggleBtn = disabled
    ? '<button class="btn btn-sm btn-success" data-action="enable-account" data-account-name="' + escapeHtml(accountName) + '">启用</button>'
    : '<button class="btn btn-sm btn-danger" data-action="disable-account" data-account-name="' + escapeHtml(accountName) + '">禁用</button>';

  return `
    <div class="quota-card">
      <div class="quota-card-header">
        <div class="quota-header-main">
          <div class="quota-account" title="${escapeHtml(displayName)}">${escapeHtml(displayName)}</div>
          <div class="quota-header-meta">
            <div class="quota-badges">
              ${statusBadge}${exceptionBadge}${sourceBadge}
            </div>
            <div class="quota-file-name" title="${escapeHtml(accountName)}">${escapeHtml(accountName)}</div>
          </div>
        </div>
        ${quota?.planType ? `<span class="quota-plan">${escapeHtml(quota.planType)}</span>` : ''}
      </div>
      <div>${windowsHtml}</div>
      <div class="quota-meta">
        <div class="quota-source-meta">
          <span title="${escapeHtml(quota?.refreshedAt ? '刷新时间：' + formatDate(quota.refreshedAt) : '')}">${quota?.refreshedAt ? '刷新时间：' + formatDate(quota.refreshedAt) : ''}</span>
          ${quota?.fromCache ? `<span title="${escapeHtml(`缓存依据：${quota.cacheReason || '-'}`)}">缓存依据：${escapeHtml(quota.cacheReason || '-')}</span>` : ''}
        </div>
        <div class="quota-actions">
          <button class="btn btn-sm" data-action="refresh-single" data-account-name="${escapeHtml(accountName)}">刷新额度</button>
          ${toggleBtn}
        </div>
      </div>
    </div>
  `;
}

// 额度分页只影响展示，不改变后端全量刷新逻辑。
function getFilteredAccounts() {
  if (currentFilter === 'all') return cachedAccounts;
  const quotaMap = Object.fromEntries((cachedQuotas || []).map(q => [q.accountName, q]));
  return cachedAccounts.filter(account => {
    const accountName = getAccountName(account);
    const quota = quotaMap[accountName];
    const disabled = quota?.disabled ?? getAccountDisabled(account);
    switch (currentFilter) {
      case 'disabled': return disabled;
      case 'enabled': return !disabled;
      case 'error': return quota && !quota.success;
      default: return true;
    }
  });
}

function getQuotaTotalPages() {
  return Math.max(1, Math.ceil(getFilteredAccounts().length / quotaPageSize));
}

function normalizeQuotaPage() {
  currentQuotaPage = Math.min(Math.max(1, currentQuotaPage), getQuotaTotalPages());
}

function getVisibleQuotaAccounts() {
  normalizeQuotaPage();
  const filtered = getFilteredAccounts();
  const startIndex = (currentQuotaPage - 1) * quotaPageSize;
  return filtered.slice(startIndex, startIndex + quotaPageSize);
}

function buildQuotaPageItems(totalPages) {
  const items = [];
  const startPage = Math.max(1, currentQuotaPage - 2);
  const endPage = Math.min(totalPages, currentQuotaPage + 2);

  if (startPage > 1) {
    items.push(1);
    if (startPage > 2) {
      items.push('start-ellipsis');
    }
  }

  for (let page = startPage; page <= endPage; page++) {
    items.push(page);
  }

  if (endPage < totalPages) {
    if (endPage < totalPages - 1) {
      items.push('end-ellipsis');
    }
    items.push(totalPages);
  }

  return items;
}

function buildQuotaPaginationHtml() {
  const totalPages = getQuotaTotalPages();
  const pageItems = buildQuotaPageItems(totalPages);

  return `
    <div class="quota-pagination-inner">
      <div class="quota-pagination-nav">
        <button class="btn btn-sm" data-page-action="first" ${currentQuotaPage === 1 ? 'disabled' : ''}>首页</button>
        <button class="btn btn-sm" data-page-action="prev" ${currentQuotaPage === 1 ? 'disabled' : ''}>上一页</button>
        <div class="quota-pagination-pages">
          ${pageItems.map(item => typeof item === 'number'
            ? `<button class="btn btn-sm quota-page-btn ${item === currentQuotaPage ? 'active' : ''}" data-page="${item}">${item}</button>`
            : '<span class="quota-pagination-ellipsis">...</span>').join('')}
        </div>
        <button class="btn btn-sm" data-page-action="next" ${currentQuotaPage === totalPages ? 'disabled' : ''}>下一页</button>
        <button class="btn btn-sm" data-page-action="last" ${currentQuotaPage === totalPages ? 'disabled' : ''}>末页</button>
      </div>
      <div class="quota-pagination-jump">
        <span>跳至</span>
        <input type="number" class="quota-pagination-input" min="1" max="${totalPages}" value="${currentQuotaPage}">
        <span>页</span>
        <button class="btn btn-sm" data-page-action="jump">确定</button>
      </div>
    </div>
  `;
}

function renderQuotaPagination() {
  const toolbar = document.getElementById('quota-list-toolbar');
  const summary = document.getElementById('quota-page-summary');
  const topPagination = document.getElementById('quota-pagination-top');
  const refreshPageButton = document.getElementById('btn-refresh-page');

  const filtered = getFilteredAccounts();
  if (cachedAccounts.length === 0) {
    toolbar.style.display = 'none';
    topPagination.style.display = 'none';
    refreshPageButton.disabled = true;
    return;
  }

  normalizeQuotaPage();
  const totalPages = getQuotaTotalPages();
  const totalCount = filtered.length;
  const startIndex = totalCount === 0 ? 0 : (currentQuotaPage - 1) * quotaPageSize + 1;
  const endIndex = Math.min(currentQuotaPage * quotaPageSize, totalCount);

  const filterLabel = currentFilter === 'all' ? '' : `，筛选: ${document.querySelector(`[data-filter="${currentFilter}"]`)?.textContent || currentFilter}`;
  summary.textContent = `共 ${cachedAccounts.length} 个账号${filterLabel}，当前 ${totalCount} 个，每页 ${quotaPageSize} 个，显示第 ${startIndex}-${endIndex} 个，第 ${currentQuotaPage}/${totalPages} 页`;
  toolbar.style.display = 'flex';
  topPagination.style.display = 'block';
  topPagination.innerHTML = buildQuotaPaginationHtml();
  refreshPageButton.disabled = false;
}

function setQuotaPage(page, { showInvalidMessage = false } = {}) {
  const totalPages = getQuotaTotalPages();
  const targetPage = Number(page);
  if (!Number.isInteger(targetPage) || targetPage < 1 || targetPage > totalPages) {
    if (showInvalidMessage) {
      showToast(`请输入 1 到 ${totalPages} 之间的页码`, 'warning');
    }
    return;
  }

  currentQuotaPage = targetPage;
  renderQuotaGrid();
}

function renderQuotaGrid() {
  const grid = document.getElementById('quota-grid');
  const empty = document.getElementById('quota-empty');
  const quotaMap = Object.fromEntries((cachedQuotas || []).map(quota => [quota.accountName, quota]));

  if (cachedAccounts.length === 0) {
    grid.innerHTML = '';
    empty.style.display = 'block';
    empty.querySelector('p').textContent = '暂无账号数据，请检查 CPA 连接配置';
    renderQuotaPagination();
    return;
  }

  const filtered = getFilteredAccounts();
  if (filtered.length === 0) {
    grid.innerHTML = '';
    empty.style.display = 'block';
    empty.querySelector('p').textContent = '当前筛选条件下没有匹配的账号';
    renderQuotaPagination();
    return;
  }

  empty.style.display = 'none';
  grid.innerHTML = getVisibleQuotaAccounts().map(account => renderAccountCard(account, quotaMap[getAccountName(account)])).join('');
  updateQuotaResetCountdowns();
  renderQuotaPagination();
}

function renderProgress(progress) {
  const container = document.getElementById('quota-progress');
  if (!progress || (!progress.startedAt && progress.status === 'idle')) {
    container.innerHTML = '<p>暂无运行中的任务</p>';
    return;
  }

  const percent = Math.max(0, Math.min(100, progress.percent ?? 0));
  container.innerHTML = `
    <div class="progress-card">
      <div class="progress-summary">
        <strong>${escapeHtml(progress.message || '暂无进度信息')}</strong>
        <span>${percent.toFixed(1)}%</span>
      </div>
      <div class="progress-bar">
        <div class="progress-bar-fill" style="width:${percent}%"></div>
      </div>
      <div class="progress-meta">
        <div class="progress-meta-item"><span class="progress-meta-label">任务类型：</span><span class="progress-meta-value">${escapeHtml(resolveOperationLabel(progress.operationType))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">状态：</span><span class="progress-meta-value">${escapeHtml(resolveStatusLabel(progress.status))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">阶段：</span><span class="progress-meta-value">${escapeHtml(resolveStageLabel(progress.stage))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">探测进度：</span><span class="progress-meta-value">${progress.processed || 0} / ${progress.total || 0}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">动作进度：</span><span class="progress-meta-value">${progress.actionProcessed || 0} / ${progress.actionTotal || 0}</span></div>
        <div class="progress-meta-item progress-meta-item-wide"><span class="progress-meta-label">当前账号：</span><span class="progress-meta-value progress-meta-value-break" title="${escapeHtml(progress.currentDisplayAccount || progress.currentAccountName || '-')}">${escapeHtml(progress.currentDisplayAccount || progress.currentAccountName || '-')}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">开始时间：</span><span class="progress-meta-value progress-meta-value-nowrap">${formatDate(progress.startedAt)}</span></div>
      </div>
    </div>
  `;
}

function renderLogs(logs) {
  const container = document.getElementById('quota-logs');
  if (!logs?.length) {
    container.innerHTML = '<p>暂无运行日志</p>';
    return;
  }

  const filteredLogs = logs.filter(item => ['quota', 'inspection', 'account', 'system'].includes(item.category)).slice(0, 20);
  if (!filteredLogs.length) {
    container.innerHTML = '<p>暂无运行日志</p>';
    return;
  }

  container.innerHTML = `
    <div class="log-list">
      ${filteredLogs.map(item => `
        <div class="log-item ${escapeHtml(item.level || 'info')}">
          <div class="log-item-header">
            <span>${formatDate(item.createdAt)}</span>
            <span>${escapeHtml(resolveOperationLabel(item.operationType))}</span>
          </div>
          <div class="log-item-message">
            ${escapeHtml(item.message || '-')}
            ${item.displayAccount || item.accountName ? `<div class="hint" style="margin-top:4px; margin-bottom:0">账号：${escapeHtml(item.displayAccount || item.accountName)}</div>` : ''}
          </div>
        </div>
      `).join('')}
    </div>
  `;
}

async function loadQuotasPage({ refreshAccountList = false } = {}) {
  try {
    if (refreshAccountList) {
      cachedAccounts = await refreshAccounts();
    } else if (cachedAccounts.length === 0) {
      cachedAccounts = await getAccounts();
    }

    const [quotas, exceptionNames] = await Promise.all([
      api('/api/quotas'),
      loadExceptionNames(),
    ]);

    cachedQuotas = quotas || [];
    cachedExceptions = exceptionNames;
    renderQuotaGrid();
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function loadRuntimePanels() {
  try {
    const [progress, logs] = await Promise.all([
      getRuntimeProgress(),
      getOperationLogs(20),
    ]);
    renderProgress(progress);
    renderLogs(logs || []);
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function refreshAllQuotas(force = false) {
  try {
    setLoading(true);
    showToast(force ? '开始通过真实接口刷新全部额度...' : '开始刷新全部额度...', 'success');
    const suffix = force ? '?force=true' : '';
    const result = await api(`/api/quotas/refresh${suffix}`, { method: 'POST' });
    await Promise.all([loadQuotasPage(), loadRuntimePanels()]);
    showToast(force ? `真实接口刷新完成，共处理 ${result?.refreshed ?? 0} 个账号` : `全部额度刷新完成，共处理 ${result?.refreshed ?? 0} 个账号`, 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

// 刷新本页会顺序调用当前页账号的单账号刷新接口，不影响全量刷新入口。
async function refreshCurrentPageQuotas() {
  const pageAccounts = getVisibleQuotaAccounts();
  if (pageAccounts.length === 0) {
    showToast('当前页没有可刷新的账号', 'warning');
    return;
  }

  try {
    setLoading(true);
    showToast(`开始刷新当前页 ${pageAccounts.length} 个账号...`, 'success');

    for (const account of pageAccounts) {
      const accountName = getAccountName(account);
      await api(`/api/quotas/${encodeURIComponent(accountName)}/refresh`, { method: 'POST' });
    }

    await Promise.all([loadQuotasPage(), loadRuntimePanels()]);
    showToast(`当前页 ${pageAccounts.length} 个账号额度已刷新`, 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

async function refreshSingleQuota(accountName, force = false) {
  try {
    setLoading(true);
    const suffix = force ? '?force=true' : '';
    await api(`/api/quotas/${encodeURIComponent(accountName)}/refresh${suffix}`, { method: 'POST' });
    await Promise.all([loadQuotasPage(), loadRuntimePanels()]);
    showToast(force ? `账号 ${accountName} 已通过真实接口刷新额度` : `账号 ${accountName} 的额度已刷新`, 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

async function toggleAccount(accountName, action) {
  try {
    setLoading(true);
    const result = await api(`/api/accounts/${encodeURIComponent(accountName)}/${action}`, { method: 'POST' });
    await Promise.all([loadQuotasPage({ refreshAccountList: true }), loadRuntimePanels()]);
    showToast(result?.message || `账号 ${accountName} 已${action === 'enable' ? '启用' : '禁用'}`, 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

function handleQuotaPaginationClick(event) {
  const pageButton = event.target.closest('[data-page]');
  if (pageButton) {
    setQuotaPage(Number(pageButton.dataset.page));
    return;
  }

  const actionButton = event.target.closest('[data-page-action]');
  if (!actionButton) {
    return;
  }

  switch (actionButton.dataset.pageAction) {
    case 'first':
      setQuotaPage(1);
      return;
    case 'prev':
      setQuotaPage(currentQuotaPage - 1);
      return;
    case 'next':
      setQuotaPage(currentQuotaPage + 1);
      return;
    case 'last':
      setQuotaPage(getQuotaTotalPages());
      return;
    case 'jump': {
      const container = actionButton.closest('.quota-pagination');
      const input = container?.querySelector('.quota-pagination-input');
      setQuotaPage(Number(input?.value), { showInvalidMessage: true });
      return;
    }
    default:
      return;
  }
}

function handleQuotaPaginationKeydown(event) {
  if (!(event.target instanceof HTMLInputElement) || !event.target.classList.contains('quota-pagination-input') || event.key !== 'Enter') {
    return;
  }

  setQuotaPage(Number(event.target.value), { showInvalidMessage: true });
}

function bindEvents() {
  document.getElementById('btn-refresh-all').addEventListener('click', () => refreshAllQuotas(false));
  document.getElementById('btn-refresh-all-force').addEventListener('click', () => refreshAllQuotas(true));
  document.getElementById('btn-refresh-page').addEventListener('click', refreshCurrentPageQuotas);
  document.getElementById('quota-filter-tabs').addEventListener('click', event => {
    const tab = event.target.closest('[data-filter]');
    if (!tab) return;
    document.querySelectorAll('#quota-filter-tabs .settings-tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    currentFilter = tab.dataset.filter;
    currentQuotaPage = 1;
    renderQuotaGrid();
  });
  document.getElementById('quota-pagination-top').addEventListener('click', handleQuotaPaginationClick);
  document.getElementById('quota-pagination-top').addEventListener('keydown', handleQuotaPaginationKeydown);
  document.getElementById('quota-grid').addEventListener('click', event => {
    const refreshBtn = event.target.closest('[data-action="refresh-single"]');
    if (refreshBtn) {
      refreshSingleQuota(refreshBtn.dataset.accountName || '', true);
      return;
    }

    const disableBtn = event.target.closest('[data-action="disable-account"]');
    if (disableBtn) {
      toggleAccount(disableBtn.dataset.accountName || '', 'disable');
      return;
    }

    const enableBtn = event.target.closest('[data-action="enable-account"]');
    if (enableBtn) {
      toggleAccount(enableBtn.dataset.accountName || '', 'enable');
    }
  });
}

function resolveOperationLabel(operationType) {
  switch (String(operationType || '').toLowerCase()) {
    case 'inspection':
      return '巡检';
    case 'quotarefresh':
      return '额度刷新';
    case 'accountdisable':
      return '账号禁用';
    case 'accountenable':
      return '账号启用';
    case 'startup':
      return '启动同步';
    default:
      return operationType || '-';
  }
}

function resolveStatusLabel(status) {
  switch (String(status || '').toLowerCase()) {
    case 'running':
      return '运行中';
    case 'completed':
      return '已完成';
    case 'error':
      return '失败';
    default:
      return '空闲';
  }
}

function resolveStageLabel(stage) {
  switch (String(stage || '').toLowerCase()) {
    case 'prepare':
      return '准备中';
    case 'probing':
      return '探测中';
    case 'actions':
      return '执行动作';
    case 'delay':
      return '批次等待';
    case 'completed':
      return '已完成';
    case 'error':
      return '失败';
    default:
      return stage || '-';
  }
}

async function init() {
  renderPage();
  bindEvents();
  await Promise.all([
    loadQuotasPage({ refreshAccountList: true }),
    loadRuntimePanels(),
  ]);
  setInterval(() => {
    updateQuotaResetCountdowns();
  }, 1000);
  setInterval(() => {
    loadQuotasPage();
    loadRuntimePanels();
  }, 3000);
}

init();
