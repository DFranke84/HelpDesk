window.helpdeskIdle = (function () {
  let last = Date.now();
  const idleMs = 60 * 60 * 1000;

  function bump() { last = Date.now(); }

  ['mousemove', 'keydown', 'click', 'scroll', 'touchstart'].forEach(function (ev) {
    document.addEventListener(ev, bump, { passive: true });
  });

  return {
    start: function (dotNetHelper, logoutUrl) {
      setInterval(async function () {
        if (Date.now() - last >= idleMs) {
          try {
            await fetch(logoutUrl, {
              method: 'POST',
              credentials: 'include',
              headers: { 'X-Helpdesk-Logout-Reason': 'idle' }
            });
          } catch (e) { /* ignore */ }
          await dotNetHelper.invokeMethodAsync('OnIdleLogout');
        }
      }, 30000);
    }
  };
})();
