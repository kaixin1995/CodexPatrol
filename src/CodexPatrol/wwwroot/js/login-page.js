import { api, showToast, setLoading } from './common.js';

function renderPage() {
  document.body.innerHTML = `
    <div class="auth-shell">
      <section class="auth-brand">
        <div class="auth-brand-content">
          <span class="auth-word">CODEX</span>
          <span class="auth-word auth-word-muted">PATROL</span>
          <span class="auth-word auth-word-faint">LOGIN</span>
        </div>
      </section>
      <section class="auth-panel">
        <div class="auth-card">
          <div class="auth-card-header">
            <span class="auth-eyebrow">LOCAL ACCESS</span>
            <h1>登录 Codex Patrol</h1>
            <p>请输入本地访问密码后继续进入管理面板。</p>
          </div>
          <form id="login-form" class="auth-form">
            <div class="form-group">
              <label for="login-password">登录密码</label>
              <input id="login-password" type="password" autocomplete="current-password" placeholder="请输入登录密码">
            </div>
            <button type="submit" class="btn btn-primary auth-submit">登录</button>
          </form>
          <div id="login-error" class="auth-error" style="display:none"></div>
        </div>
      </section>
    </div>
    <div id="toast-container"></div>
  `;
}

function setError(message) {
  const errorBox = document.getElementById('login-error');
  if (!errorBox) return;
  if (!message) {
    errorBox.style.display = 'none';
    errorBox.textContent = '';
    return;
  }
  errorBox.style.display = 'block';
  errorBox.textContent = message;
}

async function loadStatus() {
  const status = await api('/api/auth/status');
  if (status.setupRequired) {
    window.location.replace('/setup.html');
    return false;
  }
  if (status.authenticated) {
    window.location.replace('/dashboard.html');
    return false;
  }
  return true;
}

async function submitLogin(event) {
  event.preventDefault();
  const passwordInput = document.getElementById('login-password');
  const password = passwordInput?.value?.trim() || '';
  if (!password) {
    setError('请输入登录密码');
    return;
  }

  try {
    setError('');
    setLoading(true);
    await api('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ password }),
    });
    showToast('登录成功', 'success');
    window.location.replace('/dashboard.html');
  } catch (error) {
    const message = error instanceof Error ? error.message : '登录失败';
    setError(message);
    showToast(message, 'error');
  } finally {
    setLoading(false);
  }
}

async function init() {
  renderPage();
  const canContinue = await loadStatus();
  if (!canContinue) {
    return;
  }

  document.getElementById('login-form')?.addEventListener('submit', submitLogin);
}

init();
