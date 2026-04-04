window.helpdeskDataTables = {
  init: function (tableId, options) {
    var el = document.getElementById(tableId);
    if (!el || !window.jQuery || !window.jQuery.fn.DataTable) return;
    if (window.jQuery.fn.DataTable.isDataTable(el)) {
      window.jQuery(el).DataTable().destroy();
    }
    window.jQuery(el).DataTable(options);
  },
  destroy: function (tableId) {
    var el = document.getElementById(tableId);
    if (!el || !window.jQuery || !window.jQuery.fn.DataTable) return;
    if (window.jQuery.fn.DataTable.isDataTable(el)) {
      window.jQuery(el).DataTable().destroy();
    }
  }
};
