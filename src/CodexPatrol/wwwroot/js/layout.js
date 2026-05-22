import { api, escapeHtml, getSelectedSiteId, loadSites, setSelectedSiteId } from './common.js';

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
}
