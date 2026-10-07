/* ============================================================
   app.js — 书架页：导入 TXT / 书籍卡片 / 排序过滤 / 编辑删除 / 路由
   ============================================================ */
window.QDApp = (function () {
  'use strict';

  var Parser = window.QDParser;
  var Store = window.QDStore;

  var state = {
    books: [],
    filter: 'all',
    sort: 'recent',
    encoding: 'auto',
    editingId: null
  };

  var ENC_LABEL = {
    'utf-8': 'UTF-8', 'gb18030': 'GBK/GB18030', 'gbk': 'GBK',
    'big5': 'Big5', 'utf-16le': 'UTF-16LE', 'utf-16be': 'UTF-16BE'
  };
  var ENC_SCORE_WARN = 0.75;
  var toastTimer = null;

  function el(id) { return document.getElementById(id); }

  function encLabel(label) {
    if (!label) return '未知';
    return ENC_LABEL[String(label).toLowerCase()] || String(label);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      if (c === '&') return '&amp;';
      if (c === '<') return '&lt;';
      if (c === '>') return '&gt;';
      if (c === '"') return '&quot;';
      return '&#39;';
    });
  }

  function fmtWords(n) {
    n = n || 0;
    if (n >= 100000000) return (n / 100000000).toFixed(2) + ' 亿字';
    if (n >= 10000) return (n / 10000).toFixed(n >= 1000000 ? 0 : 1) + ' 万字';
    return n + ' 字';
  }

  function fmtBytes(n) {
    if (!n) return '';
    if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB';
    if (n >= 1024) return Math.round(n / 1024) + ' KB';
    return n + ' B';
  }

  function fmtTime(ts) {
    if (!ts) return '未阅读';
    var d = new Date(ts);
    var now = new Date();
    var pad = function (v) { return v < 10 ? '0' + v : '' + v; };
    var sameDay = d.toDateString() === now.toDateString();
    if (sameDay) return '今天 ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    var y = new Date(now.getTime() - 86400000);
    if (d.toDateString() === y.toDateString()) return '昨天 ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    if (d.getFullYear() === now.getFullYear()) return (d.getMonth() + 1) + '月' + d.getDate() + '日';
    return d.getFullYear() + '年' + (d.getMonth() + 1) + '月' + d.getDate() + '日';
  }

  function hueOf(str) {
    var h = 0, i;
    for (i = 0; i < str.length; i++) h = (h * 31 + str.charCodeAt(i)) % 360;
    /* 书架封面统一走蓝色系：204~243 度 */
    return 204 + (h % 40);
  }

  function toast(msg, type, sticky) {
    var t = el('shelfToast');
    if (!t) return;
    t.textContent = msg;
    t.className = 'q-toast show' + (type ? ' ' + type : '');
    clearTimeout(toastTimer);
    if (sticky) return;
    toastTimer = setTimeout(function () { t.className = 'q-toast'; }, 2600);
  }

  /* ---------------- 主题（应用外壳深浅色） ---------------- */
  function applyAppTheme() {
    var s = Store.getSettings();
    document.body.classList.toggle('dark', !!s.dark);
  }

  /* ---------------- 数据 ---------------- */
  function loadBooks() {
    return Store.listBooks().then(function (books) {
      state.books = (books || []).slice();
    });
  }

  /* ---------------- 列表排序 / 过滤 ---------------- */
  function visibleBooks() {
    var list = state.books.slice();
    if (state.filter === 'reading') {
      list = list.filter(function (b) {
        var p = (b.progress && b.progress.percent) || 0;
        return p > 0 && p < 99.5;
      });
    } else if (state.filter === 'finished') {
      list = list.filter(function (b) { return ((b.progress && b.progress.percent) || 0) >= 99.5; });
    }
    var sort = state.sort;
    list.sort(function (a, b) {
      if (sort === 'name') return String(a.name).localeCompare(String(b.name), 'zh-Hans-CN');
      if (sort === 'imported') return (b.createdAt || 0) - (a.createdAt || 0);
      if (sort === 'progress') return ((b.progress && b.progress.percent) || 0) - ((a.progress && a.progress.percent) || 0);
      return (b.lastReadAt || 0) - (a.lastReadAt || 0) || (b.createdAt || 0) - (a.createdAt || 0);
    });
    return list;
  }

  /* ---------------- 渲染 ---------------- */
  function render() {
    var list = visibleBooks();
    var grid = el('bookGrid');
    var empty = el('shelfEmpty');

    if (!state.books.length) {
      grid.innerHTML = '';
      empty.hidden = false;
      grid.hidden = true;
    } else {
      empty.hidden = true;
      grid.hidden = false;
      if (!list.length) {
        grid.innerHTML = '<div class="q-list-empty">这个分类下还没有书，换个筛选看看～</div>';
      } else {
        grid.innerHTML = list.map(bookCard).join('');
      }
    }

    var words = state.books.reduce(function (sum, b) { return sum + (b.wordCount || 0); }, 0);
    el('shelfTitle').textContent = state.filter === 'reading' ? '在读' : (state.filter === 'finished' ? '已读完' : '我的书架');
    el('shelfSub').textContent = '共 ' + state.books.length + ' 本 · ' + fmtWords(words);
    updateStorageInfo();
  }

  function bookCard(b) {
    var bookName = b.name || '未命名';
    var prog = b.progress || {};
    var percent = Math.max(0, Math.min(100, prog.percent || 0));
    var chapterIdx = (prog.chapter || 0) + 1;
    var total = b.chapterCount || (b.chapters ? b.chapters.length : 0);
    var continueText = percent > 0 ? '继续阅读' : '开始阅读';
    var cover = b.cover
      ? '<img src="' + esc(b.cover) + '" alt="" />'
      : '' +
        '<span class="cb-badge">TXT</span>' +
        '<span class="cb-title">' + esc(bookName) + '</span>' +
        '<span class="cb-author">' + esc(b.author || '佚名') + '</span>';
    var readLink = '#/read/' + encodeURIComponent(b.id) + (percent > 0 ? '/' + (prog.chapter || 0) : '');
    return '' +
      '<article class="q-book" data-id="' + esc(b.id) + '">' +
        '<a class="q-book-cover" href="' + readLink + '" style="--hue:' + hueOf(bookName) + '">' + cover + '</a>' +
        '<div class="q-book-info">' +
          '<a class="q-book-name" href="' + readLink + '" title="' + esc(bookName) + '">' + esc(bookName) + '</a>' +
          '<p class="q-book-meta">' +
            esc(b.author || '佚名') +
            ' · <b>' + (total || '?') + '</b> 章 · <b>' + fmtWords(b.wordCount) + '</b>' +
            ((b.encoding && b.encoding !== 'utf-8') ? ' · ' + esc(encLabel(b.encoding)) : '') +
            ' · ' + fmtTime(b.lastReadAt) +
          '</p>' +
          '<div class="q-book-prog"><i style="width:' + percent.toFixed(1) + '%"></i></div>' +
          '<p class="q-book-prog-text">' +
            (percent > 0
              ? '已读 ' + percent.toFixed(1) + '% · 读到第 ' + chapterIdx + ' 章'
              : '尚未开始阅读 · ' + (b.intro ? esc(b.intro) : '暂无简介')) +
          '</p>' +
          '<div class="q-book-actions">' +
            '<a class="q-btn-primary q-btn-sm" href="' + readLink + '">' + continueText + '</a>' +
            '<button class="q-btn-link" data-act="edit" data-id="' + esc(b.id) + '">编辑</button>' +
            '<button class="q-btn-link" data-act="toc" data-id="' + esc(b.id) + '">目录</button>' +
            '<button class="q-btn-link danger" data-act="del" data-id="' + esc(b.id) + '">删除</button>' +
          '</div>' +
        '</div>' +
      '</article>';
  }

  function updateStorageInfo() {
    var node = el('storageInfo');
    if (!node) return;
    if (!Store.isPersistent()) { node.textContent = '存储受限'; return; }
    Store.estimate().then(function (est) {
      if (!est || !est.usage) { node.textContent = ''; return; }
      node.textContent = '本地占用 ' + fmtBytes(est.usage);
    }).catch(function () { node.textContent = ''; });
  }

  /* ---------------- 导入 ---------------- */
  function pickFiles() {
    var input = el('fileInput');
    input.value = '';
    input.click();
  }

  function isTxt(name) {
    return /\.txt$/i.test(String(name || '')) || /text\/plain/.test('');
  }

  /* ---------------- 安装目录里的「书架」文件夹 ----------------
     安装版（由 Reader001.exe 的本地服务托管）会在安装目录里建一个「书架」文件夹：
       · 启动时自动把里面的 .txt 导入书架；
       · 从「＋ 导入 TXT」导入的文件会同时复制一份进这个文件夹。
     没走本地服务（file:// 直接打开）时，这些接口全部安静跳过。 */
  var shelfDir = '';

  function shelfEnabled() {
    return (location.protocol === 'http:' || location.protocol === 'https:') && typeof window.fetch === 'function';
  }

  function shelfList() {
    if (!shelfEnabled()) return Promise.resolve([]);
    return window.fetch('/__qdr/shelf', { cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (data) {
      if (data && data.dir) { shelfDir = String(data.dir); paintShelfPath(); }
      var files = (data && data.files) || [];
      return files.filter(function (f) { return f && f.name; });
    });
  }

  function shelfRead(name) {
    return window.fetch('/__qdr/shelf/file?name=' + encodeURIComponent(name), { cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.arrayBuffer();
    });
  }

  function shelfWrite(name, buf) {
    if (!shelfEnabled() || !buf) return Promise.resolve(false);
    try {
      return window.fetch('/__qdr/shelf/save?name=' + encodeURIComponent(name), { method: 'POST', body: buf })
        .then(function (r) { return r.ok; })
        .catch(function (err) { console.warn('复制到「书架」文件夹失败', err); return false; });
    } catch (err) {
      console.warn('复制到「书架」文件夹失败', err);
      return Promise.resolve(false);
    }
  }

  function paintShelfPath() {
    var tip = el('shelfPathTip');
    if (!tip || !shelfDir) return;
    tip.hidden = false;
    tip.innerHTML = '也可以把 .txt 直接放进安装目录的「书架」文件夹：<br />' + esc(shelfDir) +
      '<br />然后点右上角的「&#10227; 书架」扫描导入。';
  }

  /* 从「书架」文件夹扫描并导入未入库的 txt，返回导入数量 */
  function syncShelfFolder(opts) {
    opts = opts || {};
    if (!shelfEnabled()) {
      if (opts.notify) toast('当前不是安装版运行方式，读不到「书架」文件夹', 'warn');
      return Promise.resolve(0);
    }
    return shelfList().then(function (files) {
      if (!files.length) {
        if (opts.notify) toast('「书架」文件夹里还没有 txt：' + shelfDir, 'info', true);
        return 0;
      }
      var known = {};
      state.books.forEach(function (b) {
        if (b && b.sourceName) known[b.sourceName + '|' + (b.sourceSize || 0)] = true;
      });
      var todo = files.filter(function (f) { return !known[f.name + '|' + (f.size || 0)]; });
      if (!todo.length) {
        if (opts.notify) toast('「书架」里的 ' + files.length + ' 个 txt 都已经在书架上了', 'ok');
        return 0;
      }
      var done = 0, chain = Promise.resolve();
      todo.forEach(function (f, i) {
        chain = chain.then(function () {
          if (todo.length > 1) toast('正在从「书架」导入 (' + (i + 1) + '/' + todo.length + ') ' + f.name, 'info', true);
          return shelfRead(f.name).then(function (buf) {
            return buildBook(f.name, buf, f.name, f.size);
          }).then(function () { done++; }).catch(function (err) {
            console.error('从「书架」导入失败', f.name, err);
          });
        });
      });
      return chain.then(function () {
        if (!done) return 0;
        // 入库后刷新内存里的书架，调用方的 render() 才能显示新导入的书
        return loadBooks().then(function () { return done; });
      });
    }).catch(function (err) {
      console.warn('扫描「书架」文件夹失败', err);
      if (opts.notify) toast('扫描「书架」文件夹失败：' + (err && err.message ? err.message : err), 'warn', true);
      return 0;
    });
  }

  function importFiles(fileList) {
    var files = Array.prototype.slice.call(fileList || []);
    files = files.filter(function (f) { return /\.txt$/i.test(f.name); });
    if (!files.length) { toast('请选择 .txt 文件', 'warn'); return Promise.resolve(); }
    toast('正在导入 (1/' + files.length + ') ' + files[0].name, 'info', true);

    var done = 0, failed = 0, chain = Promise.resolve(), results = [];
    files.forEach(function (file, i) {
      chain = chain.then(function () {
        toast('正在导入 (' + (i + 1) + '/' + files.length + ') ' + file.name, 'info', true);
        return importOne(file).then(function (meta) { done++; results.push(meta); }).catch(function (err) {
          failed++;
          console.error('导入失败', file.name, err);
        });
      });
    });
    return chain.then(function () {
      return loadBooks();
    }).then(function () {
      render();
      if (!done) { toast('导入失败：' + files.length + ' 个文件都没能读取', 'warn'); return; }
      var last = results[results.length - 1] || {};
      var enc = encLabel(last.encoding);
      if (failed) { toast('导入完成：成功 ' + done + ' 本，失败 ' + failed + ' 本', 'warn'); return; }
      if (!last.encodingForced && typeof last.encodingScore === 'number' && last.encodingScore < ENC_SCORE_WARN) {
        toast('已按 ' + enc + ' 解码，但识别把握不大（' + last.encodingScore.toFixed(2) + '）。若正文是乱码，请在「编码」中选择正确编码后重新导入（先删掉这本）', 'warn', true);
        return;
      }
      toast('已导入 ' + done + ' 本 · ' + enc + ' 编码', 'ok');
    });
  }

  // 把一段 txt 缓冲区解析成书并入库。rawName 用于猜书名/作者（如「西游记 - 吴承恩.txt」）。
  function buildBook(rawName, buf, sourceName, sourceSize) {
    return Promise.resolve().then(function () {
      var forced = state.encoding && state.encoding !== 'auto' ? state.encoding : null;
      var decoded = Parser.decodeText(buf, forced);
      if (!decoded.text || !decoded.text.replace(/\s/g, '').length) {
        throw new Error('文件内容为空');
      }
      var info = Parser.parseFileName(rawName);
      var parsed = Parser.parseChapters(decoded.text);
      var now = Date.now();
      var meta = {
        id: 'b' + now.toString(36) + Math.random().toString(36).slice(2, 7),
        name: info.name,
        sourceName: sourceName || rawName,
        sourceSize: typeof sourceSize === 'number' ? sourceSize : decoded.bytes,
        author: info.author,
        intro: Parser.excerptText(decoded.text.slice(parsed.chapters.length ? parsed.chapters[0].start : 0), 110),
        size: decoded.bytes,
        encoding: decoded.encoding,
        encodingScore: typeof decoded.score === 'number' ? decoded.score : null,
        encodingForced: !!decoded.forced,
        chapterCount: parsed.chapters.length,
        parseMode: parsed.mode,
        wordCount: parsed.words,
        chapters: parsed.chapters,
        createdAt: now,
        updatedAt: now,
        lastReadAt: 0,
        progress: { chapter: 0, ratio: 0, percent: 0 }
      };
      return Store.putBook(meta, decoded.text).then(function () { return meta; });
    });
  }

  // 从「＋ 导入 TXT」进来的文件：入库后再复制一份到安装目录的「书架」文件夹。
  function importOne(file) {
    var bufRef = null;
    return file.arrayBuffer().then(function (buf) {
      bufRef = buf;
      return buildBook(file.name, buf, file.name, file.size);
    }).then(function (meta) {
      return shelfWrite(file.name, bufRef).then(function (ok) {
        meta.shelfCopied = !!ok;
        return meta;
      });
    });
  }

  /* ---------------- 编辑 / 删除 ---------------- */
  function findBook(id) {
    for (var i = 0; i < state.books.length; i++) if (state.books[i].id === id) return state.books[i];
    return null;
  }

  function openEdit(id) {
    var b = findBook(id);
    if (!b) return;
    state.editingId = id;
    el('editName').value = b.name || '';
    el('editAuthor').value = b.author || '';
    el('editTip').textContent = (b.chapterCount || 0) + ' 章 · ' + fmtWords(b.wordCount) + ' · 导入于 ' + fmtTime(b.createdAt) + '（只改本地书架显示，不动原始 txt）';
    el('editModal').hidden = false;
    setTimeout(function () { el('editName').focus(); el('editName').select(); }, 30);
  }

  function closeEdit() {
    el('editModal').hidden = true;
    state.editingId = null;
  }

  function saveEdit() {
    var id = state.editingId;
    if (!id) return;
    var name = el('editName').value.trim();
    var author = el('editAuthor').value.trim();
    if (!name) { toast('书名不能为空', 'warn'); return; }
    Store.updateBook(id, { name: name, author: author, updatedAt: Date.now() }).then(function () {
      closeEdit();
      return loadBooks();
    }).then(function () {
      render();
      toast('已保存', 'ok');
    }).catch(function (e) { toast('保存失败：' + e.message, 'warn'); });
  }

  function removeBook(id) {
    var b = findBook(id);
    if (!b) return;
    var tip = '确定把《' + b.name + '》从书架删除吗？\n（只删除本地书库里的记录，原始 txt 文件不受影响）';
    if (!window.confirm(tip)) return;
    Store.deleteBook(id).then(function () {
      return loadBooks();
    }).then(function () {
      render();
      toast('已删除《' + b.name + '》', 'ok');
    }).catch(function (e) { toast('删除失败：' + e.message, 'warn'); });
  }

  function showToc(id) {
    var b = findBook(id);
    if (!b || !b.chapters) return;
    var names = b.chapters.slice(0, 8).map(function (c, i) { return '第 ' + (i + 1) + ' 章 ' + (c.title || ''); }).join('\n');
    window.alert('《' + b.name + '》 共 ' + b.chapters.length + ' 章\n\n' + names + (b.chapters.length > 8 ? '\n…' : '') + '\n\n点「继续阅读」后在阅读页左侧目录里可以快速跳章。');
  }

  /* ---------------- 路由 ---------------- */
  function route() {
    var hash = location.hash || '#/shelf';
    var m = hash.match(/^#\/read\/([^\/?#]+)(?:\/(\d+))?/);
    if (m && window.QDReader) {
      el('pageShelf').hidden = true;
      window.QDReader.open(decodeURIComponent(m[1]), m[2] ? parseInt(m[2], 10) : null);
    } else {
      if (window.QDReader && window.QDReader.isOpen()) window.QDReader.close();
      el('pageShelf').hidden = false;
      document.body.classList.remove('reader-active');
      document.title = 'Reader001 · 我的书架';
      loadBooks().then(render);
    }
  }

  /* ---------------- 事件绑定 ---------------- */
  function bindEvents() {
    el('btnImport').addEventListener('click', pickFiles);
    el('btnImportEmpty').addEventListener('click', pickFiles);
    el('fileInput').addEventListener('change', function (e) {
      importFiles(e.target.files);
      e.target.value = '';
    });

    el('btnShelfTheme').addEventListener('click', function () {
      var s = Store.saveSettings({ dark: !Store.getSettings().dark });
      document.body.classList.toggle('dark', !!s.dark);
    });

    el('sortSelect').addEventListener('change', function (e) {
      state.sort = e.target.value;
      Store.saveSettings({ sort: state.sort });
      render();
    });

    el('encSelect').addEventListener('change', function (e) {
      state.encoding = e.target.value;
      Store.saveSettings({ encoding: state.encoding });
      if (state.encoding === 'auto') toast('导入时自动识别编码', 'info');
      else toast('导入时按 ' + encLabel(state.encoding) + ' 解码（重新导入即可修正乱码）', 'info');
    });

    el('shelfNav').addEventListener('click', function (e) {
      var a = e.target.closest('a[data-filter]');
      if (!a) return;
      e.preventDefault();
      state.filter = a.getAttribute('data-filter');
      Array.prototype.forEach.call(el('shelfNav').querySelectorAll('a'), function (node) {
        node.classList.toggle('active', node === a);
      });
      render();
      if (location.hash !== '#/shelf') location.hash = '#/shelf';
    });

    el('bookGrid').addEventListener('click', function (e) {
      var btn = e.target.closest('[data-act]');
      if (!btn) return;
      e.preventDefault();
      var id = btn.getAttribute('data-id');
      var act = btn.getAttribute('data-act');
      if (act === 'edit') openEdit(id);
      else if (act === 'del') removeBook(id);
      else if (act === 'toc') showToc(id);
    });

    var scanBtn = el('btnShelfScan');
    if (scanBtn) {
      scanBtn.addEventListener('click', function () {
        syncShelfFolder({ notify: true }).then(function (n) {
          if (n > 0) { render(); toast('已从「书架」文件夹导入 ' + n + ' 本 txt', 'ok'); }
        });
      });
    }

    el('editSave').addEventListener('click', saveEdit);
    el('editCancel').addEventListener('click', closeEdit);
    el('editCancel2').addEventListener('click', closeEdit);
    el('editModal').addEventListener('click', function (e) { if (e.target === el('editModal')) closeEdit(); });
    ['editName', 'editAuthor'].forEach(function (id) {
      el(id).addEventListener('keydown', function (e) {
        if (e.key === 'Enter') saveEdit();
        if (e.key === 'Escape') closeEdit();
      });
    });

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !el('editModal').hidden) closeEdit();
    });

    /* 拖拽导入 */
    var dragDepth = 0;
    window.addEventListener('dragenter', function (e) {
      if (el('pageShelf').hidden) return;
      e.preventDefault();
      dragDepth++;
      el('dropMask').classList.add('show');
    });
    window.addEventListener('dragover', function (e) {
      if (el('pageShelf').hidden) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = 'copy';
    });
    window.addEventListener('dragleave', function () {
      dragDepth = Math.max(0, dragDepth - 1);
      if (!dragDepth) el('dropMask').classList.remove('show');
    });
    window.addEventListener('drop', function (e) {
      if (el('pageShelf').hidden) return;
      e.preventDefault();
      dragDepth = 0;
      el('dropMask').classList.remove('show');
      var dt = e.dataTransfer;
      if (!dt) return;
      var files = dt.files && dt.files.length ? dt.files : [];
      if (!files.length) { toast('没有识别到文件，请拖入 .txt 文件', 'warn'); return; }
      importFiles(files);
    });

    window.addEventListener('hashchange', route);
  }

  /* 由 Reader001.exe 内置的本地服务托管时，用心跳告诉它“窗口还开着”，
     窗口关掉之后 exe 会自动退出，不会在后台留下僵尸进程。 */
  function startHeartbeat() {
    if (location.protocol !== 'http:' && location.protocol !== 'https:') return;
    var beat = function () {
      try {
        fetch('/__qdr/ping', { cache: 'no-store' }).then(function (r) { return r.text(); }).catch(function () {});
      } catch (err) { /* 老浏览器没有 fetch，忽略 */ }
    };
    beat();
    window.setInterval(beat, 5000);
  }

  /* ---------------- 启动 ---------------- */
  function boot() {
    bindEvents();
    var settings = Store.getSettings();
    state.sort = settings.sort || 'recent';
    state.encoding = settings.encoding || 'auto';
    el('sortSelect').value = state.sort;
    el('encSelect').value = state.encoding;
    applyAppTheme();
    if (location.protocol === 'file:') el('fileHint').hidden = false;
    startHeartbeat();

    Store.listBooks().catch(function () { return []; }).then(function (books) {
      state.books = books || [];
    }).then(function () {
      if (location.hash && location.hash.indexOf('#/read/') === 0) {
        route();
        render();
      } else {
        render();
        route();
      }
    }).then(function () {
      // 安装版：启动时自动扫描安装目录里的「书架」文件夹，把新放进去的 txt 导入书架
      var scanBtn = el('btnShelfScan');
      if (scanBtn) scanBtn.hidden = !shelfEnabled();
      if (!shelfEnabled()) return 0;
      return syncShelfFolder({}).then(function (n) {
        if (n > 0) { render(); toast('已从「书架」文件夹导入 ' + n + ' 本 txt', 'ok'); }
        return n;
      });
    }).catch(function (e) {
      console.error(e);
      toast('书架初始化失败：' + (e && e.message ? e.message : e), 'warn', true);
      render();
    });
  }

  return {
    boot: boot,
    render: render,
    refresh: loadBooks,
    syncShelf: syncShelfFolder,
    toast: toast,
    esc: esc,
    fmtWords: fmtWords
  };
})();
