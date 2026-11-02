const assert = require('assert');
const fs = require('fs');
const path = require('path');

const wwwroot = path.join(__dirname, '..', '..', 'src', 'CrmNlQuery.Web', 'wwwroot');
const source = fs.readFileSync(path.join(wwwroot, 'result.js'), 'utf8');

/** Extracts a named block from the source text. */
function block(name) {
  const start = source.indexOf('const ' + name + ' = {');
  assert.ok(start >= 0, name + ' blogu yok');
  return source.slice(start, source.indexOf('\n};', start));
}

const ENTRY = /^  (\w+): "((?:[^"\\]|\\.)*)"/gm;
/** Parses translation file text into a key-to-value object, unescaping quoted and backslash characters in the values. */
function entries(text) {
  const out = {};
  for (const m of text.matchAll(ENTRY)) {
    out[m[1]] = m[2].replace(/\\"/g, '"').replace(/\\\\/g, '\\');
  }
  return out;
}

const en = entries(block('T_EN'));
const tr = entries(block('T_TR'));
/** Extracts the sorted list of placeholder names in a string. */
const placeholders = (s) => [...String(s).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');

assert.ok(Object.keys(en).length > 30, 'EN katalogu bos gorunuyor');
assert.deepStrictEqual(Object.keys(en).filter((k) => !(k in tr)), [], 'TR cevirisi eksik');
assert.deepStrictEqual(Object.keys(tr).filter((k) => !(k in en)), [], "TR'de olup EN'de olmayan");
Object.keys(en).forEach((k) => {
  assert.ok(en[k].trim() && tr[k].trim(), 'bos metin: ' + k);
  assert.strictEqual(placeholders(tr[k]), placeholders(en[k]), 'parametre uyusmazligi: ' + k);
});

const used = new Set([...source.matchAll(/\bt\("(\w+)"/g)].map((m) => m[1]));
['countOf', 'countMore'].forEach((k) => used.add(k));
assert.deepStrictEqual([...used].filter((k) => !(k in en)), [], 'katalogda olmayan anahtar');
assert.deepStrictEqual(Object.keys(en).filter((k) => !used.has(k)), [], 'kullanilmayan anahtar (silin ya da kullanin)');

const widget = fs.readFileSync(path.join(wwwroot, 'widget', 'widget.js'), 'utf8');
/** Cuts the explainText block out of a script for comparison. */
const cut = (s) => s.slice(s.indexOf('// BEGIN explainText'), s.indexOf('// END explainText')).replace(/\r\n/g, '\n');
assert.strictEqual(cut(source), cut(widget), 'explainText bloklari ayni olmali');

console.log('result-i18n testi gecti (' + Object.keys(en).length + ' anahtar)');
