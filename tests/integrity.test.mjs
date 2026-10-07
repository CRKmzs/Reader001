/* 完整性测试：资源存在 / el(id) 引用 / data-* 约定 / JS 语法 / CSS 括号
   node tests/integrity.test.mjs */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const errors = [];
const warnings = [];
const read = (p) => fs.readFileSync(path.join(root, p), 'utf8');

const html = read('index.html');
const jsFiles = fs.readdirSync(path.join(root, 'js')).filter((f) => f.endsWith('.js')).sort();
const cssFiles = fs.readdirSync(path.join(root, 'css')).filter((f) => f.endsWith('.css')).sort();

/* 1. 资源引用存在性 */
for (const m of html.matchAll(/<script[^>]+src="([^"]+)"/g)) {
  if (!fs.existsSync(path.join(root, m[1]))) errors.push('index.html 引用的脚本不存在: ' + m[1]);
}
for (const m of html.matchAll(/<link[^>]+href="([^"]+)"/g)) {
  if (!fs.existsSync(path.join(root, m[1]))) errors.push('index.html 引用的样式不存在: ' + m[1]);
}

/* 2. JS 语法检查（浏览器脚本按脚本解析） */
for (const f of jsFiles) {
  const src = read('js/' + f);
  try { new vm.Script(src, { filename: f }); }
  catch (e) { errors.push('js/' + f + ' 语法错误: ' + e.message); }
  if (/^\s*import\s/m.test(src) || /^\s*export\s/m.test(src)) warnings.push('js/' + f + ' 含 ESM 语法，浏览器中需以 module 方式加载');
}

/* 3. el('id') / getElementById 引用必须在 HTML 中存在 */
const htmlIds = new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]));
for (const f of jsFiles) {
  const src = read('js/' + f);
  const refs = new Set();
  for (const m of src.matchAll(/el\('([A-Za-z0-9_-]+)'\)/g)) refs.add(m[1]);
  for (const m of src.matchAll(/getElementById\('([A-Za-z0-9_-]+)'\)/g)) refs.add(m[1]);
  for (const id of refs) if (!htmlIds.has(id)) errors.push('js/' + f + ' 引用了不存在的元素 id: ' + id);
}

/* 4. JS 里用到的 data-* 选择器必须在 HTML 或 JS 生成代码里出现 */
const allJs = jsFiles.map((f) => read('js/' + f)).join('\n');
const dataAttrs = new Set();
for (const m of allJs.matchAll(/data-([a-z0-9-]+)/g)) dataAttrs.add('data-' + m[1]);
/* HTML 里写作 attr="x"，JS 里可能写作 attr="x" 或 setAttribute('attr', ...) */
const attrUsed = (src, attr) =>
  new RegExp(attr + '=').test(src) ||
  new RegExp("'" + attr + "'").test(src) ||
  new RegExp('"' + attr + '"').test(src);
for (const attr of dataAttrs) {
  if (!attrUsed(html, attr) && !attrUsed(allJs, attr)) {
    errors.push('data 属性在 HTML 与 JS 中都找不到: ' + attr);
  }
}

/* 5. HTML 中出现的关键 id 是否都被 JS 绑定（提示级） */
const idRefs = new Set();
for (const m of allJs.matchAll(/el\('([A-Za-z0-9_-]+)'\)/g)) idRefs.add(m[1]);
for (const m of allJs.matchAll(/#([A-Za-z][A-Za-z0-9_-]*)/g)) idRefs.add(m[1]);
for (const m of allJs.matchAll(/getElementById\('([A-Za-z0-9_-]+)'\)/g)) idRefs.add(m[1]);
const svgRefs = new Set([...html.matchAll(/url\(#([A-Za-z0-9_-]+)\)/g)].map((m) => m[1]));
for (const id of htmlIds) {
  if (!idRefs.has(id) && !svgRefs.has(id) && !/^view|^page/.test(id)) {
    warnings.push('HTML 中的 id 未被 JS 使用: ' + id);
  }
}

/* 6. aria-labelledby 指向的 id 是否存在 */
for (const m of html.matchAll(/aria-labelledby="([^"]+)"/g)) {
  for (const id of m[1].split(/\s+/)) if (!htmlIds.has(id)) errors.push('aria-labelledby 指向不存在的 id: ' + id);
}

/* 7. CSS 花括号配平 + 选择器里用到的类至少被定义 */
for (const f of cssFiles) {
  const src = read('css/' + f);
  const open = (src.match(/{/g) || []).length;
  const close = (src.match(/}/g) || []).length;
  if (open !== close) errors.push('css/' + f + ' 花括号不匹配: { = ' + open + ', } = ' + close);
}

/* 8. HTML 类名与 CSS/JS 对照（仅提示） */
const allCss = cssFiles.map((f) => read('css/' + f)).join('\n');
const htmlClasses = new Set();
for (const m of html.matchAll(/class="([^"]+)"/g)) m[1].split(/\s+/).forEach((c) => c && htmlClasses.add(c));
for (const c of htmlClasses) {
  const re = new RegExp('\\.' + c.replace(/[-]/g, '\\-') + '(?![A-Za-z0-9_-])');
  if (!re.test(allCss)) warnings.push('HTML 类名在 CSS 中没有规则: ' + c);
}

console.log('检查文件: index.html + ' + jsFiles.length + ' 个 JS + ' + cssFiles.length + ' 个 CSS');
if (warnings.length) console.log('\n提示 (' + warnings.length + '):\n  ' + warnings.join('\n  '));
if (errors.length) {
  console.log('\n错误 (' + errors.length + '):\n  ' + errors.join('\n  '));
  process.exit(1);
}
console.log('\n完整性检查通过 ✓');
