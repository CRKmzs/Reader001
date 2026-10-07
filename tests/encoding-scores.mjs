import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const Parser = require('../js/parser.js');
const dir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'encoding-samples');
for (const f of fs.readdirSync(dir).filter((x) => x.endsWith('.bin')).sort()) {
  const bytes = new Uint8Array(fs.readFileSync(path.join(dir, f)));
  const r = Parser.explainEncoding(bytes);
  const sc = Object.keys(r.scores).map((k) => k + '=' + r.scores[k].toFixed(3)).join(' ');
  console.log(f.padEnd(26), '->', r.label.padEnd(10), sc);
}
