// Escapes text before it is placed in a grid cell (grid cells are inserted as HTML).
window.helpdeskEscapeHtml = window.helpdeskEscapeHtml || function (value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
};

/* DataTables grid for Books.dbo.Books — load after adminGrids.js */
(function () {
  var ha = window.helpdeskAdmin;
  if (!ha || typeof ha._dtBase !== 'function') return;

  ha.initBooksCatalog = function (tableId, apiUrl, permissions) {
    permissions = permissions || { canCreate: true, canRead: true, canUpdate: true, canDelete: true };
    
    ha._dtBase(tableId, apiUrl, {
      order: [[1, 'asc']],
      columns: [
        { data: 'isbn13', title: 'ISBN-13' },
        { data: 'title', title: 'Title' },
        { data: 'publisher', title: 'Publisher', defaultContent: '' },
        { data: 'year', title: 'Year' },
        {
          data: 'pages',
          title: 'Pages',
          defaultContent: '',
          render: function (d) {
            return d == null ? '—' : window.helpdeskEscapeHtml(d);
          }
        },
        { data: 'lang', title: 'Lang' },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            var actions = '<div class="hd-admin-grid-actions">';
            
            // Edit button - enabled or disabled based on permission
            if (permissions.canUpdate) {
              actions += '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'booksCatalog\',\'OpenEdit\',' +
                row.id + ')">Edit</button> ';
            } else {
              actions += '<button type="button" class="hd-btn sm hd-permission-disabled" disabled ' +
                'title="You don\'t have permission to edit books">Edit</button> ';
            }
            
            // Delete button - enabled or disabled based on permission
            if (permissions.canDelete) {
              actions += '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'booksCatalog\',\'PromptDelete\',' +
                row.id + ')">Delete</button>';
            } else {
              actions += '<button type="button" class="hd-btn sm danger hd-permission-disabled" disabled ' +
                'title="You don\'t have permission to delete books">Delete</button>';
            }
            
            actions += '</div>';
            return actions;
          }
        }
      ]
    });
  };
})();