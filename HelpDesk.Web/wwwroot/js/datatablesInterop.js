if (window.jQuery && window.jQuery.fn.DataTable) {
  var DT = window.jQuery.fn.DataTable;
  // DataTables 1.x uses DataTable.defaults.column (not ext.defaults.column).
  if (DT.defaults && DT.defaults.column && DT.render && DT.render.text) {
    DT.defaults.column.render = DT.render.text();
  }
}

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
