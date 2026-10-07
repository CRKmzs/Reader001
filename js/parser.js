/* ============================================================
   parser.js — 编码识别 / 章节解析 / 字数统计（纯函数，可被 Node 测试）
   ============================================================ */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.QDParser = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var TITLE_MAX = 60;
  var RE_CHAPTER = /^第\s*[0-9０-９零〇一二三四五六七八九十百千万两廿]{1,12}\s*[章回节篇部][^\n]{0,40}$/;
  var RE_VOLUME = /^第\s*[0-9０-９零〇一二三四五六七八九十百千万两廿]{1,12}\s*卷[^\n]{0,40}$/;
  var RE_SPECIAL = /^(序章|序言|自序|楔子|引子|前言|后记|尾声|终章|大结局|番外|作品相关|外传|篇外)([\s:：·、\-—][^\n]{0,24})?$/;
  /* 括号里出现的这些词是版本标记，不是作者名 */
  var RE_NOISE_AUTHOR = /^(全本|全集|完结|精校|校对|校对版|未删减|无删减|下载|小说|全文|合集|新版|文字版|epub|txt|pdf)$/i;
  var RE_EN = /^chapter\s*[0-9ivxlc]+[^\n]{0,40}$/i;

  function toBytes(input) {
    if (!input) return new Uint8Array(0);
    if (typeof Uint8Array !== 'undefined' && input instanceof Uint8Array) return input;
    if (typeof ArrayBuffer !== 'undefined' && input instanceof ArrayBuffer) return new Uint8Array(input);
    if (input.buffer && typeof ArrayBuffer !== 'undefined' && input.buffer instanceof ArrayBuffer) {
      return new Uint8Array(input.buffer, input.byteOffset || 0, input.byteLength || input.length);
    }
    return new Uint8Array(input);
  }

  function supported(label) {
    try { new TextDecoder(label); return true; } catch (e) { return false; }
  }

  /* ---------- 编码判定 ---------- */
  /* 高频汉字表：用于区分“编码正确”与“字节合法但用错编码”的情况
     （典型：Big5 的字节按 GB18030 解码仍然全是合法汉字，但常用字命中率骤降） */
  var COMMON_CHARS = '的一是不了在人有我他这个们中来上大为和国地到以说时要就出会可也你对生能而子那得于着下自之年过发后作里用道行所然家种事成方多经么去法学如都同现当没动面起看定天分还进好小部其些主样理心她本前开但因只从想实日军者意无力它与长把机十民第公此已工使情明性知全三又关点正业外将两高间由问很最重并物手应战向头文体政美相见被利什二等产或新己制身果加西斯月话合回特代内信表化老给世位次度门任常先海通教儿原东声提立及比员解水名真论处走义各入几口认条平系气题活尔更别打女变四神总何电数安少报才结反受目太量再感建务做接必场件计管期市直德资命山金指克许统区保至队形社便空决治展马科司五基眼书非则听白却界达光放强即像难且权思王象完设式色路记南品住告类求据程北边死张该交规万取拉格望觉术领共确传师观清今切院让识候带导争运笑飞风步改收根干造言联持组每济车亲极林服快办议往元英士证近失转夫令准布始怎呢存未远叫台单影具罗字爱击流备兵连调深商算质团集百需价花党华城石级整府离况亚请技际约示复病息究线似官火断精满支视消越器容照须九增研写称企八功吗包片史委乎查轻容易星差属钱专否帝射草冲承独';

  /* 候选编码：按“打平时优先”排序，简体中文优先于繁体 */
  var ENC_ORDER = ['utf-8', 'gb18030', 'big5', 'utf-16le', 'utf-16be'];
  var SAMPLE_LIMIT = 1 << 20;
  var TEXT_LIMIT = 262144;
  var BIG5_MARGIN = 0.08;
  var SCORES = {};

  function decodeWith(label, bytes) {
    try { return new TextDecoder(label).decode(bytes); } catch (e) { return null; }
  }

  /* 解码结果的“像不像正常中文文本”打分：越高越可信 */
  function plausibility(text) {
    var sample = text.length > TEXT_LIMIT ? text.slice(0, TEXT_LIMIT) : text;
    if (sample.length > 16) sample = sample.slice(0, sample.length - 4); /* 采样截断可能切坏最后一个字符 */
    var total = 0, good = 0, bad = 0, common = 0, i, code, ch;
    for (i = 0; i < sample.length; i++) {
      code = sample.charCodeAt(i);
      total++;
      if (code === 0xFFFD) { bad += 2.5; continue; }
      if (code === 0xFEFF || code === 0x200B) continue;
      if (code < 0x20) {
        if (code === 0x0A || code === 0x0D || code === 0x09) good += 0.8; else bad += 3;
        continue;
      }
      if (code >= 0x4E00 && code <= 0x9FFF) {
        good += 1;
        ch = sample.charAt(i);
        if (COMMON_CHARS.indexOf(ch) >= 0) common++;
        continue;
      }
      if (code >= 0x3400 && code <= 0x4DBF) { good += 0.5; bad += 0.3; continue; } /* 扩展 A：生僻字 */
      if (code >= 0xE000 && code <= 0xF8FF) { bad += 1.5; continue; }              /* 私用区 */
      if (code >= 0xD800 && code <= 0xDFFF) { bad += 1; continue; }                /* 落单代理 */
      if ((code >= 0x3000 && code <= 0x303F) || (code >= 0xFF00 && code <= 0xFFEF) ||
          (code >= 0x2000 && code <= 0x206F)) { good += 0.85; continue; }          /* 中英标点 */
      if (code >= 0x20 && code <= 0x7E) { good += 0.8; continue; }                 /* 可见 ASCII */
      good += 0.1;
    }
    if (!total) return 0;
    return (good - bad) / total + 0.6 * (common / total);
  }

  /* 逐个候选编码解码采样并打分，返回 {label, scores} */
  function scoreEncodings(bytes) {
    var n = bytes.length;
    if (n === 0) return { label: 'utf-8', scores: {} };
    if (n >= 2 && bytes[0] === 0xFF && bytes[1] === 0xFE) return { label: 'utf-16le', scores: {} };
    if (n >= 2 && bytes[0] === 0xFE && bytes[1] === 0xFF) return { label: 'utf-16be', scores: {} };
    if (n >= 3 && bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF) return { label: 'utf-8', scores: {} };

    var sample = bytes.subarray(0, Math.min(n, SAMPLE_LIMIT));
    var scores = {}, i, label, text, score, k;
    for (i = 0; i < ENC_ORDER.length; i++) {
      label = ENC_ORDER[i];
      if (!supported(label)) continue;
      text = decodeWith(label, sample);
      if (text === null) continue;
      scores[label] = plausibility(text);
    }
    var best = 'utf-8', bestVal = -Infinity, val;
    for (i = 0; i < ENC_ORDER.length; i++) {
      label = ENC_ORDER[i];
      if (scores[label] === undefined) continue;
      val = scores[label];
      /* Big5 与 GB18030 字节结构重叠，简体优先：只有明显更好才判为 Big5 */
      if (label === 'big5' && scores.gb18030 !== undefined && val < scores.gb18030 + BIG5_MARGIN) {
        val = scores.gb18030;
      }
      if (val > bestVal + 1e-9) { bestVal = val; best = label; }
    }
    for (k in scores) SCORES[k] = scores[k];
    return { label: best, scores: scores };
  }

  function detectEncoding(input) {
    return scoreEncodings(toBytes(input)).label;
  }

  /* 诊断用：返回判定结果与各候选编码的得分 */
  function explainEncoding(input) {
    return scoreEncodings(toBytes(input));
  }

  /* ---------- 文本规范化 ---------- */
  function normalizeText(text) {
    if (text.charCodeAt(0) === 0xFEFF) text = text.slice(1);
    return text.replace(/\r\n?/g, '\n');
  }

  /* ---------- 解码 ---------- */
  /* forcedLabel 非空时不再自动判定（用户在书架上指定了编码），返回值多一个 score：
     自动判定时是“像不像正常中文文本”的得分（约 0.9 以上才可靠），指定编码时为 undefined。 */
  function decodeText(input, forcedLabel) {
    var bytes = toBytes(input);
    var forced = forcedLabel && forcedLabel !== 'auto' ? String(forcedLabel).toLowerCase() : null;
    var det = forced && supported(forced) ? { label: forced, scores: {} } : scoreEncodings(bytes);
    var enc = det.label;
    var body = bytes;
    if (enc === 'utf-8' && bytes.length >= 3 && bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF) {
      body = bytes.subarray(3);
    } else if ((enc === 'utf-16le' && bytes.length >= 2 && bytes[0] === 0xFF && bytes[1] === 0xFE) ||
               (enc === 'utf-16be' && bytes.length >= 2 && bytes[0] === 0xFE && bytes[1] === 0xFF)) {
      body = bytes.subarray(2);
    }
    var label = enc === 'utf-16be' ? 'utf-16be' : enc;
    var text;
    try {
      text = new TextDecoder(label).decode(body);
    } catch (e) {
      text = new TextDecoder('utf-8').decode(body);
      label = 'utf-8';
    }
    return {
      text: normalizeText(text),
      encoding: label,
      hadBom: body !== bytes,
      bytes: bytes.length,
      score: det.scores[label],
      forced: !!forced
    };
  }

  /* ---------- 标题识别 ---------- */
  function cleanLine(line) {
    return line.replace(/[\s\u3000]+/g, ' ').replace(/^ | $/g, '');
  }

  function isTitleLine(line, custom) {
    if (!line || line.length > TITLE_MAX) return null;
    if (/[。！？]$/.test(line)) return null;
    if (custom) {
      custom.lastIndex = 0;
      if (custom.test(line)) return 'chapter';
    }
    if (RE_VOLUME.test(line)) return 'volume';
    if (RE_CHAPTER.test(line)) return 'chapter';
    if (RE_SPECIAL.test(line)) return 'chapter';
    if (RE_EN.test(line)) return 'chapter';
    return null;
  }

  /* ---------- 无章节标题时的兜底切分 ---------- */
  function fallbackChapters(text, size) {
    size = size || 3000;
    var chapters = [], pos = 0, n = 1;
    while (pos < text.length && chapters.length < 4000) {
      var end = Math.min(text.length, pos + size);
      if (end < text.length) {
        var cut = text.lastIndexOf('\n', end);
        if (cut > pos + Math.floor(size * 0.5)) end = cut;
      }
      chapters.push({ title: '第' + n + '节', start: pos, end: end, isVolume: 0, isFallback: 1 });
      pos = end;
      n++;
    }
    return chapters;
  }

  /* ---------- 章节解析 ---------- */
  function parseChapters(text, options) {
    options = options || {};
    text = text || '';
    var custom = null;
    if (options.customRule) {
      try { custom = new RegExp(options.customRule); } catch (e) { custom = null; }
    }

    var found = [];
    var i = 0, len = text.length;
    while (i < len && found.length < 20000) {
      var nl = text.indexOf('\n', i);
      var lineEnd = nl === -1 ? len : nl;
      var lineLen = lineEnd - i;
      if (lineLen > 0 && lineLen <= TITLE_MAX) {
        var line = cleanLine(text.slice(i, lineEnd));
        var kind = isTitleLine(line, custom);
        if (kind) {
          found.push({
            title: line,
            titleStart: i,
            contentStart: lineEnd + 1,
            isVolume: kind === 'volume' ? 1 : 0
          });
        }
      }
      if (nl === -1) break;
      i = nl + 1;
    }

    var chapters = [];
    var mode = 'regex';
    if (found.length >= 2) {
      for (var k = 0; k < found.length; k++) {
        chapters.push({
          title: found[k].title,
          start: found[k].contentStart,
          end: (k + 1 < found.length) ? found[k + 1].titleStart : len,
          isVolume: found[k].isVolume
        });
      }
      /* 第一个标题之前的内容：够长就作为「前言」 */
      var headEnd = found[0].titleStart;
      if (headEnd > 0) {
        var headText = text.slice(0, headEnd).replace(/[\s\u3000]+/g, '');
        if (headText.length > 200) {
          chapters.unshift({ title: '前言', start: 0, end: headEnd, isVolume: 0, isFront: 1 });
        }
      }
    } else {
      chapters = fallbackChapters(text, options.fallbackSize);
      mode = 'fallback';
    }

    var words = countWords(text);
    return {
      chapters: chapters,
      mode: mode,
      count: chapters.length,
      words: words
    };
  }

  /* ---------- 工具 ---------- */
  function countWords(text) {
    if (!text) return 0;
    return text.replace(/[\s\u3000]/g, '').length;
  }

  function chapterTitle(chapter, index) {
    if (chapter && chapter.title) return chapter.title;
    return '第' + (index + 1) + '章';
  }

  function excerptText(text, max) {
    max = max || 120;
    if (!text) return '';
    var s = text.replace(/[\s\u3000]+/g, ' ').replace(/^ | $/g, '');
    return s.length > max ? s.slice(0, max) + '…' : s;
  }

  /* 依据文件名猜测书名与作者 */
  function parseFileName(fileName) {
    var base = String(fileName || '').replace(/\.[^.]+$/, '');
    var name = base, author = '';
    var m;
    if ((m = base.match(/^《(.+?)》\s*(.*)$/))) {
      name = m[1];
      author = m[2] || '';
    } else if ((m = base.match(/^(.*?)\s*[\[\(【（]\s*(?:作者[:：]?)?\s*(.{1,20}?)\s*[\]\)】）]\s*$/))) {
      name = m[1];
      author = m[2];
      if (RE_NOISE_AUTHOR.test(author.replace(/\s/g, ''))) author = '';
    } else if ((m = base.match(/^(.*?)\s*[-—_|]\s*(.{1,20})$/))) {
      name = m[1];
      author = m[2];
    }
    if ((m = name.match(/^(.*?)\s*(?:作者|著)[:：]\s*(.+)$/))) {
      name = m[1];
      author = m[2];
    } else if ((m = base.match(/作者[:：]\s*(.{1,20})$/))) {
      author = m[1];
      name = base.replace(/作者[:：]\s*.{1,20}$/, '');
    }
    name = name.replace(/[（(【\[](全本|完结|全集|精校|校对|未删减|下载|txt|TXT|小说|全文)[)）】\]]/g, '').replace(/[\s_-]+$/, '').trim();
    author = author.replace(/[（(【\[](全本|完结|精校|下载)[)）】\]]/g, '').trim();
    if (!name) name = base || '未命名';
    return { name: name, author: author };
  }

  return {
    decodeText: decodeText,
    detectEncoding: detectEncoding,
    explainEncoding: explainEncoding,
    normalizeText: normalizeText,
    parseChapters: parseChapters,
    countWords: countWords,
    chapterTitle: chapterTitle,
    excerptText: excerptText,
    parseFileName: parseFileName,
    TITLE_MAX: TITLE_MAX
  };
});
