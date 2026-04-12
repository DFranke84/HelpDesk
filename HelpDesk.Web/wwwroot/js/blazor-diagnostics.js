/**
 * Captures Blazor/client failures for Development diagnostics.
 * Must load BEFORE blazor.web.js so console.error from Blazor is intercepted.
 * POST /api/diag/blazor-client (Development only).
 */
(function () {
    'use strict';

    var recentConsole = [];
    var maxRecent = 20;
    var postTimer = null;

    function post(payload) {
        try {
            fetch('/api/diag/blazor-client', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            }).catch(function () { /* ignore */ });
        } catch (e) { /* ignore */ }
    }

    function schedulePost(payload) {
        if (postTimer) clearTimeout(postTimer);
        postTimer = setTimeout(function () {
            postTimer = null;
            post(payload);
        }, 400);
    }

    function pushConsole(kind, line) {
        recentConsole.push({ t: new Date().toISOString(), kind: kind, line: String(line).slice(0, 8000) });
        if (recentConsole.length > maxRecent)
            recentConsole.splice(0, recentConsole.length - maxRecent);
    }

    function patchConsole(method) {
        var orig = console[method];
        console[method] = function () {
            var args = Array.prototype.slice.call(arguments);
            var line = args.map(function (a) {
                try {
                    if (a instanceof Error) return a.stack || a.message;
                    if (typeof a === 'object' && a !== null) return JSON.stringify(a);
                    return String(a);
                } catch (x) {
                    return String(a);
                }
            }).join(' ');
            pushConsole('console.' + method, line);
            orig.apply(console, args);
            if (method === 'error' || method === 'warn')
                schedulePost({ kind: 'console.' + method, line: line.slice(0, 8000), href: window.location.href });
        };
    }

    patchConsole('error');
    patchConsole('warn');

    window.addEventListener('error', function (e) {
        post({
            kind: 'window.error',
            message: e.message,
            filename: e.filename,
            lineno: e.lineno,
            colno: e.colno,
            stack: e.error && (e.error.stack || String(e.error)),
            href: window.location.href,
            recentConsole: recentConsole.slice(-12)
        });
    });

    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        var text = r == null ? String(r) : (typeof r === 'string' ? r : (r.stack || String(r)));
        post({
            kind: 'unhandledrejection',
            reason: text,
            href: window.location.href,
            recentConsole: recentConsole.slice(-12)
        });
    });

    function watchBlazorErrorUi() {
        var el = document.getElementById('blazor-error-ui');
        if (!el || el.__hdObserved) return;
        el.__hdObserved = true;
        var posted = false;

        function check() {
            if (posted) return;
            var visible = window.getComputedStyle(el).display !== 'none';
            if (visible) {
                posted = true;
                post({
                    kind: 'blazor-error-ui-visible',
                    text: (el.innerText || '').trim().slice(0, 4000),
                    href: window.location.href,
                    recentConsole: recentConsole.slice(-15)
                });
            }
        }

        var obs = new MutationObserver(function () { check(); });
        obs.observe(el, { attributes: true, attributeFilter: ['style', 'class'] });
        setInterval(check, 400);
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', watchBlazorErrorUi);
    else
        watchBlazorErrorUi();
})();
