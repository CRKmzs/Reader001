/* ============================================================
   reader.js — 阅读页：目录抽屉 / 翻章 / 进度记忆 / 字体与主题设置
   ============================================================ */
window.QDReader = (function () {
  'use strict';

  var Parser = window.QDParser;
  var Store = window.QDStore;

  var cur = null;
  var openToken = 0;
  var saveTimer = null;
  var catList = [];
  var catRendered = 0;
  var CAT_CHUNK = 300;
  var lastScrollTop = 0;
  var lastRatio = 0;
  var scrollTicking = false;
  var filterTimer = null;
  var toastTimer = null;
  var bound = false;

  function el(id) { return document.getElementById(id); }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      if (c === '&') return '&amp;';
      if (c === '<') return '&lt;';
      if (c === '>') return '&gt;';
      if (c === '"') return '&quot;';
      return '&#39;';
    });
  }

  function toast(msg, type) {
    var t = el('rdToast');
    if (!t) return;
    t.textContent = msg;
    t.className = 'rd-toast show' + (type ? ' ' + type : '');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.className = 'rd-toast'; }, 2000);
  }

  function isOpen() { return !!cur && !el('pageReader').hidden; }

  /* ---------------- 打开 / 关闭 ---------------- */
  function open(bookId, chapterIndex) {
    var token = ++openToken;
    if (!bound) bindEvents();

    return Store.getBook(bookId).then(function (meta) {
      if (token !== openToken) return null;
      if (!meta) throw new Error('这本书已经不在书架上了');
      return Store.getText(bookId).then(function (text) {
        if (token !== openToken) return null;
        if (!text) throw new Error('书籍正文读取失败');
        var parsed = (meta.chapters && meta.chapters.length) ? meta.chapters : Parser.parseChapters(text).chapters;
        cur = { id: bookId, meta: meta, text: text, chapters: parsed, index: 0 };

        el('pageReader').hidden = false;
        document.body.classList.add('reader-active');
        el('rdCatalogFilter').value = '';
        el('rdSettings').hidden = true;

        var p = meta.progress || {};
        var hasTarget = (chapterIndex !== null && chapterIndex !== undefined);
        applySettings();
        renderCatalog();
        renderChapter(hasTarget ? chapterIndex : (p.chapter || 0), hasTarget ? 0 : (p.ratio || 0));
        updateMeta();
        saveProgress(true);
        return true;
      });
    }).catch(function (e) {
      console.error(e);
      toast((e && e.message) || '打开失败', 'warn');
      setTimeout(function () { location.hash = '#/shelf'; }, 900);
    });
  }

  function close() {
    if (!cur) {
      el('pageReader').hidden = true;
      document.body.classList.remove('reader-active');
      return;
    }
    saveProgress(true);
    openToken++;
    cur = null;
    catList = [];
    catRendered = 0;
    el('pageReader').hidden = true;
    el('pageReader').classList.remove('rd-hide-bar');
    el('rdCatalogDrawer').hidden = true;
    el('rdDrawerMask').hidden = true;
    el('rdSettings').hidden = true;
    document.body.classList.remove('reader-active');
    if (window.QDApp) window.QDApp.render();
  }

  /* ---------------- 正文渲染 ---------------- */
  function splitLongLine(line, limit) {
    if (line.length <= limit) return [line];
    var out = [], buf = '', i, ch;
    for (i = 0; i < line.length; i++) {
      ch = line.charAt(i);
      buf += ch;
      if (buf.length >= limit && '。！？”"’.!?'.indexOf(ch) >= 0) { out.push(buf); buf = ''; }
    }
    if (buf) out.push(buf);
    return out;
  }

  function paragraphsHTML(raw) {
    var text = String(raw || '').replace(/^\s+/, '').replace(/\s+$/, '');
    if (!text) return '<p class="rd-blank">（本章暂无正文内容）</p>';
    var lines = text.split('\n');
    var out = [], i, j, t, chunks;
    for (i = 0; i < lines.length; i++) {
      t = lines[i].replace(/^\s+|\s+$/g, '');
      if (!t) continue;
      chunks = splitLongLine(t, 700);
      for (j = 0; j < chunks.length; j++) out.push('<p>' + esc(chunks[j]) + '</p>');
    }
    if (!out.length) return '<p class="rd-blank">（本章暂无正文内容）</p>';
    return out.join('');
  }

  function renderChapter(index, ratio) {
    if (!cur) return;
    var total = cur.chapters.length;
    if (!total) return;
    index = Math.max(0, Math.min(total - 1, index | 0));
    var ch = cur.chapters[index];
    var raw = cur.text.slice(ch.start, ch.end);
    cur.index = index;

    el('rdTitle').textContent = Parser.chapterTitle(ch, index);
    el('rdMeta').textContent = '第 ' + (index + 1) + ' / ' + total + ' 章 · 本章 ' + Parser.countWords(raw) + ' 字 · ' + cur.meta.name;
    el('rdContent').innerHTML = paragraphsHTML(raw);
    el('rdChapterName').textContent = Parser.chapterTitle(ch, index);

    var hasPrev = index > 0, hasNext = index < total - 1;
    el('rdArrowPrev').disabled = !hasPrev;
    el('rdArrowNext').disabled = !hasNext;
    el('rdPrevBottom').disabled = !hasPrev;
    el('rdNextBottom').disabled = !hasNext;

    document.title = Parser.chapterTitle(ch, index) + ' - 《' + cur.meta.name + '》';

    revealInCatalog(index);

    var main = el('rdMain');
    var max = Math.max(0, main.scrollHeight - main.clientHeight);
    var r = Math.max(0, Math.min(1, ratio || 0));
    main.scrollTop = r * max;
    lastScrollTop = main.scrollTop;
    lastRatio = r;
    el('pageReader').classList.remove('rd-hide-bar');
    updateProgressUI();
  }

  function currentRatio() {
    var main = el('rdMain');
    if (!main) return 0;
    var max = main.scrollHeight - main.clientHeight;
    if (max <= 0) return 0;
    lastRatio = Math.max(0, Math.min(1, main.scrollTop / max));
    return lastRatio;
  }

  function overallPercent() {
    if (!cur) return 0;
    var ch = cur.chapters[cur.index] || { start: 0, end: 1 };
    var span = Math.max(1, ch.end - ch.start);
    var pos = ch.start + currentRatio() * span;
    return Math.max(0, Math.min(100, (pos / Math.max(1, cur.text.length)) * 100));
  }

  function updateProgressUI() {
    if (!cur) return;
    var percent = overallPercent();
    el('rdProgressBar').style.width = percent.toFixed(2) + '%';
    el('rdFooterInfo').textContent = '第 ' + (cur.index + 1) + ' / ' + cur.chapters.length + ' 章 · 已读 ' + percent.toFixed(1) + '%';
  }

  function updateMeta() {
    if (!cur) return;
    el('rdBookName').textContent = cur.meta.name + (cur.meta.author ? ' · ' + cur.meta.author : '');
    el('rdChapterName').textContent = Parser.chapterTitle(cur.chapters[cur.index], cur.index);
  }

  /* ---------------- 进度保存 ---------------- */
  function saveProgress(force) {
    if (!cur) return;
    var id = cur.id;
    var payload = {
      chapter: cur.index,
      ratio: Math.round(currentRatio() * 1000) / 1000,
      percent: Math.round(overallPercent() * 10) / 10,
      updatedAt: Date.now()
    };
    cur.meta.progress = payload;
    cur.meta.lastReadAt = Date.now();
    var lastReadAt = cur.meta.lastReadAt;
    clearTimeout(saveTimer);
    var flush = function () {
      Store.updateBook(id, { progress: payload, lastReadAt: lastReadAt }).catch(function () { /* 忽略写入失败 */ });
    };
    if (force) flush();
    else saveTimer = setTimeout(flush, 700);
  }

  /* ---------------- 目录 ---------------- */
  function renderCatalog() {
    if (!cur) return;
    var filter = (el('rdCatalogFilter').value || '').replace(/^\s+|\s+$/g, '').toLowerCase();
    catList = [];
    var i;
    for (i = 0; i < cur.chapters.length; i++) {
      if (filter) {
        var title = String(cur.chapters[i].title || '').toLowerCase();
        var label = '第' + (i + 1) + '章';
        if (title.indexOf(filter) === -1 && label.indexOf(filter) === -1 && String(i + 1) !== filter) continue;
      }
      catList.push(i);
    }
    catRendered = 0;
    el('rdCatalogList').innerHTML = '';
    el('rdCatalogBook').textContent = cur.meta.name;
    el('rdCatalogCount').textContent = cur.chapters.length + ' 章' + (filter ? ' · 命中 ' + catList.length + ' 章' : '');
    if (!catList.length) {
      el('rdCatalogList').innerHTML = '<div class="rd-cat-empty">没有匹配的章节</div>';
      el('rdCatalogMore').hidden = true;
      return;
    }
    revealInCatalog(cur.index);
  }

  function renderCatalogChunk() {
    if (!cur || catRendered >= catList.length) { el('rdCatalogMore').hidden = true; return; }
    var html = [], n = 0, i, c;
    while (catRendered < catList.length && n < CAT_CHUNK) {
      i = catList[catRendered++];
      c = cur.chapters[i];
      if (c.isVolume) {
        html.push('<div class="rd-cat-volume">' + esc(Parser.chapterTitle(c, i)) + '</div>');
      } else {
        html.push('<button class="rd-cat-item' + (i === cur.index ? ' active' : '') + '" data-index="' + i + '">' +
          '<span class="ci-idx">' + (i + 1) + '</span>' + esc(Parser.chapterTitle(c, i)) + '</button>');
      }
      n++;
    }
    el('rdCatalogList').insertAdjacentHTML('beforeend', html.join(''));
    el('rdCatalogMore').hidden = catRendered >= catList.length;
  }

  function ensureCatalogUpTo(index) {
    var pos = catList.indexOf(index);
    if (pos < 0) return;
    var need = Math.min(catList.length, pos + 40);
    var guard = 0;
    while (catRendered < need && guard++ < 200) renderCatalogChunk();
  }

  function highlightCatalog(index) {
    var items = el('rdCatalogList').querySelectorAll('.rd-cat-item');
    var key = String(index);
    for (var i = 0; i < items.length; i++) items[i].classList.toggle('active', items[i].getAttribute('data-index') === key);
  }

  function scrollActiveIntoView() {
    var list = el('rdCatalogList');
    var item = list.querySelector('.rd-cat-item.active');
    if (!item) return;
    var lr = list.getBoundingClientRect();
    var ir = item.getBoundingClientRect();
    list.scrollTop += (ir.top - lr.top) - list.clientHeight / 2 + ir.height / 2;
  }

  function revealInCatalog(index) {
    if (!catList.length) return;
    ensureCatalogUpTo(index);
    highlightCatalog(index);
    scrollActiveIntoView();
  }

  function openDrawer() {
    el('rdCatalogDrawer').hidden = false;
    el('rdDrawerMask').hidden = false;
    el('rdSettings').hidden = true;
    el('pageReader').classList.add('rd-overlay');
    renderCatalog();
  }

  function closeDrawer() {
    el('rdCatalogDrawer').hidden = true;
    el('rdDrawerMask').hidden = true;
    el('pageReader').classList.remove('rd-overlay');
  }

  function toggleSettings(force) {
    var panel = el('rdSettings');
    var next = (force === undefined) ? panel.hidden : !!force;
    panel.hidden = !next;
    if (next) {
      closeDrawer();
      syncControls();
      el('pageReader').classList.remove('rd-hide-bar');
    }
    el('pageReader').classList.toggle('rd-overlay', next || !el('rdCatalogDrawer').hidden);
  }

  /* ---------------- 翻章 ---------------- */
  function gotoChapter(index, ratio) {
    if (!cur) return;
    if (index < 0 || index >= cur.chapters.length) return;
    saveProgress(true);
    renderChapter(index, ratio || 0);
    saveProgress(true);
    updateMeta();
  }

  function turnPage(dir) {
    var main = el('rdMain');
    var step = Math.max(140, main.clientHeight * 0.88);
    var atBottom = main.scrollTop + main.clientHeight >= main.scrollHeight - 10;
    var atTop = main.scrollTop <= 10;
    if (dir > 0 && atBottom) { gotoChapter(cur.index + 1, 0); return; }
    if (dir < 0 && atTop) { gotoChapter(cur.index - 1, 1); return; }
    main.scrollTop = main.scrollTop + dir * step;
    updateProgressUI();
    saveProgress(false);
  }

  /* ---------------- 全屏 ---------------- */
  function toggleFullscreen() {
    var doc = document;
    if (doc.fullscreenElement) {
      if (doc.exitFullscreen) doc.exitFullscreen();
      return;
    }
    var root = doc.documentElement;
    if (root.requestFullscreen) {
      root.requestFullscreen().catch(function () { toast('当前环境不允许全屏', 'warn'); });
    } else {
      toast('当前浏览器不支持全屏 API', 'warn');
    }
  }

  /* ---------------- 设置 ---------------- */
  function applySettings() {
    var s = Store.getSettings();
    var page = el('pageReader');
    page.setAttribute('data-rd-theme', s.theme || 'default');
    page.setAttribute('data-rd-font', s.font || 'song');
    page.style.setProperty('--rd-size', (s.fontSize || 20) + 'px');
    page.style.setProperty('--rd-lh', String(s.lineHeight || 1.8));
    page.style.setProperty('--rd-ps', (s.paraSpace || 0) + 'em');
    page.style.setProperty('--rd-width', s.width ? (s.width + 'px') : '100%');
    page.classList.toggle('rd-turn-click', s.turn === 'click');
    syncControls();
    if (cur) updateProgressUI();
  }

  function setActiveBy(selector, attr, value) {
    var nodes = document.querySelectorAll(selector);
    for (var i = 0; i < nodes.length; i++) {
      nodes[i].classList.toggle('active', nodes[i].getAttribute('data-' + attr) === String(value));
    }
  }

  function syncControls() {
    var s = Store.getSettings();
    setActiveBy('#rdThemes .rd-swatch', 'theme', s.theme);
    setActiveBy('#rdFonts .rd-chip', 'font', s.font);
    setActiveBy('#rdLines .rd-chip', 'lh', s.lineHeight);
    setActiveBy('#rdParas .rd-chip', 'ps', s.paraSpace);
    setActiveBy('#rdWidths .rd-chip', 'width', s.width);
    setActiveBy('#rdTurns .rd-chip', 'turn', s.turn);
    var slider = el('rdFontSize');
    if (slider) slider.value = s.fontSize;
    var val = el('rdFontSizeVal');
    if (val) val.textContent = String(s.fontSize);
  }

  function stepFont(delta) {
    var s = Store.getSettings();
    var next = Math.max(14, Math.min(40, (s.fontSize || 20) + delta));
    Store.saveSettings({ fontSize: next });
    applySettings();
  }

  function resetSettings() {
    Store.saveSettings(Store.defaultSettings());
    applySettings();
    toast('已恢复默认阅读设置', 'ok');
  }

  /* ---------------- 自动隐藏顶栏 ---------------- */
  function autoHide(top) {
    var page = el('pageReader');
    if (!el('rdSettings').hidden || !el('rdCatalogDrawer').hidden) {
      page.classList.remove('rd-hide-bar');
      return;
    }
    if (top > 300 && top > lastScrollTop + 8) page.classList.add('rd-hide-bar');
    else if (top < lastScrollTop - 8 || top < 120) page.classList.remove('rd-hide-bar');
  }

  function onScroll() {
    if (!cur || scrollTicking) return;
    scrollTicking = true;
    window.requestAnimationFrame(function () {
      scrollTicking = false;
      if (!cur) return;
      var top = el('rdMain').scrollTop;
      updateProgressUI();
      autoHide(top);
      saveProgress(false);
      lastScrollTop = top;
    });
  }

  function onMainClick(e) {
    if (!cur) return;
    if (e.target.closest('button, a, #rdSettings, #rdCatalogDrawer')) return;
    var s = Store.getSettings();
    if (s.turn !== 'click') return;
    var main = el('rdMain');
    var rect = main.getBoundingClientRect();
    var w = rect.width || 1;
    var x = e.clientX - rect.left;
    if (x < w * 0.32) turnPage(-1);
    else if (x > w * 0.68) turnPage(1);
    else el('pageReader').classList.toggle('rd-hide-bar');
  }

  /* ---------------- 事件 ---------------- */
  function bindEvents() {
    bound = true;

    el('rdBack').addEventListener('click', function () { location.hash = '#/shelf'; });
    el('rdBtnCatalog').addEventListener('click', openDrawer);
    el('rdFooterCatalog').addEventListener('click', openDrawer);
    el('rdCatalogBottom').addEventListener('click', openDrawer);
    el('rdCatalogClose').addEventListener('click', closeDrawer);
    el('rdDrawerMask').addEventListener('click', closeDrawer);
    el('rdBtnSettings').addEventListener('click', function () { toggleSettings(); });
    el('rdFooterSettings').addEventListener('click', function () { toggleSettings(); });
    el('rdSettingsClose').addEventListener('click', function () { toggleSettings(false); });
    el('rdBtnFullscreen').addEventListener('click', toggleFullscreen);
    el('rdReset').addEventListener('click', resetSettings);

    el('rdArrowPrev').addEventListener('click', function () { gotoChapter(cur.index - 1, 1); });
    el('rdArrowNext').addEventListener('click', function () { gotoChapter(cur.index + 1, 0); });
    el('rdPrevBottom').addEventListener('click', function () { gotoChapter(cur.index - 1, 1); });
    el('rdNextBottom').addEventListener('click', function () { gotoChapter(cur.index + 1, 0); });
    el('rdFooterPrev').addEventListener('click', function () { gotoChapter(cur.index - 1, 1); });
    el('rdFooterNext').addEventListener('click', function () { gotoChapter(cur.index + 1, 0); });

    el('rdCatalogFilter').addEventListener('input', function () {
      clearTimeout(filterTimer);
      filterTimer = setTimeout(renderCatalog, 200);
    });
    el('rdCatalogList').addEventListener('click', function (e) {
      var item = e.target.closest('.rd-cat-item');
      if (!item) return;
      var idx = parseInt(item.getAttribute('data-index'), 10);
      if (isNaN(idx)) return;
      gotoChapter(idx, 0);
      if (window.innerWidth < 900) closeDrawer();
    });
    el('rdCatalogList').addEventListener('scroll', function () {
      var list = el('rdCatalogList');
      if (list.scrollTop + list.clientHeight >= list.scrollHeight - 240) renderCatalogChunk();
    });
    el('rdCatalogMoreBtn').addEventListener('click', renderCatalogChunk);

    el('rdMain').addEventListener('scroll', onScroll);
    el('rdMain').addEventListener('click', onMainClick);
    el('rdMain').addEventListener('mousemove', function (e) {
      if (e.clientY < 70) el('pageReader').classList.remove('rd-hide-bar');
    });
    window.addEventListener('resize', function () {
      if (!cur) return;
      var main = el('rdMain');
      var max = Math.max(0, main.scrollHeight - main.clientHeight);
      main.scrollTop = lastRatio * max;
      updateProgressUI();
    });

    el('rdFontSize').addEventListener('input', function (e) {
      Store.saveSettings({ fontSize: parseInt(e.target.value, 10) || 20 });
      applySettings();
    });
    el('rdFontMinus').addEventListener('click', function () { stepFont(-1); });
    el('rdFontPlus').addEventListener('click', function () { stepFont(1); });

    el('rdSettings').addEventListener('click', function (e) {
      var node = e.target.closest('button[data-theme], button[data-font], button[data-lh], button[data-ps], button[data-width], button[data-turn]');
      if (!node) return;
      var patch = {};
      if (node.hasAttribute('data-theme')) patch.theme = node.getAttribute('data-theme');
      if (node.hasAttribute('data-font')) patch.font = node.getAttribute('data-font');
      if (node.hasAttribute('data-lh')) patch.lineHeight = parseFloat(node.getAttribute('data-lh'));
      if (node.hasAttribute('data-ps')) patch.paraSpace = parseFloat(node.getAttribute('data-ps'));
      if (node.hasAttribute('data-width')) patch.width = parseInt(node.getAttribute('data-width'), 10);
      if (node.hasAttribute('data-turn')) patch.turn = node.getAttribute('data-turn');
      Store.saveSettings(patch);
      applySettings();
    });

    document.addEventListener('keydown', function (e) {
      if (!isOpen()) return;
      var tag = e.target && e.target.tagName ? e.target.tagName : '';
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
        if (e.key === 'Escape') e.target.blur();
        return;
      }
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') { e.preventDefault(); gotoChapter(cur.index - 1, 1); }
      else if (e.key === 'ArrowRight' || e.key === 'PageDown') { e.preventDefault(); gotoChapter(cur.index + 1, 0); }
      else if (e.key === 'Escape') {
        if (!el('rdSettings').hidden) toggleSettings(false);
        else if (!el('rdCatalogDrawer').hidden) closeDrawer();
        else location.hash = '#/shelf';
      } else if (e.key === 'f' || e.key === 'F') { toggleFullscreen(); }
      else if (e.key === 'Home') { e.preventDefault(); el('rdMain').scrollTop = 0; }
      else if (e.key === 'End') { e.preventDefault(); el('rdMain').scrollTop = el('rdMain').scrollHeight; }
      else if (e.key === ' ') {
        e.preventDefault();
        el('rdMain').scrollTop += (e.shiftKey ? -1 : 1) * el('rdMain').clientHeight * 0.9;
      }
    });

    document.addEventListener('visibilitychange', function () {
      if (document.hidden && cur) saveProgress(true);
    });
    window.addEventListener('beforeunload', function () {
      if (cur) saveProgress(true);
    });
  }

  return {
    open: open,
    close: close,
    isOpen: isOpen,
    applySettings: applySettings,
    gotoChapter: gotoChapter
  };
})();
