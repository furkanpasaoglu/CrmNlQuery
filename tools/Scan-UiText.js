const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const args = process.argv.slice(2);
const baselinePath = path.join(__dirname, 'ui-text-baseline.json');

/** Extracts string literals from JavaScript source for the user-text scan. */
function jsLiterals(source) {
  const out = [];
  let i = 0, line = 1;
  const n = source.length;
  while (i < n) {
    const c = source[i];
    if (c === '\n') { line++; i++; continue; }
    if (c === '/' && source[i + 1] === '/') { while (i < n && source[i] !== '\n') { i++; } continue; }
    if (c === '/' && source[i + 1] === '*') { i += 2; while (i < n && !(source[i] === '*' && source[i + 1] === '/')) { if (source[i] === '\n') { line++; } i++; } i += 2; continue; }
    if (c === '\'' || c === '"' || c === '`') {
      const quote = c, startLine = line;
      let j = i + 1, text = '';
      while (j < n && source[j] !== quote) {
        if (source[j] === '\\') { text += source[j + 1] === 'n' ? '\n' : source[j + 1]; j += 2; continue; }
        if (source[j] === '\n') { line++; if (quote !== '`') { break; } }
        text += source[j];
        j++;
      }
      out.push({ text: text, line: startLine, quote: quote });
      i = j + 1;
      continue;
    }
    if (c === '/') {
      let k = i - 1;
      while (k >= 0 && /\s/.test(source[k])) { k--; }
      if (k < 0 || /[(,=:\[!&|?{};+]/.test(source[k])) {
        let j = i + 1, inClass = false;
        while (j < n && source[j] !== '\n') {
          if (source[j] === '\\') { j += 2; continue; }
          if (source[j] === '[') { inClass = true; } else if (source[j] === ']') { inClass = false; } else if (source[j] === '/' && !inClass) { break; }
          j++;
        }
        i = j + 1;
        continue;
      }
    }
    i++;
  }
  return out;
}

/** Decides whether a literal looks like user-visible text rather than code or an identifier. */
function looksLikeUserText(text) {
  const t = text.trim();
  if (t.length < 3) { return false; }
  if (!/[A-Za-zÇĞİÖŞÜçğıöşü]{3}/.test(t)) { return false; }
  if (/^(\/|https?:|#|\.|data:|<|@)/.test(t)) { return false; }
  if (/[{};=]/.test(t) && !/\s{1}[A-Za-z]{3,}\s/.test(t)) { return false; }
  if (/[a-z-]+\s*:\s*[^;]+;/.test(t)) { return false; }
  if (/^[a-z]+\/[a-z0-9.+-]+$/.test(t)) { return false; }
  if (/^[A-Z][A-Za-z]+(-[A-Za-z]+)+$/.test(t)) { return false; }
  if (/^[A-Z0-9_]+$/.test(t)) { return false; }
  if (/^[a-z][A-Za-z0-9_.:\-\[\]]*$/.test(t)) { return false; }
  if (/^[A-Za-z0-9_.-]+$/.test(t) && !/^[A-Z][a-z]{2,}/.test(t)) { return false; }
  if (/\s>\s|\bfunction\b|=>/.test(t)) { return false; }
  if (/^(use strict|noopener noreferrer|(btn|warn|xbtn|stepline|cmdrow|answerfoot|muted|chip|fb|sum|draft|lvl)[a-z]*( [a-z]+)*)$/.test(t) && !/[A-Z]/.test(t)) { return false; }
  if (/^(Enter|Escape|Tab|ArrowUp|ArrowDown|ArrowLeft|ArrowRight|Backspace|Shift|Control|Alt|Meta)$/.test(t)) { return false; }
  if (/^\(|^\d+px /.test(t)) { return false; }
  const tokens = t.split(' ');
  if (tokens.length >= 2 && tokens.every((x) => /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/.test(x)) && tokens.some((x) => x.includes('-') || /^(error|warn|muted|danger|primary|open|closed|active|sel|on|off)$/.test(x))) { return false; }
  if (/^api\//.test(t)) { return false; }
  if (/^[A-Z][A-Za-z]+:[A-Za-z:]+$/.test(t)) { return false; }
  return true;
}

/** Scans a JavaScript file for hardcoded user-visible text. */
function scanJs(file) {
  const source = fs.readFileSync(file, 'utf8');
  const lines = source.split(/\r?\n/);
  const skipRanges = [];
  lines.forEach((l, index) => {
    const open = /^(\s*)(?:var|const|let) (T_EN|T_TR|X_EN|X_TR) = \{/.exec(l);
    if (open) {
      const closing = new RegExp('^' + open[1] + '\\};');
      for (let k = index + 1; k < lines.length; k++) { if (closing.test(lines[k])) { skipRanges.push([index + 1, k + 1]); break; } }
    }
  });
  const found = [];
  for (const lit of jsLiterals(source)) {
    if (skipRanges.some(([from, to]) => lit.line >= from && lit.line <= to)) { continue; }
    const lineText = lines[lit.line - 1] || '';
    if (/console\.|throw new Error\(/.test(lineText)) { continue; }
    if (/\/\/\s*i18n-ok/.test(lineText)) { continue; }
    if (looksLikeUserText(lit.text)) { found.push({ line: lit.line, text: lit.text.replace(/\s+/g, ' ').slice(0, 90) }); }
  }
  return found;
}

const CS_PATTERNS = [
  { name: 'FailureOutcome("...")', re: /new FailureOutcome\(\s*\$?"/g },
  { name: 'AccessResult.Fail(..., "...")', re: /AccessResult\.Fail\([^,]+,\s*\$?"/g },
  { name: 'QuestionOutcome / Choice / note hardcoded', re: /(new QuestionOutcome\([^;]*?\$?"[A-Z]|new Choice\(\$?"[A-Z]|[nN]otes\.Add\(\$?")/g },
  { name: 'UserMessages hardcoded constant', re: /public const string \w+/g, only: /UserMessages\.cs$/ },
  { name: 'SettingDefinition description', re: /new SettingDefinition\(/g, only: /SettingsCatalog\.cs$/ },
  { name: 'Suggestion label hardcoded', re: /new Suggestion\(\s*[^,]+,\s*[^,]+,\s*\$?"/g },
  { name: 'Validator error ("could not"/"must")', re: /errors\.Add\(\$?"/g, only: /PlanValidator\.cs$/ },
];

/** Walks a directory recursively collecting files that match a filter. */
function walk(dir, filter, acc) {
  for (const name of fs.readdirSync(dir)) {
    if (name === 'obj' || name === 'bin' || name === 'node_modules') { continue; }
    const full = path.join(dir, name);
    const stat = fs.statSync(full);
    if (stat.isDirectory()) { walk(full, filter, acc); } else if (filter(full)) { acc.push(full); }
  }
  return acc;
}

/** Scans the C# sources for hardcoded user-visible text. */
function scanCs() {
  const files = walk(path.join(root, 'src'), (f) => f.endsWith('.cs'), []);
  const counts = {};
  for (const p of CS_PATTERNS) { counts[p.name] = 0; }
  for (const f of files) {
    const text = fs.readFileSync(f, 'utf8');
    for (const p of CS_PATTERNS) {
      if (p.only && !p.only.test(f.replace(/\\/g, '/'))) { continue; }
      const m = text.match(p.re);
      if (m) { counts[p.name] += m.length; }
    }
  }
  return counts;
}

const wwwroot = path.join(root, 'src', 'CrmNlQuery.Web', 'wwwroot');
const surfaces = [
  { name: 'widget (widget.js, outside the catalog)', files: [path.join(wwwroot, 'widget', 'widget.js')] },
  { name: 'result page (result.js)', files: [path.join(wwwroot, 'result.js')] },
  { name: 'test paneli (index.js)', files: [path.join(wwwroot, 'index.js')] },
  { name: 'admin panel (admin-ui/src, outside the catalog)', files: walk(path.join(root, 'admin-ui', 'src'), (f) => /\.tsx?$/.test(f) && !/[\\/]i18n[\\/]/.test(f), []) },
];

const result = { js: {}, jsFiles: {}, cs: scanCs() };
const listing = [];
for (const s of surfaces) {
  let total = 0;
  for (const f of s.files) {
    const hits = scanJs(f);
    total += hits.length;
    const rel = path.relative(root, f).replace(/\\/g, '/');
    result.jsFiles[rel] = hits.length;
    for (const h of hits) { listing.push(rel + ':' + h.line + ': ' + h.text); }
  }
  result.js[s.name] = total;
}

if (args.includes('--write-baseline')) {
  fs.writeFileSync(baselinePath, JSON.stringify({ js: result.js, cs: result.cs }, null, 2) + '\n');
  console.log('Baseline written: ' + baselinePath);
  process.exit(0);
}

if (args.includes('--check')) {
  if (!fs.existsSync(baselinePath)) { console.error('No baseline: run --write-baseline first'); process.exit(2); }
  const base = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
  const problems = [];
  for (const key of Object.keys(result.js)) {
    if (result.js[key] > (base.js[key] ?? 0)) { problems.push(`${key}: ${result.js[key]} > baseline ${base.js[key] ?? 0} (new hardcoded text: use the catalog)`); }
  }
  for (const key of Object.keys(result.cs)) {
    if (result.cs[key] > (base.cs[key] ?? 0)) { problems.push(`${key}: ${result.cs[key]} > baseline ${base.cs[key] ?? 0} (new hardcoded message: use the Messages catalog)`); }
  }
  if (problems.length > 0) { console.error('HARDCODED TEXT INCREASED:\n  ' + problems.join('\n  ')); process.exit(1); }
  console.log('Hardcoded text count is within the baseline.');
  process.exit(0);
}

if (args.includes('--list')) { console.log(listing.join('\n')); }
console.log('\nJS surfaces (candidate hardcoded user text):');
let jsTotal = 0;
for (const [k, v] of Object.entries(result.js)) { console.log(`  ${String(v).padStart(4)}  ${k}`); jsTotal += v; }
console.log('  ----');
console.log(`  ${String(jsTotal).padStart(4)}  JS toplam`);
console.log('\nPer file:');
for (const [k, v] of Object.entries(result.jsFiles)) { if (v > 0) { console.log(`  ${String(v).padStart(4)}  ${k}`); } }
console.log('\nC# patterns:');
let csTotal = 0;
for (const [k, v] of Object.entries(result.cs)) { console.log(`  ${String(v).padStart(4)}  ${k}`); csTotal += v; }
console.log('  ----');
console.log(`  ${String(csTotal).padStart(4)}  C# toplam`);
