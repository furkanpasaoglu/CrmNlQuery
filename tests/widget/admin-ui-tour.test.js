const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const root = path.join(__dirname, '..', '..');
const ui = path.join(root, 'admin-ui', 'src');

/** Merges the translation catalogs for a language. */
function merged(lang) {
  const out = Object.assign({}, JSON.parse(fs.readFileSync(path.join(ui, 'i18n', lang + '.json'), 'utf8')));
  const extra = path.join(ui, 'i18n', 'extra');
  fs.readdirSync(extra).filter((f) => f.endsWith('.' + lang + '.json')).sort().forEach((f) => Object.assign(out, JSON.parse(fs.readFileSync(path.join(extra, f), 'utf8'))));
  return out;
}
/** Walks a directory recursively collecting files. */
function walk(dir, acc) {
  fs.readdirSync(dir, { withFileTypes: true }).forEach((e) => {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walk(full, acc); else if (/\.tsx?$/.test(e.name)) acc.push(full);
  });
  return acc;
}

(async () => {
  const { TOURS } = await import(pathToFileURL(path.join(ui, 'tour', 'tours.ts')).href);
  const en = merged('en'), tr = merged('tr');
  const source = walk(ui, []).map((f) => fs.readFileSync(f, 'utf8')).join('\n');

  const nav = fs.readFileSync(path.join(ui, 'nav.tsx'), 'utf8');
  const pageIds = new Set([...nav.matchAll(/\{ id: '([a-z-]+)', titleKey:/g)].map((m) => m[1]));
  Object.keys(TOURS).forEach((id) => assert.ok(id === 'welcome' || pageIds.has(id), 'bilinmeyen tur anahtari: ' + id));
  assert.ok(TOURS.welcome && TOURS.welcome.length >= 5, 'karsilama turu eksik');
  const noTour = [...pageIds].filter((id) => !TOURS[id]);
  assert.deepStrictEqual(noTour, [], 'turu olmayan sayfalar: ' + noTour.join(', '));

  const problems = [];
  const targets = new Set();
  for (const [tour, steps] of Object.entries(TOURS)) {
    assert.ok(steps.length >= 2, tour + ' turu cok kisa');
    const ids = new Set();
    for (const s of steps) {
      if (ids.has(s.id)) problems.push(tour + '.' + s.id + ': yinelenen adim');
      ids.add(s.id);
      for (const [lang, cat] of [['en', en], ['tr', tr]]) {
        for (const part of ['title', 'text']) {
          const k = `tour.${tour}.${s.id}.${part}`;
          if (!String(cat[k] || '').trim()) problems.push(lang + ': ' + k);
        }
      }
      if (s.target) targets.add(s.target);
    }
  }
  assert.deepStrictEqual(problems, [], 'tur metni eksik/yinelenen: ' + problems.join(', '));

  const missing = [...targets].filter((t) => !source.includes(`data-tour="${t}"`) && !source.includes(`'data-tour': '${t}'`));
  assert.deepStrictEqual(missing, [], 'kodda data-tour hedefi olmayan adimlar: ' + missing.join(', '));

  ['step', 'skip', 'back', 'next', 'done', 'help', 'pageTour', 'noPageTour', 'welcomeTour'].forEach((k) => {
    assert.ok(en['tour.ui.' + k] && tr['tour.ui.' + k], 'tour.ui.' + k);
  });

  const stepCount = Object.values(TOURS).reduce((n, s) => n + s.length, 0);
  console.log('admin-ui-tour testi gecti (' + Object.keys(TOURS).length + ' tur, ' + stepCount + ' adim, ' + targets.size + ' hedef)');
})().catch((e) => { console.error(e); process.exit(1); });
