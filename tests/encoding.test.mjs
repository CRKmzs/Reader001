/* 编码解码测试：node tests/encoding.test.mjs
   依赖 tests/encoding-samples.py 先生成样本（用 python 标准库造出 GBK/Big5/UTF-16 等字节） */
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const Parser = require('../js/parser.js');
const dir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'encoding-samples');

let passed = 0;
const failures = [];
function test(name, fn) {
  try { fn(); passed++; console.log('  ok  ' + name); }
  catch (e) { failures.push(name + ' -> ' + e.message); console.log('  FAIL ' + name + '\n       ' + e.message); }
}
const bin = (n) => new Uint8Array(fs.readFileSync(path.join(dir, n + '.bin')));
const expected = (n) => fs.readFileSync(path.join(dir, n + '.expected.txt'), 'utf8');
const countOf = (s, ch) => s.split(ch).length - 1;

if (!fs.existsSync(dir)) {
  console.error('缺少样本目录，请先运行: python tests/encoding-samples.py');
  process.exit(2);
}

test('UTF-8 无 BOM', () => {
  const r = Parser.decodeText(bin('utf8'));
  assert.equal(r.encoding, 'utf-8');
  assert.equal(r.text, expected('utf8'));
});

test('UTF-8 BOM', () => {
  const r = Parser.decodeText(bin('utf8_bom'));
  assert.equal(r.encoding, 'utf-8');
  assert.equal(r.hadBom, true);
  assert.equal(r.text, expected('utf8_bom'));
});

test('UTF-8 中文标点', () => {
  assert.equal(Parser.decodeText(bin('utf8_punct')).text, expected('utf8_punct'));
});

test('超过 1MB 的 UTF-8 不会被误判为 GB18030', () => {
  const r = Parser.decodeText(bin('utf8_long'));
  assert.equal(r.encoding, 'utf-8');
  assert.equal(r.text, expected('utf8_long'));
});

test('GBK', () => {
  const r = Parser.decodeText(bin('gbk'));
  assert.ok(r.encoding === 'gb18030' || r.encoding === 'gbk', r.encoding);
  assert.equal(r.text, expected('gbk'));
});

test('超过 1MB 的 GBK', () => {
  const r = Parser.decodeText(bin('gbk_long'));
  assert.equal(r.text, expected('gbk_long'));
});

test('GB18030 生僻字', () => {
  const r = Parser.decodeText(bin('gb18030'));
  assert.equal(r.text, expected('gb18030'));
});

test('Big5 繁体', () => {
  const r = Parser.decodeText(bin('big5'));
  assert.equal(r.text, expected('big5'));
});

test('UTF-16LE 无 BOM', () => {
  const r = Parser.decodeText(bin('utf16le'));
  assert.equal(r.encoding, 'utf-16le');
  assert.equal(r.text, expected('utf16le'));
});

test('UTF-16BE 无 BOM', () => {
  const r = Parser.decodeText(bin('utf16be'));
  assert.equal(r.encoding, 'utf-16be');
  assert.equal(r.text, expected('utf16be'));
});

test('UTF-16LE 带 BOM', () => {
  const r = Parser.decodeText(bin('utf16le_bom'));
  assert.equal(r.encoding, 'utf-16le');
  assert.equal(r.hadBom, true);
  assert.equal(r.text, expected('utf16le_bom'));
});

test('单个损坏字节的 UTF-8 仍按 UTF-8 解码', () => {
  const r = Parser.decodeText(bin('utf8_one_bad_byte'));
  assert.equal(r.encoding, 'utf-8');
  assert.ok(r.text.indexOf('第一章 少年归来') === 0, r.text.slice(0, 12));
  assert.ok(countOf(r.text, '\uFFFD') <= 4, '替换符过多: ' + countOf(r.text, '\uFFFD'));
});

test('空文件不明文报错', () => {
  const r = Parser.decodeText(new Uint8Array(0));
  assert.equal(r.text, '');
});

console.log('\n编码测试：' + passed + ' 通过，' + failures.length + ' 失败');
if (failures.length) { for (const f of failures) console.log('  - ' + f); process.exit(1); }
