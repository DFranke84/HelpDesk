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
    return new Promise(function (resolve) {
      var attempts = 0;
      function tryFocus() {
        var el = document.getElementById(id);
        if (el) { el.focus(); resolve(); return; }
        attempts++;
        if (attempts < 10) { setTimeout(tryFocus, 50); }
        else { resolve(); }
      }
      // rAF ensures the browser has painted the Blazor render before we attempt
      // focus; fall back to polling if the element isn't ready yet.
      requestAnimationFrame(tryFocus);
    });
  }
};

window.helpdeskAuth = {
  postJson: async function (url, body) {
    try {
      var r = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
        credentials: 'include',
        body: JSON.stringify(body)
      });
      var text = await r.text();
      var data = null;
      try { data = text ? JSON.parse(text) : null; } catch (e) { /* non-JSON response */ }
      return { ok: r.ok, status: r.status, data: data };
    } catch (e) {
      return { ok: false, status: 0, data: null };
    }
  },
  postLogout: async function (url) {
    try {
      const r = await fetch(url, {
        method: 'POST',
        credentials: 'include',
        headers: { 'X-Requested-With': 'fetch', 'X-Helpdesk-Logout-Reason': 'manual' }
      });
      return r.ok;
    } catch (e) {
      return false;
    }
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
