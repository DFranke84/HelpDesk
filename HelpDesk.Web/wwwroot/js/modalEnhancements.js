/* Focus fields in admin/catalog modals and keep keyboard events from reaching the page behind (e.g. DataTables). */
(function () {
  var focusTimer;

  function focusFirstModalField() {
    var backdrops = document.querySelectorAll('.hd-modal-backdrop');
    if (backdrops.length === 0) return;
    var b = backdrops[backdrops.length - 1];
    var m = b.querySelector('.hd-modal');
    if (!m) return;
    var el = m.querySelector(
      'input:not([type="hidden"]):not([disabled]):not([readonly]), select:not([disabled]), textarea:not([disabled]):not([readonly])'
    );
    if (el) {
      try {
        el.focus();
      } catch (e) {
        /* ignore */
      }
    }
  }

  function scheduleFocus() {
    clearTimeout(focusTimer);
    focusTimer = setTimeout(focusFirstModalField, 0);
  }

  var mo = new MutationObserver(function () {
    scheduleFocus();
  });
  if (document.body) {
    mo.observe(document.body, { childList: true, subtree: true });
  }

  /* Bubble phase only — runs after the focused element handles the key (avoids blocking input). */
  document.addEventListener('keydown', function (e) {
    if (!e.target || !e.target.closest) return;
    if (e.target.closest('.hd-modal')) {
      e.stopPropagation();
    }
  });
})();
