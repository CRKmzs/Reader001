/* ============================================================
   storage.js — 书籍存 IndexedDB，设置存 localStorage（含内存兜底）
   ============================================================ */
window.QDStore = (function () {
  'use strict';

  var DB_NAME = 'qidian-txt-reader';
  var DB_VER = 1;
  var SETTINGS_KEY = 'qdr.settings.v1';
  var dbPromise = null;
  var idbBroken = false;
  var memBooks = {};
  var memTexts = {};
  var settingsCache = null;

  function defaultSettings() {
    return {
      theme: 'default',
      font: 'song',
      fontSize: 20,
      lineHeight: 1.8,
      paraSpace: 0.4,
      width: 900,
      turn: 'scroll',
      dark: false,
      sort: 'recent',
      encoding: 'auto'
    };
  }

  function merge(a, b) {
    var out = {}, k;
    for (k in a) if (Object.prototype.hasOwnProperty.call(a, k)) out[k] = a[k];
    for (k in b) if (Object.prototype.hasOwnProperty.call(b, k)) out[k] = b[k];
    return out;
  }

  function openDB() {
    if (dbPromise) return dbPromise;
    dbPromise = new Promise(function (resolve, reject) {
      if (typeof indexedDB === 'undefined' || !indexedDB) {
        reject(new Error('当前环境不支持 IndexedDB'));
        return;
      }
      var req;
      try { req = indexedDB.open(DB_NAME, DB_VER); } catch (e) { reject(e); return; }
      req.onupgradeneeded = function () {
        var db = req.result;
        if (!db.objectStoreNames.contains('books')) db.createObjectStore('books', { keyPath: 'id' });
        if (!db.objectStoreNames.contains('texts')) db.createObjectStore('texts', { keyPath: 'id' });
      };
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error || new Error('IndexedDB 打开失败')); };
      req.onblocked = function () { reject(new Error('数据库被其他标签页占用')); };
    });
    dbPromise.catch(function () { idbBroken = true; });
    return dbPromise;
  }

  function run(storeName, mode, fn) {
    return openDB().then(function (db) {
      return new Promise(function (resolve, reject) {
        var tx, req;
        try {
          tx = db.transaction(storeName, mode);
          req = fn(tx.objectStore(storeName));
        } catch (e) { reject(e); return; }
        tx.oncomplete = function () { resolve(req && typeof req === 'object' && 'result' in req ? req.result : undefined); };
        tx.onerror = function () { reject(tx.error); };
        tx.onabort = function () { reject(tx.error); };
      });
    });
  }

  function fallback(fn) {
    return function () {
      try { return Promise.resolve(fn()); } catch (e) { return Promise.reject(e); }
    };
  }

  function safe(fn) {
    return fn().catch(function (err) {
      idbBroken = true;
      throw err;
    });
  }

  /* ---------------- 书籍 ---------------- */
  function listBooks() {
    return safe(function () {
      return run('books', 'readonly', function (s) { return s.getAll(); });
    }).catch(fallback(function () {
      return Object.keys(memBooks).map(function (k) { return memBooks[k]; });
    }));
  }

  function getBook(id) {
    return safe(function () {
      return run('books', 'readonly', function (s) { return s.get(id); });
    }).catch(fallback(function () { return memBooks[id] || null; }));
  }

  function getText(id) {
    return safe(function () {
      return run('texts', 'readonly', function (s) { return s.get(id); });
    }).then(function (rec) { return rec ? rec.text : null; })
      .catch(fallback(function () { return memTexts[id] || null; }));
  }

  function putBook(meta, text) {
    return safe(function () {
      return openDB().then(function (db) {
        return new Promise(function (resolve, reject) {
          var tx = db.transaction(['books', 'texts'], 'readwrite');
          tx.objectStore('books').put(meta);
          tx.objectStore('texts').put({ id: meta.id, text: text });
          tx.oncomplete = function () { resolve(meta); };
          tx.onerror = function () { reject(tx.error); };
          tx.onabort = function () { reject(tx.error); };
        });
      });
    }).catch(fallback(function () {
      memBooks[meta.id] = meta;
      memTexts[meta.id] = text;
      return meta;
    }));
  }

  function updateBook(id, patch) {
    return getBook(id).then(function (book) {
      if (!book) return null;
      var next = merge(book, patch);
      return safe(function () {
        return run('books', 'readwrite', function (s) { return s.put(next); });
      }).then(function () { return next; })
        .catch(fallback(function () { memBooks[id] = next; return next; }));
    });
  }

  function deleteBook(id) {
    return safe(function () {
      return openDB().then(function (db) {
        return new Promise(function (resolve, reject) {
          var tx = db.transaction(['books', 'texts'], 'readwrite');
          tx.objectStore('books').delete(id);
          tx.objectStore('texts').delete(id);
          tx.oncomplete = function () { resolve(true); };
          tx.onerror = function () { reject(tx.error); };
          tx.onabort = function () { reject(tx.error); };
        });
      });
    }).catch(fallback(function () {
      delete memBooks[id];
      delete memTexts[id];
      return true;
    }));
  }

  /* ---------------- 设置 ---------------- */
  function getSettings() {
    if (settingsCache) return settingsCache;
    var stored = null;
    try {
      var raw = localStorage.getItem(SETTINGS_KEY);
      if (raw) stored = JSON.parse(raw);
    } catch (e) { stored = null; }
    settingsCache = merge(defaultSettings(), stored || {});
    return settingsCache;
  }

  function saveSettings(patch) {
    settingsCache = merge(getSettings(), patch || {});
    try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settingsCache)); } catch (e) { /* 无痕模式等 */ }
    return settingsCache;
  }

  /* ---------------- 其它 ---------------- */
  function isPersistent() { return !idbBroken; }

  function estimate() {
    if (navigator.storage && navigator.storage.estimate) {
      return navigator.storage.estimate().catch(function () { return null; });
    }
    return Promise.resolve(null);
  }

  return {
    defaultSettings: defaultSettings,
    listBooks: listBooks,
    getBook: getBook,
    getText: getText,
    putBook: putBook,
    updateBook: updateBook,
    deleteBook: deleteBook,
    getSettings: getSettings,
    saveSettings: saveSettings,
    isPersistent: isPersistent,
    estimate: estimate
  };
})();
