import {
  api,
  showToast,
  setLoading,
  formatDate,
  escapeHtml,
  getRuntimeProgress,
} from './common.js';
import { renderLayout } from './layout.js';

const actionMap = {
  0: { label: '保留', className: 'keep' },
  1: { label: '删除', className: 'delete' },
  2: { label: '禁用', className: 'disable' },
  3: { label: '启用', className: 'enable' },
  keep: { label: '保留', className: 'keep' },
  delete: { label: '删除', className: 'delete' },
  disable: { label: '禁用', className: 'disable' },
  enable: { label: '启用', className: 'enable' },
};

function renderPage() {
  renderLayout('inspection', '巡检管理', `
    <div class="page-header">
      <h1>巡检管理</h1>
      <div class="actions">
        <button class="btn btn-primary" id="btn-run-inspection">手动巡检</button>
        <button class="btn" id="btn-run-inspection-force">真实巡检</button>
        <button class="btn btn-success" id="btn-auto-start">启动自动轮询</button>
        <button class="btn btn-danger" id="btn-auto-stop" style="display:none">停止自动轮询</button>
      </div>
    </div>
    <div class="card">
      <h3>巡检状态</h3>
      <div id="inspection-status"></div>
    </div>
    <div class="card">
      <h3>实时进度</h3>
      <div id="inspection-progress"></div>
    </div>
    <div class="card">
      <h3>动作反馈</h3>
      <div id="inspection-feedback"></div>
    </div>
    <div class="card">
      <h3>上次巡检结果</h3>
      <div id="inspection-results"></div>
    </div>
  `);
}

function getActionMeta(action) {
  if (typeof action === 'number') {
    return actionMap[action] || { label: String(action), className: 'keep' };
  }

  const key = String(action || '').toLowerCase();
  return actionMap[key] || { label: String(action || '-'), className: 'keep' };
}

function renderProgress(progress) {
  const container = document.getElementById('inspection-progress');
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

function renderFeedback(result) {
  const container = document.getElementById('inspection-feedback');
  const outcomes = result?.actionOutcomes || [];
  const priorityOutcomes = result?.priorityRoutingOutcomes || [];

  if (!outcomes.length && !priorityOutcomes.length) {
    container.innerHTML = '<p>本次没有账号状态变更</p>';
    return;
  }

  let html = '';

  if (outcomes.length) {
    html += `<div class="feedback-list">
      ${outcomes.map(outcome => {
        const action = getActionMeta(outcome.action);
        const success = outcome.success !== false;
        return `
          <div class="feedback-item ${success ? 'success' : 'error'}">
            <strong>${escapeHtml(outcome.displayAccount || outcome.fileName || '-')}</strong>
            <span style="margin-left:8px" class="badge badge-${action.className}">${escapeHtml(action.label)}</span>
            <div class="hint" style="margin-top:6px; margin-bottom:0; color:${success ? '#166534' : '#991b1b'}">
              ${escapeHtml(success ? `执行成功：${outcome.fileName}` : `执行失败：${outcome.error || outcome.fileName}`)}
            </div>
          </div>
        `;
      }).join('')}
    </div>`;
  }

  if (priorityOutcomes.length) {
    html += `<div style="margin-top:8px;font-size:13px;color:var(--color-text-secondary);font-weight:500">优先级路由调度</div>
    <div class="feedback-list">
      ${priorityOutcomes.map(outcome => {
        const action = getActionMeta(outcome.action);
        const success = outcome.success !== false;
        return `
          <div class="feedback-item ${success ? 'success' : 'error'}">
            <strong>${escapeHtml(outcome.displayAccount || outcome.fileName || '-')}</strong>
            <span style="margin-left:8px" class="badge badge-${action.className}">${escapeHtml(action.label)}</span>
            <span style="margin-left:4px" class="badge badge-priority">优先级路由</span>
            <div class="hint" style="margin-top:6px; margin-bottom:0; color:${success ? '#166534' : '#991b1b'}">
              ${escapeHtml(success ? `调度成功：${outcome.fileName}` : `调度失败：${outcome.error || outcome.fileName}`)}
            </div>
          </div>
        `;
      }).join('')}
    </div>`;
  }

  container.innerHTML = html;
}

async function loadInspectionStatus() {
  try {
    const status = await api('/api/inspection/status');
    document.getElementById('inspection-status').innerHTML = `
      <p><span class="status-dot ${status.isPolling ? 'running' : 'stopped'}"></span>${status.isPolling ? '巡检运行中' : '巡检空闲'}</p>
      <p>自动轮询：${status.autoPollingEnabled ? '已启用' : '已停用'}</p>
      <p>轮询间隔：${status.pollIntervalMinutes} 分钟</p>
      <p>常规下次巡检：${formatDate(status.nextScheduledAt)}</p>
      <p>额度重置检查：${formatDate(status.nextResetCheckAt)}</p>
      <p>上次开始：${formatDate(status.lastRunStartedAt)}</p>
      <p>上次结束：${formatDate(status.lastRunFinishedAt)}</p>
    `;

    document.getElementById('btn-auto-start').style.display = status.autoPollingEnabled ? 'none' : 'inline-flex';
    document.getElementById('btn-auto-stop').style.display = status.autoPollingEnabled ? 'inline-flex' : 'none';
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function loadInspectionResults() {
  try {
    const result = await api('/api/inspection/last-run');
    const container = document.getElementById('inspection-results');
    renderFeedback(result);

    if (!result?.decisions?.length) {
      container.innerHTML = '<p>暂无巡检结果</p>';
      return;
    }

    container.innerHTML = `
      <div class="hint">总计 ${result.totalAccounts || 0} 个账号，保留 ${result.keepCount || 0}，禁用建议 ${result.disableCount || 0}，启用建议 ${result.enableCount || 0}，删除建议 ${result.deleteCount || 0}</div>
      <table class="table">
        <thead>
          <tr>
            <th>账号</th>
            <th>动作</th>
            <th>原因</th>
            <th>HTTP</th>
            <th>使用率</th>
            <th>检查时间</th>
          </tr>
        </thead>
        <tbody>
          ${result.decisions.map(decision => {
            const action = getActionMeta(decision.action);
            return `
              <tr>
                <td>${escapeHtml(decision.displayAccount || decision.accountName || '-')}</td>
                <td><span class="badge badge-${action.className}">${escapeHtml(action.label)}</span></td>
                <td>${escapeHtml(decision.reason || '-')}</td>
                <td>${decision.statusCode || '--'}</td>
                <td>${decision.usedPercent != null ? decision.usedPercent.toFixed(1) + '%' : '--'}</td>
                <td>${formatDate(decision.checkedAt)}</td>
              </tr>
            `;
          }).join('')}
        </tbody>
      </table>
    `;
  } catch (error) {
    if (!String(error.message || '').includes('HTTP 204')) {
      showToast(error.message, 'error');
    }
  }
}

async function loadRuntimePanels() {
  try {
    const progress = await getRuntimeProgress();
    renderProgress(progress);
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function runInspection(force = false) {
  try {
    setLoading(true);
    showToast(force ? '开始执行真实巡检...' : '开始执行手动巡检...', 'success');
    const suffix = force ? '?force=true' : '';
    const result = await api(`/api/inspection/run${suffix}`, { method: 'POST' });
    renderFeedback(result);
    await Promise.all([loadInspectionStatus(), loadInspectionResults(), loadRuntimePanels()]);

    const outcomeCount = result?.actionOutcomes?.length || 0;
    showToast(
      outcomeCount > 0
        ? `${force ? '真实巡检' : '手动巡检'}完成，已处理 ${outcomeCount} 个账号动作`
        : `${force ? '真实巡检' : '手动巡检'}完成，本次没有账号状态变更`,
      'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

async function startAutoPolling() {
  try {
    await api('/api/inspection/auto/start', { method: 'POST' });
    await loadInspectionStatus();
    showToast('自动轮询已启动', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function stopAutoPolling() {
  try {
    await api('/api/inspection/auto/stop', { method: 'POST' });
    await loadInspectionStatus();
    showToast('自动轮询已停止', 'warning');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

function bindEvents() {
  document.getElementById('btn-run-inspection').addEventListener('click', () => runInspection(false));
  document.getElementById('btn-run-inspection-force').addEventListener('click', () => runInspection(true));
  document.getElementById('btn-auto-start').addEventListener('click', startAutoPolling);
  document.getElementById('btn-auto-stop').addEventListener('click', stopAutoPolling);
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
  bindEvents();
  renderFeedback(null);
  await Promise.all([loadInspectionStatus(), loadInspectionResults(), loadRuntimePanels()]);
  setInterval(() => {
    loadInspectionStatus();
    loadInspectionResults();
    loadRuntimePanels();
  }, 3000);
}

init();
