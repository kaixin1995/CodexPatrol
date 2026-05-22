// 统一处理接口返回的大小写，页面脚本只使用 camelCase 字段。
const selectedSiteStorageKey = 'codexPatrol.selectedSiteId';

function normalizeKey(key) {
  const mergedKey = key.replace(/[_-]+([a-zA-Z0-9])/g, (_, char) => char.toUpperCase());
  return mergedKey.charAt(0).toLowerCase() + mergedKey.slice(1);
}

function normalizeData(value) {
  if (Array.isArray(value)) {
    return value.map(normalizeData);
  }

  if (!value || typeof value !== 'object') {
    return value;
  }

  const normalized = {};
  for (const [key, item] of Object.entries(value)) {
    normalized[normalizeKey(key)] = normalizeData(item);
  }
  return normalized;
}

export function getSelectedSiteId() {
  try {
    return localStorage.getItem(selectedSiteStorageKey) || '';
  } catch {
    return '';
  }
}

export function setSelectedSiteId(siteId) {
  try {
    if (!siteId) {
      localStorage.removeItem(selectedSiteStorageKey);
      return;
    }

    localStorage.setItem(selectedSiteStorageKey, siteId);
  } catch {
  }
}

function appendSiteId(url) {
  if (!url.startsWith('/api/') || url.startsWith('/api/auth')) {
    return url;
  }

  const selectedSiteId = getSelectedSiteId();
  if (!selectedSiteId) {
    return url;
  }

  const requestUrl = new URL(url, window.location.origin);
  if (!requestUrl.searchParams.has('siteId')) {
    requestUrl.searchParams.set('siteId', selectedSiteId);
  }

  return `${requestUrl.pathname}${requestUrl.search}${requestUrl.hash}`;
}

export async function api(url, options = {}) {
  const headers = {
    'Content-Type': 'application/json',
    ...(options.headers || {}),
  };

  const response = await fetch(appendSiteId(url), {
    ...options,
    headers,
  });

  const contentType = response.headers.get('content-type') || '';
  const isJson = contentType.includes('json');

  if (!response.ok) {
    let message = `HTTP ${response.status}`;

    try {
      if (isJson) {
        const error = normalizeData(await response.json());
        message = error.error || error.title || message;
      } else {
        const text = await response.text();
        message = text || message;
      }
    } catch {
    }

    // 登录失效后直接回到登录页，避免页面停留在受保护状态。
    if (!url.startsWith('/api/auth')) {
      if (response.status === 401) {
        window.location.replace('/login.html');
      }
      if (response.status === 403) {
        window.location.replace('/setup.html');
      }
    }

    throw new Error(message);
  }

  if (response.status === 204) {
    return null;
  }

  if (!isJson) {
    const text = await response.text();
    return text || null;
  }

  return normalizeData(await response.json());
}

export async function loadSites() {
  const data = await api('/api/settings/sites');
  const sites = data?.sites || [];
  const selectedSiteId = data?.selectedSiteId || sites[0]?.siteId || '';
  if (selectedSiteId) {
    setSelectedSiteId(selectedSiteId);
  }
  return {
    selectedSiteId,
    sites,
  };
}

export function showToast(message, type = 'success') {
  const container = document.getElementById('toast-container');
  if (!container) {
    return;
  }

  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.textContent = message;
  container.appendChild(toast);
  setTimeout(() => toast.remove(), 3000);
}

export function setLoading(loading, scope = document) {
  scope.querySelectorAll('.btn').forEach(button => {
    if (loading) {
      button.setAttribute('data-was-disabled', String(button.disabled));
      button.disabled = true;
      return;
    }

    button.disabled = button.getAttribute('data-was-disabled') === 'true';
    button.removeAttribute('data-was-disabled');
  });
}

export function formatDate(value) {
  if (!value || value === '0001-01-01T00:00:00') {
    return '--';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '--';
  }

  return date.toLocaleString('zh-CN');
}

export function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text ?? '';
  return div.innerHTML;
}

export function getAccountName(account) {
  return account?.name || account?.authIndex || '';
}

export function getAccountDisabled(account) {
  return account?.disabled ?? false;
}

export function getDisplayAccount(account, quota = null) {
  return quota?.displayAccount || account?.account || account?.email || account?.label || getAccountName(account) || '-';
}

export async function refreshAccounts() {
  return await api('/api/accounts/refresh', { method: 'POST' }) || [];
}

export async function getAccounts() {
  return await api('/api/accounts') || [];
}

export async function loadExceptionNames() {
  const data = await api('/api/exceptions');
  return new Set(data?.exceptions || []);
}

export async function getRuntimeProgress() {
  return await api('/api/runtime/progress');
}

export async function getOperationLogs(limit = 200) {
  return await api(`/api/runtime/logs?limit=${encodeURIComponent(limit)}`) || [];
}
