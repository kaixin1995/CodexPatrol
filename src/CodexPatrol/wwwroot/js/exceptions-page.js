import {
  api,
  showToast,
  escapeHtml,
  getAccountName,
  getDisplayAccount,
  refreshAccounts,
  loadExceptionNames,
} from './common.js';
import { renderLayout } from './layout.js';

let cachedAccounts = [];
let cachedExceptions = new Set();
let pickedExceptions = new Set();

function renderPage() {
  renderLayout('exceptions', '例外名单', `
    <div class="page-header">
      <h1>例外名单</h1>
      <div class="actions">
        <button class="btn btn-primary" id="btn-open-picker">添加账号</button>
      </div>
    </div>
    <div class="card">
      <p class="hint">例外名单中的账号不参与自动巡检和批量巡检</p>
      <table class="table" id="exceptions-table">
        <thead>
          <tr>
            <th>账号名</th>
            <th>账号文件</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody id="exceptions-tbody"></tbody>
      </table>
    </div>
    <div id="exception-picker" class="modal" style="display:none">
      <div class="modal-content">
        <h3>选择要加入例外名单的账号</h3>
        <div id="exception-picker-list" class="picker-list"></div>
        <div class="modal-actions">
          <button class="btn btn-primary" id="btn-confirm-add">确认添加</button>
          <button class="btn" id="btn-close-picker">取消</button>
        </div>
      </div>
    </div>
  `);
}

function renderExceptionsTable() {
  const tbody = document.getElementById('exceptions-tbody');

  if (cachedExceptions.size === 0) {
    tbody.innerHTML = '<tr><td colspan="3" style="text-align:center;color:#6b7280">暂无例外账号，点击上方“添加账号”按钮</td></tr>';
    return;
  }

  tbody.innerHTML = [...cachedExceptions].map(name => {
    const account = cachedAccounts.find(item => getAccountName(item) === name);
    const displayName = account ? getDisplayAccount(account) : name;

    return `
      <tr>
        <td title="${escapeHtml(displayName)}">${escapeHtml(displayName)}</td>
        <td title="${escapeHtml(name)}" style="color:#6b7280;font-size:12px">${escapeHtml(name)}</td>
        <td><button class="btn btn-sm btn-danger" data-action="remove-exception" data-account-name="${escapeHtml(name)}">移除</button></td>
      </tr>
    `;
  }).join('');
}

function renderPicker() {
  const list = document.getElementById('exception-picker-list');
  const availableAccounts = cachedAccounts.filter(account => !cachedExceptions.has(getAccountName(account)));

  if (availableAccounts.length === 0) {
    list.innerHTML = '<p class="hint">当前没有可添加的账号</p>';
    return;
  }

  list.innerHTML = availableAccounts.map(account => {
    const accountName = getAccountName(account);
    return `
      <div class="picker-item">
        <input type="checkbox" id="pick-${escapeHtml(accountName)}" value="${escapeHtml(accountName)}">
        <label for="pick-${escapeHtml(accountName)}" title="${escapeHtml(`${getDisplayAccount(account)} (${accountName})`)}">${escapeHtml(getDisplayAccount(account))} <span style="color:#999;font-size:11px">(${escapeHtml(accountName)})</span></label>
      </div>
    `;
  }).join('');
}

function openPicker() {
  if (cachedAccounts.length === 0) {
    showToast('账号列表为空，请先检查 CPA 连接', 'error');
    return;
  }

  pickedExceptions = new Set();
  renderPicker();
  document.getElementById('exception-picker').style.display = 'flex';
}

function closePicker() {
  document.getElementById('exception-picker').style.display = 'none';
}

async function loadExceptionsPage({ refreshAccountList = false } = {}) {
  try {
    if (refreshAccountList || cachedAccounts.length === 0) {
      cachedAccounts = await refreshAccounts();
    }

    cachedExceptions = await loadExceptionNames();
    renderExceptionsTable();
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function confirmAddExceptions() {
  try {
    if (pickedExceptions.size === 0) {
      showToast('请先选择账号', 'warning');
      return;
    }

    for (const name of pickedExceptions) {
      await api('/api/exceptions', {
        method: 'POST',
        body: JSON.stringify({ accountName: name }),
      });
    }

    closePicker();
    await loadExceptionsPage();
    showToast('例外名单已更新', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

async function removeException(name) {
  try {
    await api(`/api/exceptions/${encodeURIComponent(name)}`, { method: 'DELETE' });
    await loadExceptionsPage();
    showToast('已移除例外账号', 'success');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

function bindEvents() {
  document.getElementById('btn-open-picker').addEventListener('click', async () => {
    cachedAccounts = await refreshAccounts();
    openPicker();
  });

  document.getElementById('btn-confirm-add').addEventListener('click', confirmAddExceptions);
  document.getElementById('btn-close-picker').addEventListener('click', closePicker);
  document.getElementById('exception-picker-list').addEventListener('change', event => {
    const input = event.target.closest('input[type="checkbox"]');
    if (!input) {
      return;
    }

    if (input.checked) {
      pickedExceptions.add(input.value);
      return;
    }

    pickedExceptions.delete(input.value);
  });

  document.getElementById('exceptions-tbody').addEventListener('click', event => {
    const button = event.target.closest('[data-action="remove-exception"]');
    if (!button) {
      return;
    }

    removeException(button.dataset.accountName || '');
  });
}

async function init() {
  renderPage();
  bindEvents();
  await loadExceptionsPage({ refreshAccountList: true });
}

init();
