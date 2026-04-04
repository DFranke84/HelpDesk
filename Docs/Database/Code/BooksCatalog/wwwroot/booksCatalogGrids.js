/* Template: copy to HelpDesk.Web/wwwroot/js/booksCatalogGrids.js — load after adminGrids.js */
(function () {
  var ha = window.helpdeskAdmin;
  if (!ha || typeof ha._dtBase !== 'function') return;

  ha.initBooksCatalog = function (tableId, apiUrl) {
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
            return d == null ? '—' : d;
          }
        },
        { data: 'lang', title: 'Lang' },
        {
          data: null,
          orderable: false,
          title: '',
          className: 'hd-dt-actions',
          render: function (data, type, row) {
            return (
              '<div class="hd-admin-grid-actions">' +
              '<button type="button" class="hd-btn sm" onclick="window.helpdeskAdmin.invoke(\'booksCatalog\',\'OpenEdit\',' +
              row.id +
              ')">Edit</button> ' +
              '<button type="button" class="hd-btn sm danger" onclick="window.helpdeskAdmin.invoke(\'booksCatalog\',\'PromptDelete\',' +
              row.id +
              ')">Delete</button></div>'
            );
          }
        }
      ]
    });
  };
})();
