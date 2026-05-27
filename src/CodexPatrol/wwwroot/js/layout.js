import { api, escapeHtml, getAppInfo, getSelectedSiteId, loadSites, setSelectedSiteId } from './common.js';

// 导航图标尽量贴近原项目的浅色侧栏风格。
const navItems = [
  {
    key: 'dashboard',
    href: '/dashboard.html',
    label: '仪表盘',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="1.5"></rect><rect x="14" y="3" width="7" height="7" rx="1.5"></rect><rect x="14" y="14" width="7" height="7" rx="1.5"></rect><rect x="3" y="14" width="7" height="7" rx="1.5"></rect></svg>',
  },
  {
    key: 'quotas',
    href: '/quotas.html',
    label: '额度管理',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20"></path><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7H14.5a3.5 3.5 0 0 1 0 7H7"></path></svg>',
  },
  {
    key: 'inspection',
    href: '/inspection.html',
    label: '巡检管理',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"></path><path d="m7 14 3-3 3 2 5-6"></path></svg>',
  },
  {
    key: 'operations',
    href: '/operations.html',
    label: '操作日志',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 6h13"></path><path d="M8 12h13"></path><path d="M8 18h13"></path><path d="M3 6h.01"></path><path d="M3 12h.01"></path><path d="M3 18h.01"></path></svg>',
  },
  {
    key: 'priority',
    href: '/priority.html',
    label: '优先级路由',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14"></path><path d="m19 12-7 7-7-7"></path></svg>',
  },
  {
    key: 'exceptions',
    href: '/exceptions.html',
    label: '例外名单',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2 4 6v6c0 5 3.4 9.4 8 10 4.6-.6 8-5 8-10V6l-8-4Z"></path><path d="M9 12h6"></path></svg>',
  },
  {
    key: 'settings',
    href: '/settings.html',
    label: '系统设置',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v3"></path><path d="M12 18v3"></path><path d="M3 12h3"></path><path d="M18 12h3"></path><path d="m5.64 5.64 2.12 2.12"></path><path d="m16.24 16.24 2.12 2.12"></path><path d="m5.64 18.36 2.12-2.12"></path><path d="m16.24 7.76 2.12-2.12"></path><circle cx="12" cy="12" r="3.5"></circle></svg>',
  },
];

const sidebarCollapseStorageKey = 'codexPatrol.sidebarCollapsed';

function isSidebarCollapsed() {
  try {
    return localStorage.getItem(sidebarCollapseStorageKey) === '1';
  } catch {
    return false;
  }
}

function setSidebarCollapsed(collapsed) {
  try {
    localStorage.setItem(sidebarCollapseStorageKey, collapsed ? '1' : '0');
  } catch {
  }
}

function getSidebarToggleIcon(collapsed) {
  return collapsed
    ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m9 6 6 6-6 6"></path></svg>'
    : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m15 6-6 6 6 6"></path></svg>';
}

async function bindSiteSwitcher() {
  const siteSelect = document.getElementById('topbar-site-select');
  if (!siteSelect) {
    return;
  }

  try {
    const { selectedSiteId, sites } = await loadSites();
    if (!sites.length) {
      siteSelect.innerHTML = '<option value="">暂无站点</option>';
      siteSelect.disabled = true;
      return;
    }

    siteSelect.innerHTML = sites.map(site => `
      <option value="${escapeHtml(site.siteId)}">${escapeHtml(site.siteName)}${site.siteEnabled ? '' : '（停用）'}</option>
    `).join('');

    const currentSiteId = getSelectedSiteId() || selectedSiteId || sites[0].siteId;
    if (currentSiteId) {
      setSelectedSiteId(currentSiteId);
      siteSelect.value = currentSiteId;
    }
  } catch {
    siteSelect.innerHTML = '<option value="">站点加载失败</option>';
    siteSelect.disabled = true;
    return;
  }

  siteSelect.addEventListener('change', () => {
    setSelectedSiteId(siteSelect.value);
    window.location.reload();
  });
}

async function bindAppVersion() {
  const versionValue = document.getElementById('sidebar-app-version');
  const versionBlock = document.getElementById('sidebar-version');
  if (!versionValue || !versionBlock) {
    return;
  }

  try {
    const appInfo = await getAppInfo();
    const version = appInfo?.version || '--';
    versionValue.textContent = version;
    versionBlock.title = `当前版本 ${version}`;
  } catch {
    versionValue.textContent = '--';
    versionBlock.title = '版本信息加载失败';
  }
}

export function renderLayout(activePage, title, contentHtml) {
  document.title = `Codex Patrol - ${title}`;
  const collapsed = isSidebarCollapsed();
  document.body.innerHTML = `
    <div class="app-shell ${collapsed ? 'sidebar-collapsed' : ''}">
      <nav class="sidebar">
        <div class="sidebar-toolbar">
          <button class="sidebar-toggle" id="btn-sidebar-toggle" type="button" title="${collapsed ? '展开导航' : '收起导航'}" aria-label="${collapsed ? '展开导航' : '收起导航'}">
            ${getSidebarToggleIcon(collapsed)}
          </button>
        </div>
        <div class="sidebar-brand">
          <div class="sidebar-brand-mark">CP</div>
          <div class="sidebar-brand-copy">
            <h2>Codex Patrol</h2>
            <p>Management Center</p>
          </div>
        </div>
        <ul class="nav-menu">
          ${navItems.map(item => `
            <li>
              <a href="${item.href}" class="nav-link ${item.key === activePage ? 'active' : ''}" title="${item.label}">
                <span class="nav-icon" aria-hidden="true">${item.icon}</span>
                <span class="nav-label">${item.label}</span>
              </a>
            </li>
          `).join('')}
        </ul>
        <div class="sidebar-version" id="sidebar-version" title="版本信息加载中">
          <span class="sidebar-version-label">版本号</span>
          <span class="sidebar-version-value" id="sidebar-app-version">--</span>
          <a class="sidebar-version-link" href="https://github.com/kaixin1995/CodexPatrol" target="_blank" rel="noreferrer" title="https://github.com/kaixin1995/CodexPatrol">
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.5-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82A7.65 7.65 0 0 1 8 4.27c.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z"></path>
            </svg>
            <span>GitHub</span>
          </a>
        </div>
      </nav>

      <div class="shell-main">
        <header class="topbar">
          <div class="topbar-copy">
            <span class="topbar-eyebrow">CODEX PATROL</span>
          </div>
          <div class="topbar-actions">
            <label class="topbar-site-switcher" for="topbar-site-select">
              <span class="topbar-site-label">站点</span>
              <select id="topbar-site-select" class="topbar-site-select">
                <option value="">加载中...</option>
              </select>
            </label>
            <button class="btn btn-ghost topbar-action-button" id="btn-logout">退出登录</button>
          </div>
        </header>

        <main class="content">
          <div class="page-container">
            ${contentHtml}
          </div>
        </main>
      </div>
    </div>

    <div id="toast-container"></div>
  `;

  const appShell = document.querySelector('.app-shell');
  const sidebarToggleButton = document.getElementById('btn-sidebar-toggle');
  const logoutButton = document.getElementById('btn-logout');

  // 记住用户的折叠偏好，切页后继续保持一致。
  sidebarToggleButton?.addEventListener('click', () => {
    if (!appShell) {
      return;
    }

    const nextCollapsed = appShell.classList.toggle('sidebar-collapsed');
    setSidebarCollapsed(nextCollapsed);
    sidebarToggleButton.innerHTML = getSidebarToggleIcon(nextCollapsed);
    sidebarToggleButton.title = nextCollapsed ? '展开导航' : '收起导航';
    sidebarToggleButton.setAttribute('aria-label', nextCollapsed ? '展开导航' : '收起导航');
  });

  logoutButton?.addEventListener('click', async () => {
    try {
      await fetch('/api/auth/logout', { method: 'POST' });
    } finally {
      window.location.replace('/login.html');
    }
  });

  void bindSiteSwitcher();
  void bindAppVersion();
}
