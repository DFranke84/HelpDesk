// Escapes text before it is placed in a grid cell (grid cells are inserted as HTML).
window.helpdeskEscapeHtml = window.helpdeskEscapeHtml || function (value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
};

window.helpdeskAdmin = {
  handlers: {},

  __jsonViewById: {},

  queueJsonForView: function (data) {
    var id = 'jv' + Math.random().toString(36).slice(2, 11) + Date.now().toString(36);
    var s = typeof data === 'string' ? data : JSON.stringify(data);
    this.__jsonViewById[id] = s;
    return id;
  },

  viewJsonById: function (id) {
    var raw = this.__jsonViewById[id];
    if (raw === undefined) return;
    /* Keep token so the same "View changes" button works every click (cell HTML is static). */
    this.viewJson(raw);
  },

  needsInit: function (tableId) {
    var el = document.getElementById(tableId);
    if (!el) return false;
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return false;
    return !$.fn.DataTable.isDataTable(el);
  },

  register: function (key, dotNetRef) {
    this.handlers[key] = dotNetRef;
  },

  unregister: function (key) {
    delete this.handlers[key];
  },

  invoke: function (key, method) {
    var h = this.handlers[key];
    if (!h) return Promise.resolve();
    var args = Array.prototype.slice.call(arguments, 2);
    return h.invokeMethodAsync.apply(h, [method].concat(args));
  },

  viewJson: function (json) {
    var text = json || '';
    try {
      text = JSON.stringify(JSON.parse(json), null, 2);
    } catch (e) {
      /* keep raw */
    }
    if (window.helpdeskDiag && window.helpdeskDiag.showText) {
      window.helpdeskDiag.showText('Change details', text);
    } else {
      alert(text);
    }
  },

  reloadTable: function (tableId) {
    var $ = window.jQuery;
    var el = document.getElementById(tableId);
    if (!el || !$ || !$.fn.DataTable || !$.fn.DataTable.isDataTable(el)) return;
    $(el).DataTable().ajax.reload(null, false);
  },

  setAjaxUrlAndReload: function (tableId, url) {
    var $ = window.jQuery;
    var el = document.getElementById(tableId);
    if (!el || !$ || !$.fn.DataTable || !$.fn.DataTable.isDataTable(el)) return;
    $(el).DataTable().ajax.url(url).load();
  },

  destroyTable: function (tableId) {
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
    } else {
      el.innerHTML = '';
    }
  },

  _dtBase: function (tableId, apiUrl, extra) {
    var $ = window.jQuery;
    if (!$ || !$.fn.DataTable) return;
    var el = document.getElementById(tableId);
    if (!el) return;
    if ($.fn.DataTable.isDataTable(el)) {
      $(el).DataTable().destroy();
      $(el).empty();
    }
    var opts = {
      ajax: { url: apiUrl, dataSrc: '', xhrFields: { withCredentials: true } },
      pageLength: 10,
      lengthMenu: [
        [10, 20, 50, 75, 100, -1],
        [10, 20, 50, 75, 100, 'All']
      ]
    };
    for (var k in extra) {
      opts[k] = extra[k];
    }
    $(el).DataTable(opts);
  },

  initApplications: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[1, 'asc']],
      columns: [
        { data: 'displayOrder', title: 'Order' },
        { data: 'name', title: 'Name' },
        {
          data: 'externalKey',
          title: 'External key',
          defaultContent: '',
          render: function (d) {
            return d == null || d === '' ? '—' : window.helpdeskEscapeHtml(d);
          }
        },
        {
          data: 'usesExternalDatabase',
          title: 'Ext. DB',
          render: function (d) {
            return d ? 'Yes' : 'No';
          }
        },
        {
          data: 'isActive',
          title: 'Active',
          render: function (d) {
            return d ? 'Yes' : 'No';
          }
        },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'applications\',\'OpenEdit\',' +
              row.id +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'applications\',\'PromptDelete\',' +
              row.id +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initPages: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[1, 'asc']],
      columns: [
        { data: 'applicationName', title: 'Application' },
        { data: 'name', title: 'Name' },
        {
          data: null,
          title: 'Navigation',
          render: function (data, type, row) {
            if (row.url) {
              return '<code>' + row.url + '</code>' + (row.openInNewTab ? ' <span class="muted">(new tab)</span>' : ' <span class="muted">(embedded)</span>');
            } else if (row.route) {
              return '<code>' + row.route + '</code> <span class="muted">(route)</span>';
            } else {
              return '<span class="muted">None</span>';
            }
          }
        },
        { data: 'displayOrder', title: 'Order' },
        {
          data: 'isActive',
          title: 'Active',
          render: function (d) {
            return d ? 'Yes' : 'No';
          }
        },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'pages\',\'OpenEdit\',' +
              row.id +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'pages\',\'PromptDelete\',' +
              row.id +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initGroups: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[0, 'asc']],
      columns: [
        { data: 'groupName', title: 'Name' },
        {
          data: 'description',
          title: 'Description',
          defaultContent: '',
          render: function (d) {
            return d == null || d === '' ? '—' : window.helpdeskEscapeHtml(d);
          }
        },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'groups\',\'OpenEdit\',' +
              row.id +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'groups\',\'PromptDelete\',' +
              row.id +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initApplicationGroups: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[0, 'asc']],
      columns: [
        { data: 'applicationName', title: 'Application' },
        { data: 'groupName', title: 'Group' },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'applicationGroups\',\'OpenEdit\',' +
              row.deskApplicationId +
              ',' +
              row.deskGroupId +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'applicationGroups\',\'PromptDelete\',' +
              row.deskApplicationId +
              ',' +
              row.deskGroupId +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initUserGroups: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[3, 'desc']],
      columns: [
        { data: 'loginId', title: 'Login ID' },
        { data: 'displayName', title: 'Name' },
        { data: 'groupName', title: 'Group' },
        { data: 'assignedAt', title: 'Assigned' },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'userGroups\',\'OpenEdit\',' +
              row.userId +
              ',' +
              row.groupId +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'userGroups\',\'PromptDelete\',' +
              row.userId +
              ',' +
              row.groupId +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initUsers: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[3, 'asc']],
      columns: [
        {
          data: 'userType',
          title: 'Type',
          render: function (d) {
            return d === 'Group' 
              ? '<span class="hd-badge hd-badge-group">Group</span>' 
              : '<span class="hd-badge hd-badge-user">User</span>';
          }
        },
        { data: 'name', title: 'Name' },
        { data: 'loginId', title: 'Login ID' },
        { 
          data: 'email', 
          title: 'Email', 
          defaultContent: '',
          render: function (d) {
            return d == null || d === '' ? '—' : window.helpdeskEscapeHtml(d);
          }
        },
        { data: 'role', title: 'Role' },
        {
          data: 'isActive',
          title: 'Active',
          render: function (d) {
            return d ? 'Yes' : 'No';
          }
        },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'users\',\'OpenEdit\',' +
              row.id +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'users\',\'PromptDelete\',' +
              row.id +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  },

  initChangeHistory: function (tableId, apiUrl) {
    this._dtBase(tableId, apiUrl, {
      order: [[4, 'desc']],
      columns: [
        { data: 'entityType', title: 'Type' },
        { data: 'entityLabel', title: 'Entity' },
        { data: 'action', title: 'Action' },
        { data: 'changedBy', title: 'By' },
        { data: 'changedAt', title: 'When' },
        {
          data: 'jsonChanges',
          title: '',
          orderable: false,
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            if (!data) return '';
            var id = window.helpdeskAdmin.queueJsonForView(data);
            return (
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.viewJsonById(\'' +
              id +
              '\')">View changes</button>'
            );
          }
        }
      ]
    });
  }
};
