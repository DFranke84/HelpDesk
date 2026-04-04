window.helpdeskUi = {
  togglePasswordVisibility: function (button) {
    if (!button) return;
    var wrap = button.closest('.hd-password-field');
    if (!wrap) return;
    var input = wrap.querySelector('input');
    if (!input) return;
    var show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    button.setAttribute('aria-pressed', show ? 'true' : 'false');
    button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
    if (button.classList.contains('hd-password-field__toggle--css')) {
      button.classList.toggle('hd-password-visible', show);
    } else {
      var eyeShow = button.querySelector('[data-hd-eye="show"]');
      var eyeHide = button.querySelector('[data-hd-eye="hide"]');
      if (eyeShow && eyeHide) {
        eyeShow.classList.toggle('hd-password-field__icon--hidden', show);
        eyeHide.classList.toggle('hd-password-field__icon--hidden', !show);
      }
    }
  },
  focusById: function (id) {
    const tryFocus = function () {
      const el = document.getElementById(id);
      if (el) {
        el.focus();
        return true;
      }
      return false;
    };
    setTimeout(function () { tryFocus(); }, 0);
    setTimeout(function () { tryFocus(); }, 50);
    setTimeout(function () { tryFocus(); }, 150);
  }
};

window.helpdeskAuth = {
  postJson: async function (url, body) {
    const r = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(body)
    });
    const text = await r.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch (e) { /* ignore */ }
    return { ok: r.ok, status: r.status, data: data };
  },
  postLogout: async function (url) {
    const r = await fetch(url, {
      method: 'POST',
      credentials: 'include',
      headers: { 'X-Helpdesk-Logout-Reason': 'manual' }
    });
    return r.ok;
  }
};

/** Blazor strips inline onclick after enhanced navigation / hydration — use capture so toggles always work. */
(function () {
  document.addEventListener('click', function (e) {
    var t = e.target;
    if (!t || !t.closest) return;
    var btn = t.closest('.hd-password-field__toggle');
    if (!btn) return;
    /* Blazor-managed toggles use a separate class; they handle their own click. */
    if (btn.classList.contains('hd-password-field__toggle--blazor')) return;
    e.preventDefault();
    e.stopPropagation();
    if (window.helpdeskUi && window.helpdeskUi.togglePasswordVisibility) {
      window.helpdeskUi.togglePasswordVisibility(btn);
    }
  }, true);
})();
