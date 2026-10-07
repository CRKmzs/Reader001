/* 解析器单元测试：node tests/parser.test.mjs */
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';

const require = createRequire(import.meta.url);
const Parser = require('../js/parser.js');

let passed = 0;
const failures = [];
function test(name, fn) {
  try { fn(); passed++; console.log('  ok  ' + name); }
  catch (e) { failures.push(name + ' -> ' + e.message); console.log('  FAIL ' + name + '\n       ' + e.message); }
}
const enc = (s) => new TextEncoder().encode(s);
function gbkBytes(pairs) {
  const arr = [];
  for (const p of pairs) {
    if (typeof p === 'string') { for (const ch of p) arr.push(ch.charCodeAt(0)); }
    else { arr.push(p >> 8, p & 0xff); }
  }
  return new Uint8Array(arr);
}

test('UTF-8 无 BOM 解码', () => {
  const r = Parser.decodeText(enc('第一章 少年\n正文开始。'));
  assert.equal(r.encoding, 'utf-8');
  assert.equal(r.text, '第一章 少年\n正文开始。');
});

test('UTF-8 BOM 会被剥离', () => {
  const bytes = new Uint8Array([0xEF, 0xBB, 0xBF, ...enc('第一章 少年')]);
  const r = Parser.decodeText(bytes);
  assert.equal(r.text, '第一章 少年');
  assert.equal(r.hadBom, true);
});

test('GBK/GB18030 自动识别', () => {
  const bytes = gbkBytes([0xB5DA, 0xD2BB, 0xD5C2, ' ', 0xB2E2, 0xCAD4, '\n', 0xD6D0, 0xCEC4, 0xC4DA, 0xC8DD, 0xA1A3]);
  const r = Parser.decodeText(bytes);
  assert.ok(r.encoding === 'gb18030' || r.encoding === 'gbk', 'encoding=' + r.encoding);
  assert.equal(r.text, '第一章 测试\n中文内容。');
});

test('CRLF 归一化', () => {
  const r = Parser.decodeText(enc('第一章\r\n内容\r\n'));
  assert.equal(r.text, '第一章\n内容\n');
});

const novel = [
  /* 首标题前 > 200 字才会被识别为「前言」 */
  '本书简介：这是简介部分，用来测试前言识别。'.repeat(12),
  '第一章 少年',
  '陈砚抬起头。'.repeat(20),
  '第二章 白鹭洲',
  '江风吹过水面。'.repeat(20),
  '第三章 旧信',
  '钥匙是铜的。'.repeat(20)
].join('\n');

test('章节解析：基本识别', () => {
  const r = Parser.parseChapters(novel);
  assert.equal(r.mode, 'regex');
  assert.equal(r.chapters.length, 4); // 前言 + 3 章
  assert.equal(r.chapters[0].title, '前言');
  assert.equal(r.chapters[1].title, '第一章 少年');
  assert.equal(r.chapters[3].title, '第三章 旧信');
  const body1 = novel.slice(r.chapters[1].start, r.chapters[1].end);
  assert.ok(body1.indexOf('陈砚抬起头') === 0, 'chapter1 body starts wrong: ' + JSON.stringify(body1.slice(0, 12)));
  assert.ok(body1.indexOf('第二章') === -1, 'chapter1 should not contain next title');
});

test('章节解析：卷标题标记为 volume', () => {
  const text = '第一卷 少年游\n第一章 出发\n正文甲\n第二章 归途\n正文乙\n第二卷 北行\n第三章 风雪\n正文丙';
  const r = Parser.parseChapters(text);
  assert.equal(r.chapters.length, 5);
  assert.equal(r.chapters[0].isVolume, 1);
  assert.equal(r.chapters[0].title, '第一卷 少年游');
  assert.equal(r.chapters[3].isVolume, 1);
  assert.equal(r.chapters[3].title, '第二卷 北行');
});

test('章节解析：中文数字与序章/番外/Chapter', () => {
  const text = [
    '序章 起源', '内容一'.repeat(30),
    '第一百二十三章 归乡', '内容二'.repeat(30),
    '番外 那年夏天', '内容三'.repeat(30),
    'Chapter 4 The Road', 'content'.repeat(30)
  ].join('\n');
  const r = Parser.parseChapters(text);
  const titles = r.chapters.map((c) => c.title);
  assert.deepEqual(titles, ['序章 起源', '第一百二十三章 归乡', '番外 那年夏天', 'Chapter 4 The Road']);
});

test('章节解析：正文里的“第一章”不会被误判', () => {
  const longLine = '我翻开书，看到第一章 少年这一节的标题，忽然想起很多年前的往事，那时候天还很蓝，河水还很清，我们坐在岸边看云。';
  const text = ['第一章 少年', longLine, '第二章 归途', '内容'.repeat(30)].join('\n');
  const r = Parser.parseChapters(text);
  assert.equal(r.chapters.length, 2);
  assert.ok(r.text === undefined || true);
});

test('章节解析：无标题时兜底切分', () => {
  const text = Array.from({ length: 40 }, (_, i) => '第' + (i + 1) + '段：' + '这是一段没有任何章节标记的文字。'.repeat(6)).join('\n');
  const r = Parser.parseChapters(text);
  assert.equal(r.mode, 'fallback');
  assert.ok(r.chapters.length >= 2, 'chapters=' + r.chapters.length);
  assert.equal(r.chapters[0].title, '第1节');
  assert.ok(r.chapters[0].end - r.chapters[0].start <= 3000 + 200);
});

test('章节解析：自定义正则', () => {
  const text = ['【一】起', '内容甲'.repeat(30), '【二】承', '内容乙'.repeat(30), '【三】转', '内容丙'.repeat(30)].join('\n');
  const r = Parser.parseChapters(text, { customRule: '^【[一二三四五六七八九十]+】.{0,20}$' });
  assert.equal(r.mode, 'regex');
  assert.deepEqual(r.chapters.map((c) => c.title), ['【一】起', '【二】承', '【三】转']);
});

test('章节解析：非法自定义正则会退化为默认规则', () => {
  const text = ['第一章 甲', '内容'.repeat(40), '第二章 乙', '内容'.repeat(40)].join('\n');
  const r = Parser.parseChapters(text, { customRule: '([' });
  assert.equal(r.chapters.length, 2);
});

test('字数统计忽略空白', () => {
  assert.equal(Parser.countWords('一二三 四五\n 六'), 6);
  assert.equal(Parser.countWords(''), 0);
});

test('摘要截断', () => {
  assert.equal(Parser.excerptText('  abc   def ', 5), 'abc d…');
  assert.equal(Parser.excerptText('abc', 10), 'abc');
});

test('文件名解析', () => {
  assert.deepEqual(Parser.parseFileName('《九州缥缈录》江南.txt'), { name: '九州缥缈录', author: '江南' });
  assert.deepEqual(Parser.parseFileName('斗破苍穹 - 天蚕土豆.txt'), { name: '斗破苍穹', author: '天蚕土豆' });
  assert.deepEqual(Parser.parseFileName('凡人修仙传（全本）.txt'), { name: '凡人修仙传', author: '' });
  assert.deepEqual(Parser.parseFileName('《三体》刘慈欣.txt'), { name: '三体', author: '刘慈欣' });
  assert.deepEqual(Parser.parseFileName('斗破苍穹 作者：天蚕土豆.txt'), { name: '斗破苍穹', author: '天蚕土豆' });
  assert.deepEqual(Parser.parseFileName('随便一个名字.txt'), { name: '随便一个名字', author: '' });
});

console.log('\n解析器测试：' + passed + ' 通过，' + failures.length + ' 失败');
if (failures.length) { console.log(failures.join('\n')); process.exit(1); }
