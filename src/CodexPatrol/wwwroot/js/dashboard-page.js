import { api, showToast, formatDate, refreshAccounts, getAccounts, loadExceptionNames, escapeHtml, loadSites } from './common.js';
import { renderLayout } from './layout.js';

function renderPage() {
  renderLayout('dashboard', '仪表盘', `
    <h1>仪表盘</h1>
    <div class="stats-grid" id="dashboard-stats"></div>
    <div class="card">
      <h3>调用队列监控</h3>
      <div id="dashboard-usage-monitor"></div>
    </div>
    <div class="card">
      <h3>最近巡检结果</h3>
      <div id="dashboard-last-run"></div>
    </div>
  `);
}

function renderStats(status, accountCount, quotaCount, exceptionCount) {
  const autoPollingEnabled = status?.autoPollingEnabled === true;
  document.getElementById('dashboard-stats').innerHTML = `
    <div class="stat-card ${autoPollingEnabled ? 'good' : ''}">
      <div class="stat-value">${autoPollingEnabled ? '已启用' : '已停止'}</div>
      <div class="stat-label">自动轮询</div>
    </div>
    <div class="stat-card">
      <div class="stat-value">${accountCount}</div>
      <div class="stat-label">Codex 账号</div>
    </div>
    <div class="stat-card">
      <div class="stat-value">${quotaCount}</div>
      <div class="stat-label">已获取额度</div>
    </div>
    <div class="stat-card warn">
      <div class="stat-value">${exceptionCount}</div>
      <div class="stat-label">例外名单</div>
    </div>
  `;
}

function renderUsageMonitor(monitors) {
  const container = document.getElementById('dashboard-usage-monitor');
  if (!monitors || monitors.length === 0) {
    container.innerHTML = '<p>暂无监控数据</p>';
    return;
  }

  container.innerHTML = monitors.map(m => {
    const statusTag = m.unsupported
      ? '<span class="badge badge-bad">不支持</span>'
      : m.active
        ? '<span class="badge badge-good">已激活</span>'
        : '<span class="badge badge-warn">等待中</span>';
    const lastPoll = m.lastPollAt && m.lastPollAt !== '0001-01-01T00:00:00'
      ? formatDate(m.lastPollAt)
      : '--';
    return `
      <div class="monitor-row">
        <div class="monitor-site">${statusTag} ${escapeHtml(m.siteName)}</div>
        <div class="monitor-stats">
          <span title="累计拉取条目">${m.totalItemsPopped} 条</span>
          <span title="轮询次数">${m.pollCount} 次</span>
          <span title="当前活跃账号数">${m.activeAuthIndexCount} 个活跃</span>
          <span title="最近轮询时间">${lastPoll}</span>
        </div>
      </div>
    `;
  }).join('');
}

function renderLastRun(lastRun) {
  const container = document.getElementById('dashboard-last-run');

  if (!lastRun) {
    container.innerHTML = '<p>暂无巡检记录</p>';
    return;
  }

  container.innerHTML = `
    <p>上次巡检：${formatDate(lastRun.finishedAt)}</p>
    <p>总账号：${lastRun.totalAccounts}，删除：${lastRun.deleteCount}，禁用：${lastRun.disableCount}，启用：${lastRun.enableCount}，保留：${lastRun.keepCount}</p>
  `;
}

async function loadDashboard({ refreshAccountList = false } = {}) {
  try {
    const accountLoader = refreshAccountList ? refreshAccounts() : getAccounts();
    const [accounts, status, lastRun, quotas, exceptionNames, monitors] = await Promise.all([
      accountLoader,
      api('/api/inspection/status'),
      api('/api/inspection/last-run'),
      api('/api/quotas'),
      loadExceptionNames(),
      api('/api/runtime/usage-monitor'),
    ]);

    renderStats(status, (accounts || []).length, (quotas || []).length, exceptionNames.size);
    renderUsageMonitor(monitors);
    renderLastRun(lastRun);
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function init() {
  renderPage();
  await loadSites();
  await loadDashboard({ refreshAccountList: true });
  setInterval(() => {
    loadDashboard();
  }, 10000);
}

init();
