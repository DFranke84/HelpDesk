// Escapes text before it is placed in a grid cell (grid cells are inserted as HTML).
window.helpdeskEscapeHtml = window.helpdeskEscapeHtml || function (value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
};

window.helpdeskDiag = {
  __pending: {},

  /** Remove DataTables UI for a table id (call when leaving a Blazor page to avoid orphan wrappers). */
  destroyFull: function (tableId) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    try {
      if ($.fn.DataTable.isDataTable(el)) {
        $(el).DataTable().destroy(false);
      }
    } catch (e) {
      /* ignore */
    }
    var wrap = el.closest('.dataTables_wrapper');
    if (wrap) {
      wrap.remove();
    }
  },

  showText: function (title, text) {
    var backdrop = document.getElementById('hd-text-modal');
    if (!backdrop) {
      backdrop = document.createElement('div');
      backdrop.id = 'hd-text-modal';
      backdrop.className = 'hd-modal-backdrop';
      backdrop.innerHTML =
        '<div class="hd-modal"><h3 class="hd-modal-title"></h3><pre class="hd-modal-body" style="white-space:pre-wrap;max-height:60vh;overflow:auto"></pre>' +
        '<button type="button" class="hd-btn primary hd-modal-close">Close</button></div>';
      document.body.appendChild(backdrop);
      backdrop.style.zIndex = '400';
      backdrop.querySelector('.hd-modal-close').onclick = function () {
        backdrop.style.display = 'none';
      };
      backdrop.onclick = function (ev) {
        if (ev.target === backdrop) backdrop.style.display = 'none';
      };
      var inner = backdrop.querySelector('.hd-modal');
      if (inner) inner.onclick = function (ev) { ev.stopPropagation(); };
    }
    backdrop.querySelector('.hd-modal-title').textContent = title || 'Details';
    backdrop.querySelector('.hd-modal-body').textContent = text || '';
    backdrop.style.display = 'grid';
  },

  showPending: function (id, title) {
    var t = window.helpdeskDiag.__pending[id];
    window.helpdeskDiag.showText(title, t);
  },

  cellLink: function (title, data) {
    if (data == null || data === '') return '';
    var s = String(data);
    var maxInline = 100;
    var needsTruncate = s.length > maxInline || s.indexOf('\n') >= 0 || s.indexOf('\r') >= 0;
    var display = needsTruncate ? s.substring(0, maxInline - 1) + '…' : s;

    var id = 'p' + Math.random().toString(36).slice(2, 11);
    window.helpdeskDiag.__pending[id] = s;

    var esc = function (x) {
      var d = document.createElement('div');
      d.textContent = x;
      return d.innerHTML;
    };

    var safeId = id.replace(/[^a-z0-9]/gi, '');
    var safeTitle = esc(String(title || 'Details')).replace(/'/g, '&#39;').replace(/"/g, '&quot;');
    var onclick =
      "window.helpdeskDiag.showPending('" + safeId + "','" + safeTitle + "');return false;";

    var link = '<a href="#" class="hd-cellpop" onclick="' + onclick + '">' + esc(display) + '</a>';
    return needsTruncate ? link + ' <a href="#" class="hd-more" onclick="' + onclick + '">more</a>' : link;
  },

  initAudits: function (tableId, apiUrl) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    if ($.fn.DataTable.isDataTable(el)) {
      $(el).DataTable().destroy();
      $(el).empty();
    }
    window.helpdeskDiag.__pending = {};
    $(el).DataTable({
      ajax: { url: apiUrl, dataSrc: '', xhrFields: { withCredentials: true } },
      order: [[5, 'desc']],
      pageLength: 10,
      lengthMenu: [
        [10, 20, 50, 75, 100, -1],
        [10, 20, 50, 75, 100, 'All']
      ],
      columns: [
        { data: 'actionType', title: 'Action' },
        {
          data: 'loginId',
          title: 'Login ID',
          defaultContent: '',
          render: function (d) {
            return d == null || d === '' ? '—' : window.helpdeskEscapeHtml(d);
          }
        },
        {
          data: 'message',
          title: 'Message',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Message', d);
          }
        },
        {
          data: 'details',
          title: 'Details',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Details', d);
          }
        },
        {
          data: 'correlationId',
          title: 'Links',
          render: function (data, type, row) {
            if (!row || !row.correlationLinksAvailable || !data) return '';
            var id = String(data);
            return (
              '<a class="hd-mini" href="/admin/nlog?correlationId=' +
              encodeURIComponent(id) +
              '">NLog</a>'
            );
          }
        },
        { data: 'createdAt', title: 'Created' }
      ]
    });
  },

  initErrors: function (tableId, apiUrl) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    if ($.fn.DataTable.isDataTable(el)) {
      $(el).DataTable().destroy();
      $(el).empty();
    }
    window.helpdeskDiag.__pending = {};
    $(el).DataTable({
      ajax: { url: apiUrl, dataSrc: '', xhrFields: { withCredentials: true } },
      order: [[0, 'desc']],
      pageLength: 10,
      lengthMenu: [
        [10, 20, 50, 75, 100, -1],
        [10, 20, 50, 75, 100, 'All']
      ],
      columns: [
        { data: 'id', title: 'Id' },
        {
          data: 'message',
          title: 'Message',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Message', d);
          }
        },
        {
          data: 'stackTrace',
          title: 'Stack',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Stack', d);
          }
        },
        { data: 'severity', title: 'Severity' },
        { data: 'correlationId', title: 'Correlation' },
        { data: 'createdAt', title: 'Created' }
      ]
    });
  },

  initLogs: function (tableId, apiUrl) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    if ($.fn.DataTable.isDataTable(el)) {
      $(el).DataTable().destroy();
      $(el).empty();
    }
    window.helpdeskDiag.__pending = {};
    $(el).DataTable({
      ajax: { url: apiUrl, dataSrc: '', xhrFields: { withCredentials: true } },
      order: [[0, 'desc']],
      pageLength: 10,
      lengthMenu: [
        [10, 20, 50, 75, 100, -1],
        [10, 20, 50, 75, 100, 'All']
      ],
      columns: [
        { data: 'id', title: 'Id' },
        {
          data: 'message',
          title: 'Message',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Message', d);
          }
        },
        { data: 'level', title: 'Level' },
        { data: 'correlationId', title: 'Correlation' },
        { data: 'createdAt', title: 'Created' }
      ]
    });
  }
  ,

  initNLog: function (tableId, apiUrl) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    if ($.fn.DataTable.isDataTable(el)) {
      $(el).DataTable().destroy();
      $(el).empty();
    }
    window.helpdeskDiag.__pending = {};
    $(el).DataTable({
      ajax: { url: apiUrl, dataSrc: '', xhrFields: { withCredentials: true } },
      order: [[1, 'desc']],
      pageLength: 10,
      lengthMenu: [
        [10, 20, 50, 75, 100, -1],
        [10, 20, 50, 75, 100, 'All']
      ],
      columns: [
        { data: 'id', title: 'Id' },
        { data: 'logged', title: 'Logged' },
        { data: 'level', title: 'Level' },
        { data: 'activityCategory', title: 'Category', defaultContent: '' },
        {
          data: 'message',
          title: 'Message',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Message', d);
          }
        },
        {
          data: 'exception',
          title: 'Exception',
          render: function (d) {
            return window.helpdeskDiag.cellLink('Exception', d);
          }
        },
        { data: 'logger', title: 'Logger', defaultContent: '' },
        { data: 'correlationId', title: 'Correlation', defaultContent: '' },
        { data: 'loginId', title: 'Login ID', defaultContent: '' },
        { data: 'returnCode', title: 'Return Code', defaultContent: '' }
      ]
    });
  }
};
