import { api, loadSites, setLoading, showToast, setSelectedSiteId } from './common.js';
import { renderLayout } from './layout.js';

let siteOptions = [];
let currentSiteId = '';
let createMode = false;

function renderPage() {
  renderLayout('settings', '系统设置', `
    <div class="page-header">
      <h1>系统设置</h1>
    </div>
    <div class="card">
      <div class="settings-tabs">
        <button type="button" class="settings-tab active" data-tab="site">站点管理</button>
        <button type="button" class="settings-tab" data-tab="policy">巡检策略</button>
      </div>
      <form id="settings-form">
        <div class="settings-panel" data-panel="site">
          <div class="settings-toolbar">
            <div class="form-group" style="margin-bottom:0; flex:1">
              <label>当前站点</label>
              <select id="settings-site-select"></select>
            </div>
            <div class="settings-site-actions">
              <button type="button" class="btn" id="btn-site-create">新增站点</button>
              <button type="button" class="btn btn-danger" id="btn-site-delete">删除当前站点</button>
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>站点名称</label>
              <input type="text" id="set-site-name" placeholder="例如：主站 / 备用站">
            </div>
            <div class="form-group">
              <label class="checkbox-label" style="margin-top:28px">
                <input type="checkbox" id="set-site-enabled" checked>
                启用该站点
              </label>
            </div>
          </div>
          <div class="form-group">
            <label>CPA 地址</label>
            <input type="text" id="set-cpa-url" placeholder="http://localhost:8317">
          </div>
          <div class="form-group">
            <label>Management Key</label>
            <input type="password" id="set-mgmt-key" placeholder="输入管理密钥，留空则保持不变">
          </div>
          <div class="form-group">
            <label>目标供应商</label>
            <select id="set-provider">
              <option value="codex">Codex</option>
              <option value="claude" disabled>Claude (预留)</option>
              <option value="gemini" disabled>Gemini (预留)</option>
            </select>
          </div>
        </div>

        <div class="settings-panel hidden" data-panel="policy">
          <div class="form-row">
            <div class="form-group">
              <label>轮询间隔 (分钟)</label>
              <input type="number" id="set-interval" min="5" value="10">
            </div>
            <div class="form-group">
              <label>自动动作模式</label>
              <select id="set-auto-action">
                <option value="none">仅巡检，不自动操作</option>
                <option value="disable">自动禁用</option>
                <option value="delete">自动删除</option>
              </select>
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>额外随机最小值 (分钟)</label>
              <input type="number" id="set-poll-random-min" min="0" value="1">
            </div>
            <div class="form-group">
              <label>额外随机最大值 (分钟)</label>
              <input type="number" id="set-poll-random-max" min="0" value="3">
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>探测并发数</label>
              <input type="number" id="set-probe-workers" min="1" max="20" value="3">
            </div>
            <div class="form-group">
              <label>执行并发数</label>
              <input type="number" id="set-action-workers" min="1" max="20" value="4">
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>批次最小延迟 (ms)</label>
              <input type="number" id="set-probe-delay-min" min="0" value="2000">
            </div>
            <div class="form-group">
              <label>批次最大延迟 (ms)</label>
              <input type="number" id="set-probe-delay-max" min="0" value="3000">
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>超时 (ms)</label>
              <input type="number" id="set-timeout" min="1000" value="15000">
            </div>
            <div class="form-group">
              <label>重试次数</label>
              <input type="number" id="set-retry" min="0" value="0">
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label>额度阈值 (%)</label>
              <input type="number" id="set-threshold" min="0" max="100" value="100">
            </div>
            <div class="form-group">
              <label class="checkbox-label" style="margin-top:28px">
                <input type="checkbox" id="set-auto-enable">
                自动启用已恢复的账号
              </label>
            </div>
          </div>
          <hr style="border-color:var(--border-color);margin:16px 0">
          <div class="form-row">
            <div class="form-group">
              <label class="checkbox-label">
                <input type="checkbox" id="set-priority-routing">
                启用优先级路由
              </label>
              <div style="color:var(--text-muted);font-size:12px;margin-top:4px">开启后账号按优先级顺序消费，至少保持指定数量的可用账号同时启用。<a href="/priority.html" style="color:var(--color-primary)">前往配置账号排列顺序 →</a></div>
            </div>
            <div class="form-group">
              <label>最少保持启用数</label>
              <input type="number" id="set-priority-min-active" min="1" max="10" value="2">
            </div>
          </div>
        </div>

        <button type="submit" class="btn btn-primary" id="btn-settings-save">保存设置</button>
      </form>
    </div>
  `);
}

function setActiveTab(tabName) {
  document.querySelectorAll('.settings-tab').forEach(button => {
    button.classList.toggle('active', button.dataset.tab === tabName);
  });
  document.querySelectorAll('.settings-panel').forEach(panel => {
    panel.classList.toggle('hidden', panel.dataset.panel !== tabName);
  });
}

function fillForm(settings) {
  currentSiteId = settings.siteId || '';
  createMode = false;
  document.getElementById('set-site-name').value = settings.siteName || '';
  document.getElementById('set-site-enabled').checked = settings.siteEnabled ?? true;
  document.getElementById('set-cpa-url').value = settings.cpaBaseUrl || '';
  document.getElementById('set-mgmt-key').value = '';
  document.getElementById('set-provider').value = settings.provider || 'codex';
  document.getElementById('set-interval').value = settings.pollIntervalMinutes ?? 10;
  document.getElementById('set-poll-random-min').value = settings.pollRandomDelayMinMinutes ?? 1;
  document.getElementById('set-poll-random-max').value = settings.pollRandomDelayMaxMinutes ?? 3;
  document.getElementById('set-probe-workers').value = settings.probeWorkers ?? 3;
  document.getElementById('set-probe-delay-min').value = settings.probeBatchDelayMinMs ?? 2000;
  document.getElementById('set-probe-delay-max').value = settings.probeBatchDelayMaxMs ?? 3000;
  document.getElementById('set-action-workers').value = settings.actionWorkers ?? 4;
  document.getElementById('set-timeout').value = settings.timeoutMs ?? 15000;
  document.getElementById('set-retry').value = settings.retryCount ?? 0;
  document.getElementById('set-auto-action').value = settings.autoActionMode || 'none';
  document.getElementById('set-threshold').value = settings.usedPercentThreshold ?? 100;
  document.getElementById('set-auto-enable').checked = settings.autoEnableRecovered ?? false;
  document.getElementById('set-priority-routing').checked = settings.priorityRoutingEnabled ?? false;
  document.getElementById('set-priority-min-active').value = settings.priorityMinActiveCount ?? 2;
  document.getElementById('btn-settings-save').textContent = '保存设置';
  syncSiteSelect(currentSiteId);
  updateDeleteButtonState();
}

function resetFormForCreate() {
  createMode = true;
  currentSiteId = '';
  document.getElementById('set-site-name').value = '';
  document.getElementById('set-site-enabled').checked = true;
  document.getElementById('set-cpa-url').value = '';
  document.getElementById('set-mgmt-key').value = '';
  document.getElementById('set-provider').value = 'codex';
  document.getElementById('set-interval').value = 10;
  document.getElementById('set-poll-random-min').value = 1;
  document.getElementById('set-poll-random-max').value = 3;
  document.getElementById('set-probe-workers').value = 3;
  document.getElementById('set-probe-delay-min').value = 2000;
  document.getElementById('set-probe-delay-max').value = 3000;
  document.getElementById('set-action-workers').value = 4;
  document.getElementById('set-timeout').value = 15000;
  document.getElementById('set-retry').value = 0;
  document.getElementById('set-auto-action').value = 'none';
  document.getElementById('set-threshold').value = 100;
  document.getElementById('set-auto-enable').checked = false;
  document.getElementById('set-priority-routing').checked = false;
  document.getElementById('set-priority-min-active').value = 2;
  document.getElementById('btn-settings-save').textContent = '创建站点';
  updateDeleteButtonState();
  setActiveTab('site');
}

function syncSiteSelect(siteId) {
  const optionHtml = siteOptions.map(site => `
    <option value="${site.siteId}">${site.siteName}${site.siteEnabled ? '' : '（停用）'}</option>
  `).join('');

  const select = document.getElementById('settings-site-select');
  select.innerHTML = optionHtml;

  const topbarSelect = document.getElementById('topbar-site-select');
  if (topbarSelect) {
    topbarSelect.innerHTML = optionHtml;
  }

  if (siteId) {
    select.value = siteId;
    if (topbarSelect) {
      topbarSelect.value = siteId;
    }
  }
}

function updateDeleteButtonState() {
  document.getElementById('btn-site-delete').disabled = createMode || siteOptions.length <= 1 || !currentSiteId;
}

function buildPayload() {
  const delayMin = Number(document.getElementById('set-probe-delay-min').value);
  const delayMax = Math.max(delayMin, Number(document.getElementById('set-probe-delay-max').value));
  const randomMin = Math.max(0, Number(document.getElementById('set-poll-random-min').value));
  const randomMax = Math.max(randomMin, Number(document.getElementById('set-poll-random-max').value));

  document.getElementById('set-probe-delay-max').value = String(delayMax);
  document.getElementById('set-poll-random-min').value = String(randomMin);
  document.getElementById('set-poll-random-max').value = String(randomMax);

  return {
    siteId: currentSiteId,
    siteName: document.getElementById('set-site-name').value.trim(),
    siteEnabled: document.getElementById('set-site-enabled').checked,
    cpaBaseUrl: document.getElementById('set-cpa-url').value.trim(),
    managementKey: document.getElementById('set-mgmt-key').value,
    provider: document.getElementById('set-provider').value,
    pollIntervalMinutes: Number(document.getElementById('set-interval').value),
    pollRandomDelayMinMinutes: randomMin,
    pollRandomDelayMaxMinutes: randomMax,
    probeWorkers: Number(document.getElementById('set-probe-workers').value),
    probeBatchDelayMinMs: delayMin,
    probeBatchDelayMaxMs: delayMax,
    actionWorkers: Number(document.getElementById('set-action-workers').value),
    timeoutMs: Number(document.getElementById('set-timeout').value),
    retryCount: Number(document.getElementById('set-retry').value),
    autoActionMode: document.getElementById('set-auto-action').value,
    usedPercentThreshold: Number(document.getElementById('set-threshold').value),
    autoEnableRecovered: document.getElementById('set-auto-enable').checked,
    priorityRoutingEnabled: document.getElementById('set-priority-routing').checked,
    priorityMinActiveCount: Number(document.getElementById('set-priority-min-active').value),
  };
}

async function loadSiteCatalog(preferredSiteId = '') {
  const data = await loadSites();
  siteOptions = data.sites || [];
  const nextSiteId = preferredSiteId || currentSiteId || data.selectedSiteId || siteOptions[0]?.siteId || '';
  syncSiteSelect(nextSiteId);
  updateDeleteButtonState();
  return nextSiteId;
}

async function loadSettings(siteId) {
  const settings = await api(`/api/settings?siteId=${encodeURIComponent(siteId)}`);
  fillForm(settings);
  if (settings.siteId) {
    setSelectedSiteId(settings.siteId);
  }
}

async function handleSiteSwitch() {
  try {
    const siteId = document.getElementById('settings-site-select').value;
    if (!siteId) {
      return;
    }

    setSelectedSiteId(siteId);
    await loadSettings(siteId);
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function saveSettings(event) {
  event.preventDefault();

  try {
    setLoading(true);
    const payload = buildPayload();

    if (createMode) {
      const created = await api('/api/settings/sites', {
        method: 'POST',
        body: JSON.stringify(payload),
      });
      currentSiteId = created.siteId;
      setSelectedSiteId(currentSiteId);
      await loadSiteCatalog(currentSiteId);
      await loadSettings(currentSiteId);
      showToast('站点已创建', 'success');
      return;
    }

    await api('/api/settings', {
      method: 'PUT',
      body: JSON.stringify(payload),
    });

    document.getElementById('set-mgmt-key').value = '';
    setSelectedSiteId(currentSiteId);
    await loadSiteCatalog(currentSiteId);

    await loadSettings(currentSiteId);
    showToast('设置已保存', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

async function deleteCurrentSite() {
  if (!currentSiteId) {
    return;
  }

  if (!window.confirm('确定删除当前站点吗？')) {
    return;
  }

  try {
    setLoading(true);
    await api(`/api/settings/sites/${encodeURIComponent(currentSiteId)}`, { method: 'DELETE' });
    const nextSiteId = await loadSiteCatalog();
    if (nextSiteId) {
      setSelectedSiteId(nextSiteId);
      await loadSettings(nextSiteId);
    }
    showToast('站点已删除', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  } finally {
    setLoading(false);
  }
}

function bindEvents() {
  document.getElementById('settings-form').addEventListener('submit', saveSettings);
  document.getElementById('settings-site-select').addEventListener('change', handleSiteSwitch);
  document.getElementById('btn-site-create').addEventListener('click', resetFormForCreate);
  document.getElementById('btn-site-delete').addEventListener('click', deleteCurrentSite);
  document.querySelectorAll('.settings-tab').forEach(button => {
    button.addEventListener('click', () => setActiveTab(button.dataset.tab));
  });
}

async function init() {
  renderPage();
  bindEvents();
  const initialSiteId = await loadSiteCatalog();
  if (initialSiteId) {
    await loadSettings(initialSiteId);
  }
}

init();
