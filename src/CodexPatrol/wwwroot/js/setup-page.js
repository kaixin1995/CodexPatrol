import { api, showToast, setLoading } from './common.js';

function renderPage() {
  document.body.innerHTML = `
    <div class="auth-shell">
      <section class="auth-brand">
        <div class="auth-brand-content">
          <span class="auth-word">CODEX</span>
          <span class="auth-word auth-word-muted">PATROL</span>
          <span class="auth-word auth-word-faint">SETUP</span>
        </div>
      </section>
      <section class="auth-panel">
        <div class="auth-card auth-card-wide">
          <div class="auth-card-header">
            <span class="auth-eyebrow">FIRST SETUP</span>
            <h1>设置登录密码</h1>
            <p>当前系统尚未配置访问密码，请先设置一个本地登录密码。</p>
          </div>
          <form id="setup-form" class="auth-form">
            <div class="form-group">
              <label for="setup-password">登录密码</label>
              <input id="setup-password" type="password" autocomplete="new-password" placeholder="至少 8 位密码">
            </div>
            <div class="form-group">
              <label for="setup-confirm-password">确认密码</label>
              <input id="setup-confirm-password" type="password" autocomplete="new-password" placeholder="请再次输入密码">
            </div>
            <button type="submit" class="btn btn-primary auth-submit">保存并进入系统</button>
          </form>
          <div id="setup-error" class="auth-error" style="display:none"></div>
        </div>
      </section>
    </div>
    <div id="toast-container"></div>
  `;
}

function setError(message) {
  const errorBox = document.getElementById('setup-error');
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
  if (status.authenticated) {
    window.location.replace('/dashboard.html');
    return false;
  }
  if (status.passwordConfigured) {
    window.location.replace('/login.html');
    return false;
  }
  return true;
}

async function submitSetup(event) {
  event.preventDefault();
  const password = document.getElementById('setup-password')?.value?.trim() || '';
  const confirmPassword = document.getElementById('setup-confirm-password')?.value?.trim() || '';

  if (!password || !confirmPassword) {
    setError('请完整填写密码和确认密码');
    return;
  }

  if (password !== confirmPassword) {
    setError('两次输入的密码不一致');
    return;
  }

  try {
    setError('');
    setLoading(true);
    await api('/api/auth/setup', {
      method: 'POST',
      body: JSON.stringify({ password, confirmPassword }),
    });
    showToast('登录密码设置成功', 'success');
    window.location.replace('/dashboard.html');
  } catch (error) {
    const message = error instanceof Error ? error.message : '设置密码失败';
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

  document.getElementById('setup-form')?.addEventListener('submit', submitSetup);
}

init();
