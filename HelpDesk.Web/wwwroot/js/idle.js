window.helpdeskIdle = (function () {
  var last = Date.now();
  var idleMs = 60 * 60 * 1000;
  var intervalId = null;
  var loggedOut = false;

  function bump() { last = Date.now(); }

  var events = ['mousemove', 'keydown', 'click', 'scroll', 'touchstart'];

  events.forEach(function (ev) {
    document.addEventListener(ev, bump, { passive: true });
  });

  return {
    start: function (dotNetHelper, logoutUrl) {
      if (intervalId !== null) clearInterval(intervalId);
      loggedOut = false;

      intervalId = setInterval(async function () {
        if (loggedOut) return;
        if (Date.now() - last < idleMs) return;

        loggedOut = true;
        clearInterval(intervalId);
        intervalId = null;

        try {
          await fetch(logoutUrl, {
            method: 'POST',
            credentials: 'include',
            headers: { 'X-Helpdesk-Logout-Reason': 'idle' }
          });
        } catch (e) { /* network failure is acceptable */ }

        try {
          await dotNetHelper.invokeMethodAsync('OnIdleLogout');
        } catch (e) { /* circuit may already be disconnected */ }
      }, 30000);
    },

    stop: function () {
      if (intervalId !== null) {
        clearInterval(intervalId);
        intervalId = null;
      }
      events.forEach(function (ev) {
        document.removeEventListener(ev, bump, { passive: true });
      });
    }
  };
})();
