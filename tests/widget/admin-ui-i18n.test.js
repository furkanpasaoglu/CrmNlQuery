const assert = require('assert');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', '..');
const ui = path.join(root, 'admin-ui', 'src');
/** Reads and parses a file used by the test. */
const read = (p) => JSON.parse(fs.readFileSync(p, 'utf8'));

/** Merges the catalog entries used by the test. */
function merged(lang) {
  const out = Object.assign({}, read(path.join(ui, 'i18n', lang + '.json')));
  const extra = path.join(ui, 'i18n', 'extra');
  fs.readdirSync(extra).filter((f) => f.endsWith('.' + lang + '.json')).sort()
    .forEach((f) => Object.assign(out, read(path.join(extra, f))));
  return out;
}
const en = merged('en');
const tr = merged('tr');

/** Extracts the placeholders from a catalog string for the parity check. */
const placeholders = (text) => [...String(text).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');
const enKeys = Object.keys(en);
assert.ok(enKeys.length > 500, 'panel katalogu okunamadi');
assert.deepStrictEqual(enKeys.filter((k) => !(k in tr)), [], 'TR cevirisi eksik anahtarlar');
assert.deepStrictEqual(Object.keys(tr).filter((k) => !(k in en)), [], "TR'de olup EN'de olmayan anahtarlar");
enKeys.forEach((k) => {
  assert.ok(String(en[k]).trim().length > 0, 'EN bos: ' + k);
  assert.ok(String(tr[k]).trim().length > 0, 'TR bos: ' + k);
  assert.strictEqual(placeholders(tr[k]), placeholders(en[k]), 'parametre uyusmazligi: ' + k);
});

/** Walks the source tree to collect the files to check. */
function walk(dir, acc) {
  fs.readdirSync(dir, { withFileTypes: true }).forEach((e) => {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walk(full, acc); else if (/\.(tsx?)$/.test(e.name)) acc.push(full);
  });
  return acc;
}
const used = new Map();
walk(ui, []).forEach((file) => {
  const text = fs.readFileSync(file, 'utf8');
  for (const m of text.matchAll(/\bt(?:Or)?\(\s*['"]([A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)+)['"]\s*[,)]/g)) used.set(m[1], path.basename(file));
});
const missingUsed = [...used.keys()].filter((k) => !(k in en));
assert.deepStrictEqual(missingUsed, [], 'katalogda olmayan anahtarlar: ' + missingUsed.map((k) => k + ' (' + used.get(k) + ')').join(', '));
console.log('admin-ui-i18n testi gecti (' + enKeys.length + ' anahtar, ' + used.size + ' kullanilan anahtar)');

/** Reads a C# source file by its file name from anywhere under src, so folder moves do not break the check. */
function readSource(fileName) {
  const found = [];
  const visit = (dir) => fs.readdirSync(dir, { withFileTypes: true }).forEach((e) => {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) { if (!/^(bin|obj|wwwroot|node_modules)$/.test(e.name)) visit(full); } else if (e.name === fileName) found.push(full);
  });
  visit(path.join(root, 'src'));
  assert.strictEqual(found.length, 1, fileName + ' tam bir kez bulunmali: ' + found.join(', '));
  return fs.readFileSync(found[0], 'utf8');
}

const catalogCs = readSource('SettingsCatalog.cs');
const consts = {};
for (const m of catalogCs.matchAll(/public const string (\w+) = "([^"]+)";/g)) consts[m[1]] = m[2];
const settingKeys = [...catalogCs.matchAll(/new SettingDefinition\((?:"([^"]+)"|(\w+))\s*,/g)].map((m) => m[1] || consts[m[2]]);
assert.ok(settingKeys.length > 30 && settingKeys.every(Boolean), 'ayar anahtarlari okunamadi');
const missingSettings = settingKeys.filter((k) => !(('setting.' + k) in tr));
assert.deepStrictEqual(missingSettings, [], 'TR aciklamasi olmayan ayarlar: ' + missingSettings.join(', '));
console.log('ayar aciklamalari tamam (' + settingKeys.length + ' ayar)');

const permCs = readSource('Permissions.cs');
const permKeys = [...permCs.matchAll(/public const string \w+ = "([A-Za-z]+\.[A-Za-z]+)";/g)].map((m) => m[1]);
assert.ok(permKeys.length >= 14, 'izin anahtarlari okunamadi');
const missingPerms = permKeys.filter((k) => !(('perm.' + k) in tr));
assert.deepStrictEqual(missingPerms, [], 'TR aciklamasi olmayan izinler: ' + missingPerms.join(', '));
['Overview', 'Administration', 'Knowledge', 'Application'].forEach((g) => assert.ok(('shell.group' + g) in tr, 'izin grubu cevirisi yok: ' + g));
console.log('izin aciklamalari tamam (' + permKeys.length + ' izin)');

const exportCs = readSource('ExportSchema.cs');
const colNames = new Set([...exportCs.matchAll(/new ExportColumn\("(\w+)"/g)].map((m) => m[1]));
assert.ok(colNames.size >= 20, 'export sutunlari okunamadi');
const trCols = Object.keys(tr).filter((k) => k.startsWith('exportcol.')).map((k) => k.split('.').pop());
const untranslatedCols = [...colNames].filter((n) => !trCols.includes(n));
assert.deepStrictEqual(untranslatedCols, [], 'aciklamasi cevrilmeyen export sutunlari: ' + untranslatedCols.join(', '));
console.log('export sutun aciklamalari tamam (' + colNames.size + ' sutun adi)');

const navText = fs.readFileSync(path.join(ui, 'nav.tsx'), 'utf8');
const pageIds = [...navText.matchAll(/\{ id: '([a-z-]+)', titleKey:/g)].map((m) => m[1]).filter((id) => !['overview', 'access', 'data', 'quality', 'tools', 'system'].includes(id) || navText.includes("id: '" + id + "', titleKey: '" + id));
assert.ok(pageIds.length >= 17, 'nav sayfalari okunamadi: ' + pageIds.length);
const missingHow = [];
for (const id of pageIds) {
  for (const [lang, cat] of [['en', en], ['tr', tr]]) {
    const need = ['what', 's1', 's2', 'exTitle', 'r1l', 'r1v', 'tip'];
    need.forEach((s) => { if (!String(cat['howto.' + id + '.' + s] || '').trim()) missingHow.push(lang + ':' + id + '.' + s); });
  }
}
assert.deepStrictEqual(missingHow, [], 'Nasil calisir anlatimi eksik: ' + missingHow.join(', '));
console.log('nasil calisir anlatimlari tamam (' + pageIds.length + ' sayfa)');
