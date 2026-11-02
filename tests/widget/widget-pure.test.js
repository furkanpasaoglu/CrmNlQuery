const assert = require('assert');
const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'CrmNlQuery.Web', 'wwwroot', 'widget', 'widget.js'), 'utf8');
/** Extracts a named function source from the script text for testing. */
function extract(name) {
  const m = source.match(new RegExp('BEGIN ' + name + '([\\s\\S]*?)// END ' + name));
  assert.ok(m, name + ' blogu bulunamadi');
  return new Function(m[1] + '; return ' + name + ';')();
}

const parseRecord = extract('parseRecord');
const guid = '11111111-2222-3333-4444-555555555555';
assert.deepStrictEqual(parseRecord('http://h/crm/main.aspx?etn=account&pagetype=entityrecord&id=%7b' + guid.toUpperCase() + '%7d'), { entity: 'account', id: guid });
assert.deepStrictEqual(parseRecord('http://h/m?appid=1&pagetype=entityrecord&etn=Opportunity&id=' + guid), { entity: 'opportunity', id: guid });
assert.strictEqual(parseRecord('http://h/m?etn=account&id=zzz'), null);
assert.strictEqual(parseRecord('http://h/m?etn=a;b&id=' + guid), null);
assert.strictEqual(parseRecord('http://h/userdefined/edit.aspx?etc=1&id=%7b' + guid + '%7d'), null);
assert.strictEqual(parseRecord('garbage'), null);

const stopPlan = extract('stopPlan');
assert.deepStrictEqual(stopPlan(false, 'Londra firmalari'), { restoreText: 'Londra firmalari', removeYou: true });
assert.deepStrictEqual(stopPlan(true, 'ignored'), { restoreText: null, removeYou: true });
assert.deepStrictEqual(stopPlan(false, ''), { restoreText: null, removeYou: true });

const tooSoonAfterStop = extract('tooSoonAfterStop');
assert.strictEqual(tooSoonAfterStop(1000, 800), true);
assert.strictEqual(tooSoonAfterStop(1600, 1000), false);
assert.strictEqual(tooSoonAfterStop(5000, 0), false);

const draftMailto = extract('draftMailto');
const mail = draftMailto('ayse@example.test', 'Konu & ozet', 'Satir 1\nSatir 2', 1800);
assert.strictEqual(mail.tooLong, false);
assert.ok(mail.href.startsWith('mailto:ayse@example.test?subject=Konu%20%26%20ozet&body='), mail.href);
assert.ok(mail.href.endsWith('Satir%201%0D%0ASatir%202'), 'satir sonlari CRLF olmali');
assert.strictEqual(draftMailto('x@y.test?cc=evil@z.test', 'S', 'B', 1800).href.startsWith('mailto:?subject='), true, 'sade olmayan adres kullanilmamali');
assert.strictEqual(draftMailto('', 'S', 'B', 1800).href.startsWith('mailto:?subject='), true);
const longMail = draftMailto('ayse@example.test', 'S', 'x'.repeat(3000), 1800);
assert.strictEqual(longMail.tooLong, true);
assert.ok(longMail.href.length > 3000, 'uzun govde KESILMEMELI');

const draftParts = extract('draftParts');
assert.deepStrictEqual(draftParts('Merhaba [your name], selam [your name]', ['[your name]']), [
  { text: 'Merhaba ', mark: false }, { text: '[your name]', mark: true }, { text: ', selam ', mark: false }, { text: '[your name]', mark: true }]);
assert.deepStrictEqual(draftParts('Duz metin', []), [{ text: 'Duz metin', mark: false }]);
assert.deepStrictEqual(draftParts('', ['[x]']), []);

const parseSlash = extract('parseSlash');
assert.deepStrictEqual(parseSlash('/'), { open: true, query: '' });
assert.deepStrictEqual(parseSlash('/He'), { open: true, query: 'he' });
assert.deepStrictEqual(parseSlash('/new'), { open: true, query: 'new' });
['1/2', '10/2026', 'a /b', '/new x', ' /new', '', '//', '/1'].forEach(function (text) {
  assert.deepStrictEqual(parseSlash(text), { open: false, query: '' }, 'menu acilmamali: "' + text + '"');
});
const filterCommands = extract('filterCommands');
const cmds = [
  { name: 'help', available: () => true }, { name: 'new', available: (c) => c.hasChat },
  { name: 'edit', available: (c) => c.hasQuestion }, { name: 'history', available: () => true }, { name: 'clear', available: () => true }
];
assert.deepStrictEqual(filterCommands(cmds, '', { hasChat: true, hasQuestion: true }).map(c => c.name), ['help', 'new', 'edit', 'history', 'clear']);
assert.deepStrictEqual(filterCommands(cmds, 'h', { hasChat: true, hasQuestion: true }).map(c => c.name), ['help', 'history']);
assert.deepStrictEqual(filterCommands(cmds, '', { hasChat: false, hasQuestion: false }).map(c => c.name), ['help', 'history', 'clear']);
assert.deepStrictEqual(filterCommands(cmds, 'zzz', { hasChat: true, hasQuestion: true }), []);

const explainText = extract('explainText');
const base = { entityLabel: 'Opportunity', filters: [], sort: [], top: null, aggregate: false, groupBy: [] };
/** Builds a test explanation filter with overridable fields. */
const flt = (o) => Object.assign({ id: 'f1', field: 'x', fieldLabel: 'Field', op: 'eq', values: [], from: null, to: null, via: null, group: null }, o);
/** Builds a test filter value with optional label. */
const val = (value, label) => ({ value: value === undefined ? null : value, label: label === undefined ? null : label });
const DOT = ' \u00b7 ';
const AMONG = 'Among the records you can see';

let r = explainText(Object.assign({}, base, {
  filters: [flt({ fieldLabel: 'Status', values: [val('0', 'Open')] }), flt({ id: 'f2', fieldLabel: 'City', values: [val('Istanbul')] })],
  sort: [{ field: 'estimatedvalue', label: 'Est. Revenue', descending: true }], top: 10 }));
assert.strictEqual(r.summary, 'Opportunity' + DOT + 'Status = Open' + DOT + 'City = Istanbul' + DOT + 'by Est. Revenue descending' + DOT + 'first 10');
assert.deepStrictEqual(r.lines, ['Status = Open', 'City = Istanbul', 'by Est. Revenue descending', 'first 10', AMONG]);

r = explainText(Object.assign({}, base), null);
assert.strictEqual(r.summary, 'Opportunity');
assert.deepStrictEqual(r.lines, [AMONG]);

r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'last-x-days', values: [val('30')], from: '2026-09-22', to: '2026-10-22' })] }), null, (d) => d);
assert.strictEqual(r.lines[0], 'Created On: Last 30 days (2026-09-22 \u2013 2026-10-22) \u2248');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'today', from: '2026-10-22', to: '2026-10-22' })] }), null, (d) => d);
assert.strictEqual(r.lines[0], 'Created On: Today (2026-10-22) \u2248');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'olderthan-x-months', values: [val('6')], to: '2026-04-22' })] }), null, (d) => d);
assert.strictEqual(r.lines[0], 'Created On: Older than 6 months (\u2264 2026-04-22) \u2248');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'on', values: [val('2026-03-05')], from: '2026-03-05', to: '2026-03-05' })] }), null, (d) => d);
assert.strictEqual(r.lines[0], 'Created On is on 2026-03-05');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'last-x-days', values: [val('30')] })] }));
assert.strictEqual(r.lines[0], 'Created On Last 30 days');

r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Segment', via: 'Account', values: [val('Small business')] })] }));
assert.strictEqual(r.lines[0], 'Account: Segment = Small business');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Phone', op: 'null' }), flt({ id: 'f2', fieldLabel: 'Email', op: 'not-null' })] }));
assert.deepStrictEqual(r.lines.slice(0, 2), ['Phone is empty', 'Email is not empty']);
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Customer', op: 'in', values: [val(), val(), val()] }), flt({ id: 'f2', fieldLabel: 'Owner', values: [val()] })] }));
assert.deepStrictEqual(r.lines.slice(0, 2), ['Customer is one of 3 records', 'Owner = a specific record']);
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Customer', values: [val(null, 'Acme Ltd')] })] }));
assert.strictEqual(r.lines[0], 'Customer = Acme Ltd');

r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'City', values: [val('A')], group: 'o1' }), flt({ id: 'f2', fieldLabel: 'City', values: [val('B')], group: 'o1' }), flt({ id: 'f3', fieldLabel: 'Name', values: [val('x')] })] }));
assert.deepStrictEqual(r.lines.slice(0, 2), ['(City = A or City = B)', 'Name = x']);
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Name', op: 'weird-op', values: [val('x')] })] }));
assert.strictEqual(r.lines[0], 'Name weird-op x');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Name', op: 'contains', values: [val('ist')] })] }));
assert.strictEqual(r.lines[0], 'Name contains ist');

r = explainText(Object.assign({}, base, {
  filters: [flt({ id: 'f1', fieldLabel: 'City', values: [val('A')], group: 'o1' }), flt({ id: 'f2', fieldLabel: 'City', values: [val('B')], group: 'o1' }), flt({ id: 'f3', fieldLabel: 'Name', values: [val('x')] })],
  sort: [{ field: 'name', label: 'Topic', descending: false }], top: 5 }));
assert.deepStrictEqual(r.items, [{ text: '(City = A or City = B)', ids: ['f1', 'f2'] }, { text: 'Name = x', ids: ['f3'] }]);
assert.strictEqual(r.lines.length, 5);
assert.deepStrictEqual(explainText(Object.assign({}, base)).items, []);

r = explainText(Object.assign({}, base, { aggregate: true, groupBy: ['Status'], measures: [{ fn: 'count', fieldLabel: null }] }));
assert.strictEqual(r.lines[0], 'Count grouped by Status');
r = explainText(Object.assign({}, base, { aggregate: true, groupBy: ['Status'], measures: [{ fn: 'sum', fieldLabel: 'Est. Revenue' }, { fn: 'count', fieldLabel: null }],
  sort: [{ field: 'total', label: 'Est. Revenue', fn: 'sum', descending: true }] }));
assert.deepStrictEqual(r.lines.slice(0, 2), ['Sum of Est. Revenue, Count grouped by Status', 'by Sum of Est. Revenue descending']);
r = explainText(Object.assign({}, base, { aggregate: true }));
assert.strictEqual(r.lines[0], 'Aggregate');

r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Created On', op: 'last-x-days', values: [] })] }));
assert.strictEqual(r.lines[0], 'Created On last-x-days');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Tags', op: 'contain-values', values: [val('1', 'Red'), val('2', 'Blue')] }), flt({ id: 'f2', fieldLabel: 'Owner', op: 'eq-userid' })] }));
assert.deepStrictEqual(r.lines.slice(0, 2), ['Tags includes Red, Blue', 'Owner is me']);
r = explainText(Object.assign({}, base, { filters: Array.from({ length: 12 }, (_, i) => flt({ id: 'f' + i, fieldLabel: 'Some long field name ' + i, values: [val('value ' + i)] })) }));
assert.ok(r.summary.length <= 160 && r.summary.endsWith('\u2026'), 'ozet kisaltilmali: ' + r.summary.length);
r = explainText(Object.assign({}, base), { among: 'Gorebildiginiz kayitlar arasinda' });
assert.strictEqual(r.lines[r.lines.length - 1], 'Gorebildiginiz kayitlar arasinda');

const resultSource = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'CrmNlQuery.Web', 'wwwroot', 'result.js'), 'utf8');
/** Extracts the shared explainText block from a source file. */
const blockOf = (s) => (s.match(/BEGIN explainText([\s\S]*?)\/\/ END explainText/) || [])[1];
assert.ok(blockOf(resultSource), 'result.js explainText blogunu icermeli');
assert.strictEqual(blockOf(resultSource).replace(/\r\n/g, '\n'), blockOf(source).replace(/\r\n/g, '\n'), 'widget.js ve result.js explainText bloklari ayni olmali');

/** Extracts the shared chartKit block from a source file. */
const chartBlockOf = (s) => (s.match(/BEGIN chartKit([\s\S]*?)\/\/ END chartKit/) || [])[1];
assert.ok(chartBlockOf(resultSource), 'result.js chartKit blogunu icermeli');
assert.strictEqual(chartBlockOf(resultSource).replace(/\r\n/g, '\n'), chartBlockOf(source).replace(/\r\n/g, '\n'), 'widget.js ve result.js chartKit bloklari ayni olmali');

const dayGroup = extract('dayGroup');
const now = new Date(2026, 9, 18, 15, 0, 0).getTime();
/** Creates a timestamp on a fixed test date and hour. */
const at = (d, h) => new Date(2026, 9, d, h, 0, 0).getTime();
assert.strictEqual(dayGroup(at(18, 0), now), 'groupToday');
assert.strictEqual(dayGroup(at(18, 14), now), 'groupToday');
assert.strictEqual(dayGroup(at(17, 23), now), 'groupYesterday');
assert.strictEqual(dayGroup(at(17, 0), now), 'groupYesterday');
assert.strictEqual(dayGroup(at(16, 23), now), 'groupWeek');
assert.strictEqual(dayGroup(at(11, 0), now), 'groupWeek');
assert.strictEqual(dayGroup(at(10, 23), now), 'groupOlder');
assert.strictEqual(dayGroup('2026-01-01T00:00:00Z', now), 'groupOlder');

const chartKit = extract('chartKit');
assert.strictEqual(chartKit.wants('Aylik satis grafigi goster'), true);
assert.strictEqual(chartKit.wants('Fırsatların dağılımı'), true);
assert.strictEqual(chartKit.wants('Show the trend of cases'), true);
assert.strictEqual(chartKit.wants('How many accounts do we have?'), false);
assert.strictEqual(chartKit.wants(''), false);

const ticks = chartKit.niceTicks(0, 9);
assert.ok(ticks.min <= 0 && ticks.max >= 9 && ticks.ticks.length >= 3, 'eksen 0..9 araligini kapsamali');
assert.deepStrictEqual(chartKit.niceTicks(0, 0).ticks.length >= 2, true);

const texts = { other: 'Other', empty: '(empty)', line: 'Line chart', bar: 'Bar chart' };
const lineChart = { kind: 'line', xKind: 'yearmonth', measure: 'total', fn: 'sum', labels: ['2025-11', '2025-12', '2026-01'], series: [{ name: 'total', values: [10, 20, 0] }], hasOther: false, omitted: 0, partial: true };
const lm = chartKit.model(lineChart, 'en-US', texts);
assert.strictEqual(lm.labels.length, 3);
assert.ok(/Nov/.test(lm.labels[0]), 'ay adi tarayici diliyle: ' + lm.labels[0]);
assert.strictEqual(lm.partial, true);
assert.ok(/Line chart/.test(lm.summary) && /20/.test(lm.summary), 'ozet turu ve en yuksek degeri soylemeli: ' + lm.summary);

const barChart = { kind: 'bar', xKind: 'category', measure: 'n', fn: 'count', labels: ['High', 'Low', 'Other'], series: [{ name: 'n', values: [9, 2, 1234] }], hasOther: true, omitted: 0, partial: false };
const bm = chartKit.model(barChart, 'de-DE', texts);
assert.strictEqual(bm.labels[2], 'Other');
assert.strictEqual(bm.format(1234), '1.234');
const em = chartKit.model({ ...barChart, hasOther: false, labels: ['(empty)', 'Low', 'x'] }, 'en-US', { ...texts, empty: 'Leer' });
assert.strictEqual(em.labels[0], 'Leer');

/** Creates a fake canvas context for drawing tests. */
function fakeContext() {
  const calls = [];
  return new Proxy({ calls }, {
    get(target, name) {
      if (name === 'calls') { return calls; }
      if (name === 'measureText') { return text => ({ width: String(text).length * 6 }); }
      return (...args) => { calls.push(name); };
    },
    set() { return true; }
  });
}
for (const model of [lm, bm, chartKit.model({ ...barChart, kind: 'hbar' }, 'en-US', texts),
  chartKit.model({ ...lineChart, series: [{ name: '2025', values: [1, 2, 3] }, { name: '2026', values: [3, null, 5] }] }, 'en-US', texts)]) {
  const ctx = fakeContext();
  chartKit.draw(ctx, model, 480, model.height);
  assert.ok(ctx.calls.length > 10, 'grafik bir seyler cizmeli (' + model.kind + ')');
}

r = explainText(Object.assign({}, base, {
  entityLabel: 'Fırsat',
  filters: [flt({ fieldLabel: 'Durum', values: [val('0', 'Açık')] }), flt({ id: 'f2', fieldLabel: 'Şehir', op: 'contains', values: [val('Londra')] })],
  sort: [{ field: 'estimatedvalue', label: 'Tahmini Gelir', descending: true }], top: 10 }), null, null, 'tr');
assert.deepStrictEqual(r.lines, ['Durum = Açık', 'Şehir şunu içerir: Londra', 'Tahmini Gelir alanına göre azalan', 'ilk 10', 'Görebildiğiniz kayıtlar arasında']);
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Oluşturma Tarihi', op: 'last-x-days', values: [val('30')], from: '2026-09-22', to: '2026-10-22' })] }), null, (d) => d, 'tr');
assert.strictEqual(r.lines[0], 'Oluşturma Tarihi: Son 30 gün (2026-09-22 – 2026-10-22) ≈');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Telefon', op: 'null' })] }), null, null, 'tr');
assert.strictEqual(r.lines[0], 'Telefon boş');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'Sahip', op: 'eq-userid' })] }), null, null, 'tr');
assert.strictEqual(r.lines[0], 'Sahip ben');
r = explainText(Object.assign({}, base, { aggregate: true, measures: [{ fn: 'count', fieldLabel: '' }, { fn: 'sum', fieldLabel: 'Tutar' }], groupBy: ['Durum'] }), null, null, 'tr');
assert.strictEqual(r.lines[0], 'Sayı, Tutar toplamı Durum alanına göre gruplanmış');
r = explainText(Object.assign({}, base, { filters: [flt({ fieldLabel: 'City', values: [val('Istanbul')] })] }), null, null, 'de');
assert.strictEqual(r.lines[0], 'City = Istanbul');

const exSrc = source.slice(source.indexOf('BEGIN explainText'), source.indexOf('END explainText'));
/** Collects the keys of a section in a catalog for the test. */
const sectionKeys = (name, section) => {
  const start = exSrc.indexOf('var ' + name + ' = {');
  const body = exSrc.slice(start, exSrc.indexOf('\n  };', start));
  const from = body.indexOf(section + ': {');
  let depth = 0, to = from;
  for (let i = body.indexOf('{', from); i < body.length; i++) {
    if (body[i] === '{') { depth++; } else if (body[i] === '}') { depth--; if (depth === 0) { to = i; break; } }
  }
  return [...body.slice(from, to).matchAll(/'([a-z-]+)':/g)].map(x => x[1]).sort();
};
const keysOf = sectionKeys;
['ops', 'dates', 'measures'].forEach(function (section) {
  assert.deepStrictEqual(keysOf('X_TR', section), keysOf('X_EN', section), 'explainText TR/EN anahtar farki: ' + section);
});

const tableBlock = source.match(/var T_EN = \{([\s\S]*?)\r?\n  \};/)[1];
const keys = [...tableBlock.matchAll(/^\s{4}(\w+):/gm)].map(x => x[1]);
const trBlock = source.match(/var T_TR = \{([\s\S]*?)\r?\n  \};/)[1];
const trKeys = [...trBlock.matchAll(/^\s{4}(\w+):/gm)].map(x => x[1]);
assert.ok(keys.length > 60, 'metin tablosu bos gorunuyor: ' + keys.length);
const used = new Set([...source.matchAll(/\bt\('(\w+)'/g)].map(x => x[1]));
['groupToday', 'groupYesterday', 'groupWeek', 'groupOlder', 'groupPinned'].forEach(k => used.add(k));
used.add('examples');
['cmdHelp', 'cmdNew', 'cmdEdit', 'cmdHistory', 'cmdClear'].forEach(k => used.add(k));
['sumBasic', 'sumOpportunities', 'sumActivities', 'sumCases', 'sumContracts', 'sumOrders'].forEach(k => used.add(k));
['sumMOppCount', 'sumMOppTotal', 'sumMLastContact', 'sumMCaseCount', 'sumMCaseOldest', 'sumMNoPriority', 'sumMContractCount', 'sumMContractExpiring', 'sumMOrderCount'].forEach(k => used.add(k));
['fbReasonWrongRecords', 'fbReasonWrongColumns', 'fbReasonMisunderstood', 'fbReasonTooSlow', 'fbReasonOther'].forEach(k => used.add(k));
['createStatus_ready', 'createStatus_needs_info', 'createStatus_created', 'createStatus_failed', 'createStatus_skipped', 'createErr_permission', 'createErr_crm'].forEach(k => used.add(k));
['missing_required', 'unknown_field', 'invalid_number', 'invalid_date', 'invalid_boolean', 'too_long', 'choice_not_found', 'lookup_not_found', 'lookup_ambiguous'].forEach(c => used.add('createProb_' + c));
assert.ok(!/share\.checked = true/.test(source), 'paylasim kutusu varsayilan isaretli OLMAMALI');
const missing = [...used].filter(k => !keys.includes(k));
assert.deepStrictEqual(missing, [], 'T tablosunda olmayan anahtarlar: ' + missing.join(', '));
const unused = keys.filter(k => !used.has(k));
assert.deepStrictEqual(unused, [], 'kullanilmayan metin anahtarlari (silin ya da kullanin): ' + unused.join(', '));

const TR_COMPLETE = true;
/** Removes the quotes and escapes from a string literal. */
const unquote = (literal) => literal.slice(1, -1).replace(/\'/g, "'").replace(/\\/g, '\\');
const enValues = Object.fromEntries([...tableBlock.matchAll(/^\s{4}(\w+):\s*('(?:[^'\\]|\\.)*')/gm)].map(x => [x[1], unquote(x[2])]));
const trValues = Object.fromEntries([...trBlock.matchAll(/^\s{4}(\w+):\s*('(?:[^'\\]|\\.)*')/gm)].map(x => [x[1], unquote(x[2])]));
const unknownTr = trKeys.filter(k => !keys.includes(k));
assert.deepStrictEqual(unknownTr, [], "T_TR'de olup T_EN'de olmayan anahtarlar: " + unknownTr.join(', '));
/** Lists the placeholder names used in a text. */
const placeholderNames = (text) => [...String(text).matchAll(/\{(\w+)\}/g)].map(m => m[1]).sort().join(',');
trKeys.forEach(function (k) {
  if (typeof trValues[k] === 'string' && typeof enValues[k] === 'string') {
    assert.ok(trValues[k].trim().length > 0, 'TR metni bos: ' + k);
    assert.strictEqual(placeholderNames(trValues[k]), placeholderNames(enValues[k]), 'parametre uyusmazligi (en/tr): ' + k);
  }
});
if (TR_COMPLETE) {
  const missingTr = keys.filter(k => !trKeys.includes(k));
  assert.deepStrictEqual(missingTr, [], 'TR cevirisi eksik anahtarlar: ' + missingTr.join(', '));
}

const pickLanguage = extract('pickLanguage');
assert.strictEqual(pickLanguage('tr', 'en-US', ['en', 'tr']), 'tr');
assert.strictEqual(pickLanguage(undefined, 'tr-TR', ['en', 'tr']), 'tr');
assert.strictEqual(pickLanguage('de', 'fr', ['en', 'tr']), 'en');
assert.strictEqual(pickLanguage('', '', ['en', 'tr']), 'en');
assert.strictEqual(pickLanguage('TR_tr', '', ['en', 'tr']), 'tr');

console.log('widget saf fonksiyon testleri gecti (' + keys.length + ' metin anahtari, ' + trKeys.length + ' Turkce)');

const vm = require('vm');
const wwwroot = path.join(__dirname, '..', '..', 'src', 'CrmNlQuery.Web', 'wwwroot');
/** Lists the script files in a directory. */
function scripts(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).filter(e => e.name !== 'admin-next').flatMap(e => e.isDirectory() ? scripts(path.join(dir, e.name)) : (e.name.endsWith('.js') ? [path.join(dir, e.name)] : []));
}
for (const file of scripts(wwwroot)) {
  try { new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }); }
  catch (e) { assert.fail('soz dizimi hatasi: ' + path.relative(wwwroot, file) + ': ' + e.message); }
}
console.log('betik soz dizimi denetimi gecti');

const presentation = extract('presentation');
assert.deepStrictEqual(presentation('Grafik olarak ver'), { view: 'chart', style: null });
assert.deepStrictEqual(presentation('Çizgi grafik olarak ver.'), { view: 'chart', style: 'line' });
assert.deepStrictEqual(presentation('çubuk grafik yapar mısın?'), { view: 'chart', style: 'bar' });
assert.deepStrictEqual(presentation('Bunu grafik olarak göster'), { view: 'chart', style: null });
assert.deepStrictEqual(presentation('Tabloyu göster'), { view: 'table', style: null });
assert.deepStrictEqual(presentation('show it as a bar chart'), { view: 'chart', style: 'bar' });
assert.deepStrictEqual(presentation('Show as table please'), { view: 'table', style: null });
assert.strictEqual(presentation('Boğaz Denizcilik firmasına ait talepleri grafik olarak ver'), null);
assert.strictEqual(presentation('Şehirlere göre hesap sayısını grafik olarak göster'), null);
assert.deepStrictEqual(presentation('Grafik'), { view: 'chart', style: null });
assert.strictEqual(presentation('İstanbul'), null);
assert.strictEqual(presentation(''), null);
assert.strictEqual(presentation('ver'), null);
console.log('presentation testi gecti');

const columnLabel = extract('columnLabel');
assert.strictEqual(columnLabel({ columns: ['telephone1', 'n'], labels: ['Business Phone', 'Count'] }, 0), 'Business Phone');
assert.strictEqual(columnLabel({ columns: ['telephone1'], labels: null }, 0), 'telephone1');
assert.strictEqual(columnLabel({ columns: ['a', 'b'], labels: ['A'] }, 1), 'b');
assert.strictEqual(columnLabel({ columns: ['a'], labels: [''] }, 0), 'a');
console.log('columnLabel testi gecti');

const tableActions = extract('tableActions');
assert.deepStrictEqual(tableActions({ openInCrm: true }, false), { crmButton: true, resultLink: false });
assert.deepStrictEqual(tableActions({ openInCrm: true }, true), { crmButton: true, resultLink: true });
assert.deepStrictEqual(tableActions({ openInCrm: false }, false), { crmButton: false, resultLink: true });
assert.deepStrictEqual(tableActions({}, false), { crmButton: false, resultLink: true });
assert.deepStrictEqual(tableActions(null, false), { crmButton: false, resultLink: true });
console.log('tableActions testi gecti');

const suggestEligible = extract('suggestEligible');
const plain = { resultId: 'r1', table: { rows: [['a'], ['b']] }, truncated: false, stepSummaries: [] };
const user = { canSuggest: true, canSave: false };
assert.strictEqual(suggestEligible(plain, user), true);
assert.strictEqual(suggestEligible(plain, { canSuggest: true, canSave: true }), false);
assert.strictEqual(suggestEligible(plain, { canSuggest: false, canSave: false }), false);
assert.strictEqual(suggestEligible({ ...plain, table: { rows: [] } }, user), false);
assert.strictEqual(suggestEligible({ ...plain, truncated: true }, user), false);
assert.strictEqual(suggestEligible({ ...plain, stepSummaries: [{}] }, user), false);
assert.strictEqual(suggestEligible({ ...plain, resultId: null }, user), false);
assert.strictEqual(suggestEligible(null, user), false);
assert.strictEqual(suggestEligible(plain, null), false);
console.log('suggestEligible testi gecti');

const crmApiBase = extract('crmApiBase');
assert.strictEqual(crmApiBase('http://crm.example.com/contoso/main.aspx?etn=account', 'v9.0'), 'http://crm.example.com/contoso/api/data/v9.0/');
assert.strictEqual(crmApiBase('https://org.crm.contoso.com/main.aspx?appid=1', 'v9.0'), 'https://org.crm.contoso.com/api/data/v9.0/');
assert.strictEqual(crmApiBase('http://crm:5555/org/userdefined/edit.aspx', 'v8.2'), 'http://crm:5555/org/api/data/v8.2/');

const randomSecret = extract('randomSecret');
const secretA = randomSecret((bytes) => require('crypto').randomFillSync(bytes));
const secretB = randomSecret((bytes) => require('crypto').randomFillSync(bytes));
assert.ok(/^[A-Za-z0-9_-]{64}$/.test(secretA), 'gizli deger 64 URL-guvenli karakter olmali: ' + secretA);
assert.notStrictEqual(secretA, secretB);

const tokenFresh = extract('tokenFresh');
assert.strictEqual(tokenFresh({ token: 't', expiresAt: 1000000 + 120000 }, 1000000), true);
assert.strictEqual(tokenFresh({ token: 't', expiresAt: 1000000 + 30000 }, 1000000), false, 'bir dakikadan az kalan oturum yenilenir');
assert.strictEqual(tokenFresh({ token: '', expiresAt: 9999999999 }, 1), false);
assert.strictEqual(tokenFresh(null, 1), false);
console.log('crmApiBase, randomSecret, tokenFresh testi gecti');
