import { escapeHtml, formatDate, getOperationLogs, getRuntimeProgress } from './common.js';
import { renderLayout } from './layout.js';

let activeLogTab = 'all';

function renderPage() {
  renderLayout('operations', '操作日志', `
    <div class="page-header">
      <h1>操作日志</h1>
    </div>
    <div class="card">
      <h3>当前运行状态</h3>
      <div id="operations-progress"></div>
    </div>
    <div class="card">
      <div class="operations-log-header">
        <h3>最近 200 条日志</h3>
        <div class="settings-tabs operations-log-tabs" id="operations-log-tabs">
          <button type="button" class="settings-tab active" data-log-tab="all">全部日志</button>
          <button type="button" class="settings-tab" data-log-tab="monitor">监控日志</button>
        </div>
      </div>
      <div id="operations-logs" class="log-scroll"></div>
    </div>
  `);

  document.getElementById('operations-log-tabs')?.addEventListener('click', event => {
    const button = event.target.closest('[data-log-tab]');
    if (!button) {
      return;
    }

    activeLogTab = button.dataset.logTab || 'all';
    updateActiveLogTab();
    loadPageData();
  });
}

function renderProgress(progress) {
  const container = document.getElementById('operations-progress');
  if (!progress || (!progress.startedAt && progress.status === 'idle')) {
    container.innerHTML = '<p>当前没有运行中的任务</p>';
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
        <div class="progress-meta-item"><span class="progress-meta-label">触发方式：</span><span class="progress-meta-value">${escapeHtml(resolveSourceLabel(progress.source))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">状态：</span><span class="progress-meta-value">${escapeHtml(resolveStatusLabel(progress.status))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">阶段：</span><span class="progress-meta-value">${escapeHtml(resolveStageLabel(progress.stage))}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">探测进度：</span><span class="progress-meta-value">${progress.processed || 0} / ${progress.total || 0}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">动作进度：</span><span class="progress-meta-value">${progress.actionProcessed || 0} / ${progress.actionTotal || 0}</span></div>
        <div class="progress-meta-item progress-meta-item-wide"><span class="progress-meta-label">当前账号：</span><span class="progress-meta-value progress-meta-value-break" title="${escapeHtml(progress.currentDisplayAccount || progress.currentAccountName || '-')}">${escapeHtml(progress.currentDisplayAccount || progress.currentAccountName || '-')}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">开始时间：</span><span class="progress-meta-value progress-meta-value-nowrap">${formatDate(progress.startedAt)}</span></div>
        <div class="progress-meta-item"><span class="progress-meta-label">结束时间：</span><span class="progress-meta-value progress-meta-value-nowrap">${formatDate(progress.finishedAt)}</span></div>
      </div>
    </div>
  `;
}

function renderLogs(logs) {
  const container = document.getElementById('operations-logs');
  const filteredLogs = filterLogs(logs || []);

  if (!filteredLogs.length) {
    container.innerHTML = activeLogTab === 'monitor'
      ? '<p>暂无监控日志</p>'
      : '<p>暂无操作日志</p>';
    return;
  }

  container.innerHTML = `
    <div class="log-list">
      ${filteredLogs.map(item => `
        <div class="log-item ${escapeHtml(item.level || 'info')}">
          <div class="log-item-header">
            <span>${formatDate(item.createdAt)}</span>
            <span>${escapeHtml(resolveSourceLabel(item.source))} / ${escapeHtml(resolveOperationLabel(item.operationType))}</span>
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
  if (activeLogTab !== 'monitor') {
    return logs;
  }

  return logs.filter(item => String(item.category || '').toLowerCase() === 'monitor'
    || String(item.operationType || '').toLowerCase() === 'usagequeue');
}

function updateActiveLogTab() {
  document.querySelectorAll('[data-log-tab]').forEach(button => {
    button.classList.toggle('active', button.dataset.logTab === activeLogTab);
  });
}

async function loadPageData() {
  const [progress, logs] = await Promise.all([
    getRuntimeProgress(),
    getOperationLogs(200),
  ]);

  renderProgress(progress);
  renderLogs(logs || []);
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
  updateActiveLogTab();
  await loadPageData();
  setInterval(() => {
    loadPageData();
  }, 3000);
}

init();
