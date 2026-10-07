/* boot.js — 启动入口（独立文件：页面内不再有内联脚本，可启用严格 CSP） */
(function () {
  if (window.QDApp && typeof window.QDApp.boot === 'function') window.QDApp.boot();
})();
