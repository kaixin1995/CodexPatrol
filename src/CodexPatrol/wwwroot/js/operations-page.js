import { escapeHtml, formatDate, getOperationLogs } from './common.js';
import { renderLayout } from './layout.js';

let currentCategoryFilter = 'all';
let currentLevelFilter = 'all';
let lastRenderedLogSignature = '';

function syncOperationsLayoutHeight() {
  const shell = document.querySelector('.operations-page-shell');
  if (!shell) {
    return;
  }

  const shellTop = shell.getBoundingClientRect().top;
  const targetBottom = window.innerHeight - 18;
  const availableHeight = Math.max(320, Math.floor(targetBottom - shellTop));
  shell.style.height = `${availableHeight}px`;
}

function renderPage() {
  renderLayout('operations', '操作日志', `
    <div class="operations-page-shell">
      <div class="page-header">
        <h1>操作日志</h1>
      </div>
      <div class="card operations-page-card">
        <div class="operations-log-header">
          <h3>最近 200 条日志</h3>
          <div class="operations-log-filters">
            <select id="operations-category-filter" class="topbar-site-select">
              <option value="all">全部类别</option>
              <option value="inspection">巡检</option>
              <option value="quota">额度刷新</option>
              <option value="account">账号操作</option>
              <option value="priority">优先级路由</option>
              <option value="monitor">监控</option>
              <option value="system">系统</option>
            </select>
            <select id="operations-level-filter" class="topbar-site-select">
              <option value="all">全部级别</option>
              <option value="error">仅错误</option>
              <option value="warning">仅警告</option>
              <option value="info">仅正常</option>
            </select>
            <button type="button" class="btn" id="operations-refresh-btn">手动刷新</button>
          </div>
        </div>
        <div id="operations-logs" class="log-scroll operations-page-log-scroll"></div>
      </div>
    </div>
  `);
}
function renderLogs(logs) {
  const container = document.getElementById('operations-logs');
  const filteredLogs = filterLogs(logs || []);

  if (!filteredLogs.length) {
    container.innerHTML = '<p>暂无符合条件的日志</p>';
    lastRenderedLogSignature = 'empty';
    return;
  }

  const signature = filteredLogs.map(item => `${item.id}|${item.level}|${item.message}|${item.createdAt}`).join('\n');
  if (signature === lastRenderedLogSignature) {
    return;
  }

  lastRenderedLogSignature = signature;
  container.innerHTML = `
    <div class="log-list">
      ${filteredLogs.map(item => `
        <div class="log-item ${escapeHtml(item.level || 'info')}">
          <div class="log-item-header">
            <span>${formatDate(item.createdAt)}</span>
            <span>${escapeHtml(resolveCategoryLabel(item.category))} / ${escapeHtml(resolveSourceLabel(item.source))} / ${escapeHtml(resolveOperationLabel(item.operationType))}</span>
          </div>
          <div class="log-item-message">
            ${escapeHtml(item.message || '-')}
            ${item.siteName ? `<div class="hint" style="margin-top:4px; margin-bottom:0">站点：${escapeHtml(item.siteName)}${item.siteId ? ` / ${escapeHtml(item.siteId)}` : ''}</div>` : ''}
            ${item.displayAccount || item.accountName ? `<div class="hint" style="margin-top:4px; margin-bottom:0">账号：${escapeHtml(item.displayAccount || item.accountName)}</div>` : ''}
          </div>
        </div>
      `).join('')}
    </div>
  `;
}

function filterLogs(logs) {
  return logs.filter(item => {
    const category = String(item.category || '').toLowerCase();
    const level = String(item.level || '').toLowerCase();

    const categoryMatched = currentCategoryFilter === 'all'
      || category === currentCategoryFilter
      || (currentCategoryFilter === 'monitor' && String(item.operationType || '').toLowerCase() === 'usagequeue');

    const levelMatched = currentLevelFilter === 'all' || level === currentLevelFilter;
    return categoryMatched && levelMatched;
  });
}

async function loadPageData() {
  const logs = await getOperationLogs(200);
  renderLogs(logs || []);
}

function resolveCategoryLabel(category) {
  switch (String(category || '').toLowerCase()) {
    case 'inspection':
      return '巡检';
    case 'quota':
      return '额度';
    case 'account':
      return '账号';
    case 'monitor':
      return '监控';
    case 'priority':
      return '优先级';
    case 'system':
      return '系统';
    default:
      return category || '-';
  }
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
    case 'usagequeue':
      return '调用队列监控';
    case 'priorityrouting':
      return '优先级路由';
    case 'settings':
      return '配置更新';
    default:
      return operationType || '-';
  }
}

function resolveSourceLabel(source) {
  switch (String(source || '').toLowerCase()) {
    case 'manual':
      return '手动';
    case 'auto':
      return '自动';
    case 'system':
      return '系统';
    default:
      return source || '-';
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
  syncOperationsLayoutHeight();
  window.addEventListener('resize', syncOperationsLayoutHeight);

  document.getElementById('operations-category-filter')?.addEventListener('change', event => {
    currentCategoryFilter = event.target.value || 'all';
    lastRenderedLogSignature = '';
    loadPageData();
  });

  document.getElementById('operations-level-filter')?.addEventListener('change', event => {
    currentLevelFilter = event.target.value || 'all';
    lastRenderedLogSignature = '';
    loadPageData();
  });

  document.getElementById('operations-refresh-btn')?.addEventListener('click', () => {
    loadPageData();
  });

  await loadPageData();
}

init();
