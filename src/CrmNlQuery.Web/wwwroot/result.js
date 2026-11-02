const out = document.getElementById("out");
const meta = document.getElementById("meta");

/** Creates a DOM element with optional text and class, setting text safely. */
function el(tag, text, cls) {
  const e = document.createElement(tag);
  if (text !== undefined) e.textContent = text;
  if (cls) e.className = cls;
  return e;
}

const T_EN = {
  pageTitle: "Query result",
  pageTitleWith: "Query result — {entity}",
  refinePlaceholder: "Narrow it down or ask about related records, e.g. only the ones in Germany, or their contracts",
  refineGo: "Continue from this result",
  invalidLink: "This link is not valid.",
  loading: "Loading…",
  working: "Working…",
  unreachable: "Could not reach the service ({msg}).",
  summaryNote: "This is a customer summary. Open it in the chat window to see it.",
  draftNote: "This is an email draft. Open it in the chat window to see it.",
  notAvailable: "This result is not available.",
  narrowFailed: "Could not narrow the result.",
  columnsHint: "or type column names, e.g. id, name",
  rephraseHint: "or rephrase",
  send: "Send",
  countAll: "All {total} {entity}records are shown.",
  countOf: "Showing the first {shown} of {total} {entity}records — narrow your question.",
  countMore: "Showing the first {shown} of more than {total} {entity}records — narrow your question.",
  narrowBy: "Narrow by:",
  narrowSentence: "Of these, only those with {label} {value}",
  fresh: "{entity} · fresh data (the query was just run again)",
  fixedList: "This result is based on a fixed list of records identified when the question was asked; the rows are fresh, the list is not.",
  truncated: "Result truncated at 500 rows — narrow your question.",
  fetchXml: "Generated FetchXML",
  copy: "Copy",
  copied: "Copied",
  noRecords: "No records found.",
  searchResults: "Search in results…",
  chartOther: "Other",
  chartEmpty: "(empty)",
  chartLine: "Line chart",
  chartBar: "Bar chart",
  chartView: "Chart",
  tableView: "Table",
  chartPng: "Download PNG",
  chartPartial: "The chart shows partial data: the result was cut off.",
  chartOmitted: "{n} more categories are only in the table.",
  openRecord: "Open",
  openRecordHint: "Open this record in the CRM",
  rowsOf: "{a} of {b} rows",
  prev: "Prev",
  next: "Next",
  pageOf: "Page {a} / {b}",
  print: "Print",
  printHint: "Prints every row of the search result, not only this page"
};
const T_TR = {
  pageTitle: "Sorgu sonucu",
  pageTitleWith: "Sorgu sonucu — {entity}",
  refinePlaceholder: "Daraltın ya da ilişkili kayıtları sorun, örn. yalnızca Almanya'dakiler ya da sözleşmeleri",
  refineGo: "Bu sonuçtan devam et",
  invalidLink: "Bu bağlantı geçerli değil.",
  loading: "Yükleniyor…",
  working: "Çalışıyor…",
  unreachable: "Servise ulaşılamadı ({msg}).",
  summaryNote: "Bu bir müşteri özeti. Görmek için sohbet penceresinde açın.",
  draftNote: "Bu bir e-posta taslağı. Görmek için sohbet penceresinde açın.",
  notAvailable: "Bu sonuç kullanılamıyor.",
  narrowFailed: "Sonuç daraltılamadı.",
  columnsHint: "ya da sütun adlarını yazın, örn. id, name",
  rephraseHint: "ya da sorunuzu başka şekilde yazın",
  send: "Gönder",
  countAll: "Tüm {total} {entity}kaydı gösteriliyor.",
  countOf: "Toplam {total} {entity}kaydı var; ilk {shown} tanesi gösteriliyor — sorunuzu daraltın.",
  countMore: "{total} üzerinde {entity}kaydı var; ilk {shown} tanesi gösteriliyor — sorunuzu daraltın.",
  narrowBy: "Şuna göre daralt:",
  narrowSentence: "Bunlardan yalnızca {label} {value} olanlar",
  fresh: "{entity} · güncel veri (sorgu az önce yeniden çalıştırıldı)",
  fixedList: "Bu sonuç, soru sorulduğunda belirlenen sabit bir kayıt listesine dayanır; satırlar günceldir, liste değildir.",
  truncated: "Sonuç 500 satırda kesildi — sorunuzu daraltın.",
  fetchXml: "Üretilen FetchXML",
  copy: "Kopyala",
  copied: "Kopyalandı",
  noRecords: "Kayıt bulunamadı.",
  searchResults: "Sonuçlarda ara…",
  chartOther: "Diğer",
  chartEmpty: "(boş)",
  chartLine: "Çizgi grafik",
  chartBar: "Çubuk grafik",
  chartView: "Grafik",
  tableView: "Tablo",
  chartPng: "PNG indir",
  chartPartial: "Grafik kısmi veri gösteriyor: sonuç kesildi.",
  chartOmitted: "{n} kategori daha yalnızca tabloda.",
  openRecord: "Aç",
  openRecordHint: "Bu kaydı CRM'de aç",
  rowsOf: "{a} / {b} satır",
  prev: "Önceki",
  next: "Sonraki",
  pageOf: "Sayfa {a} / {b}",
  print: "Yazdır",
  printHint: "Yalnız bu sayfayı değil, arama sonucundaki tüm satırları yazdırır"
};
let language = "en";
/** Chooses the display language from the saved preference, falling back to the browser language. */
function pickLanguage(saved, browser) {
  const primary = v => String(v || "").toLowerCase().split(/[-_]/)[0];
  if (saved && ["en", "tr"].includes(primary(saved))) return primary(saved);
  if (browser && ["en", "tr"].includes(primary(browser))) return primary(browser);
  return "en";
}
/** Looks up a localized UI text by key and fills in the placeholder values. */
function t(key, vars) {
  let s = (language === "tr" ? T_TR[key] : undefined);
  if (s === undefined) s = T_EN[key];
  if (s === undefined) return key;
  if (vars) s = s.replace(/\{(\w+)\}/g, (m, k) => (vars[k] === undefined ? m : String(vars[k])));
  return s;
}
/** Returns the locale tag used for formatting numbers and dates in the current language. */
function locale() { return language === "tr" ? "tr-TR" : "en-US"; }
/** Writes the static page texts in the current language. */
function applyStaticText() {
  document.documentElement.lang = language;
  document.title = t("pageTitle");
  document.querySelector("h1").textContent = t("pageTitle");
  document.getElementById("refineText").placeholder = t("refinePlaceholder");
  document.getElementById("refineGo").textContent = t("refineGo");
}
/** Resolves the page language from the saved preference or the browser. */
async function resolveLanguage() {
  let saved = null;
  try {
    const res = await authFetch("api/me/preferences", { cache: "no-store" });
    if (res.ok) saved = (await res.json()).effectiveLanguage;
  } catch (e) {  }
  language = pickLanguage(saved, navigator.language);
  applyStaticText();
}

/** Folds text to lower case without diacritics, matching the server-side name folding including Turkish dotless i. */
function fold(s) {
  return s.replace(/[İIıi]/g, "i").replace(/[Şş]/g, "s").replace(/[Ğğ]/g, "g")
    .replace(/[Üü]/g, "u").replace(/[Öö]/g, "o").replace(/[Çç]/g, "c").toLowerCase();
}

/** Tells whether this tab already holds a still-valid session token. */
function hasSession() {
  try {
    const raw = sessionStorage.getItem("nlq_session");
    const session = raw ? JSON.parse(raw) : null;
    return !!(session && session.token && session.expiresAt > Date.now());
  } catch (e) { return false; }
}

/** When the chat widget opened this page, asks it for a hand-over code and accepts the answer only from the opening page and only from an allowed CRM origin. */
async function takeHandoff() {
  if (!window.opener || window.opener === window || hasSession()) return;
  let origins = [];
  try {
    const res = await fetch("api/auth/origins", { cache: "no-store" });
    origins = (await res.json()).origins || [];
  } catch (e) {  }
  if (origins.length === 0) return;
  await new Promise((resolve) => {
    const timer = setTimeout(() => { window.removeEventListener("message", onMessage); resolve(); }, 4000);
    async function onMessage(e) {
      if (e.source !== window.opener || !origins.includes(e.origin) || !e.data || e.data.type !== "nlq-code" || typeof e.data.code !== "string") return;
      clearTimeout(timer);
      window.removeEventListener("message", onMessage);
      try {
        const res = await fetch("api/auth/redeem", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ code: e.data.code }) });
        if (res.ok) {
          const data = await res.json();
          sessionStorage.setItem("nlq_session", JSON.stringify({ token: data.token, expiresAt: Date.parse(data.expiresAt) }));
        }
      } catch (err) {  }
      resolve();
    }
    window.addEventListener("message", onMessage);
    window.opener.postMessage({ type: "nlq-ready" }, "*");
  });
}

/** Fetches a service address with the session token when this tab has one. */
function authFetch(url, options) {
  const opts = Object.assign({}, options);
  try {
    const raw = sessionStorage.getItem("nlq_session");
    const session = raw ? JSON.parse(raw) : null;
    if (session && session.token && session.expiresAt > Date.now()) {
      opts.headers = Object.assign({}, opts.headers, { Authorization: "Bearer " + session.token });   // i18n-ok
    }
  } catch (e) {  }
  return fetch(url, opts);
}

/** Loads the result and renders it on the page. */
async function load() {
  await takeHandoff();
  await resolveLanguage();
  const id = new URLSearchParams(location.search).get("id") || "";
  if (!/^[0-9a-f]{32}$/.test(id)) {
    out.append(el("p", t("invalidLink"), "error"));
    return;
  }
  resultId = id;
  meta.textContent = t("loading");
  let data;
  try {
    const res = await authFetch("api/result/" + id, { cache: "no-store" });
    data = await res.json();
  } catch (e) {
    meta.textContent = "";
    out.append(el("p", t("unreachable", { msg: e.message }), "error"));
    return;
  }
  meta.textContent = "";
  if (data.kind === "summary") {
    out.append(el("p", t("summaryNote"), "muted"));
    return;
  }
  if (data.kind === "draft") {
    out.append(el("p", t("draftNote"), "muted"));
    return;
  }
  if (data.kind !== "answer") {
    out.append(el("p", data.message || t("notAvailable"), "error"));
    return;
  }
  render(data);
  document.getElementById("refine").hidden = false;
}

let resultId = "";
let refineText = "";
let refineAnswers = {};

/** Sends the follow-up text to continue from the current result. */
async function refine() {
  const status = document.getElementById("refineStatus");
  const box = document.getElementById("refineQuestion");
  box.replaceChildren();
  status.textContent = t("working");
  let data;
  try {
    const res = await authFetch("api/result/" + resultId + "/refine", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text: refineText, answers: refineAnswers })
    });
    data = await res.json();
  } catch (e) {
    status.textContent = "";
    box.append(el("p", t("unreachable", { msg: e.message }), "error"));
    return;
  }
  status.textContent = "";
  if (data.kind === "answer" && data.resultId) {
    location.href = "result.html?id=" + encodeURIComponent(data.resultId);
  } else if (data.kind === "question") {
    askQuestion(data, box);
  } else {
    box.append(el("p", data.message || t("narrowFailed"), "error"));
  }
}

/** Builds the clarifying-question box with answer choices for the user. */
function askQuestion(d, box) {
  box.append(el("p", d.text, "question"));
  const row = el("div", undefined, "bar");
  for (const c of d.choices) {
    const b = el("button", c.label);
    b.onclick = () => { refineAnswers[d.key] = c.value; refine(); };
    row.append(b);
  }
  if (d.allowFreeText) {
    const input = el("input");
    input.placeholder = d.questionKind === "columns" ? t("columnsHint") : t("rephraseHint");
    const go = el("button", t("send"));
    go.onclick = () => {
      const v = input.value.trim();
      if (!v) return;
      if (d.questionKind === "columns") { refineAnswers[d.key] = "custom:" + v; }
      else { refineText = v; refineAnswers = {}; }
      refine();
    };
    row.append(input, go);
  }
  box.append(row);
}

document.getElementById("refineGo").onclick = () => {
  refineText = document.getElementById("refineText").value;
  refineAnswers = {};
  refine();
};
document.getElementById("refineText").addEventListener("keydown", e => {
  if (e.key === "Enter") document.getElementById("refineGo").click();
});

/** Fetches the total record count from the server and updates the shown-rows warning, never using a hard-coded limit. */
async function loadCount(warn, shown) {
  try {
    const res = await authFetch("api/result/" + resultId + "/count", { cache: "no-store" });
    if (!res.ok) return;
    const c = await res.json();
    if (typeof c.total !== "number") return;
    const entity = c.entityLabel ? c.entityLabel + " " : "";
    if (!c.limitExceeded && c.total <= shown) {
      warn.className = "muted";
      warn.textContent = t("countAll", { total: c.total.toLocaleString(locale()), entity: entity });
      return;
    }
    warn.textContent = t(c.limitExceeded ? "countMore" : "countOf", { shown: shown.toLocaleString(locale()), total: c.total.toLocaleString(locale()), entity: entity });
  } catch (e) {  }
}

/** Builds the narrowing suggestion chips that continue from the current result or prefill a template in the input. */
function narrowBox(hints) {
  const box = el("div", undefined, "narrow");
  box.append(el("span", t("narrowBy"), "muted"));
  for (const h of hints) {
    const group = el("span", undefined, "narrowgroup");
    if (h.kind === "options" && h.options && h.options.length) {
      group.append(el("span", h.label + ":", "narrowlabel"));
      for (const o of h.options) {
        const b = el("button", o, "chipbtn");
        b.onclick = () => { document.getElementById("refineText").value = t("narrowSentence", { label: h.label, value: o }); document.getElementById("refineGo").click(); };
        group.append(b);
      }
    } else {
      const b = el("button", h.label + "…", "chipbtn");
      b.onclick = () => { const box2 = document.getElementById("refineText"); box2.value = t("narrowSentence", { label: h.label, value: "" }).replace(/ +$/, "") + " "; box2.focus(); };
      group.append(b);
    }
    box.append(group);
  }
  return box;
}

// BEGIN chartKit
var chartKit = (function () {
  var PALETTE = ['#0b5cad', '#d9822b', '#2a9d8f', '#8e5ea2', '#b00020', '#6b7280'];
  var DASHES = [[], [6, 4], [2, 3], [10, 3, 2, 3], [], [4, 4]];

  /** Folds text to lower case without diacritics for matching. */
  function fold(s) { return String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/ı/g, 'i'); }

  /** Checks whether the question asks for a chart, otherwise the table is shown first. */
  function wants(question) {
    var f = fold(question);
    return /(grafi[gk]|cizelge|trend|dagilim|chart|graph|plot|distribution|over time|zaman icinde)/.test(f);
  }

  /** Computes rounded axis tick values for a value range. */
  function niceTicks(min, max) {
    if (!(max > min)) { max = min + 1; }
    var raw = (max - min) / 4;
    var pow = Math.pow(10, Math.floor(Math.log10(raw)));
    var f = raw / pow;
    var step = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * pow;
    var lo = Math.floor(min / step) * step;
    var hi = Math.ceil(max / step) * step;
    var ticks = [];
    for (var v = lo; v <= hi + step / 2; v += step) { ticks.push(Math.abs(v) < step / 1e6 ? 0 : v); }
    return { min: lo, max: hi, ticks: ticks };
  }

  /** Formats an x-axis label for the chart category kind and locale. */
  function xLabel(xKind, raw, locale) {
    try {
      if (xKind === 'month') { return new Date(2000, +raw - 1, 1).toLocaleDateString(locale, { month: 'short' }); }
      if (xKind === 'quarter') { return 'Q' + raw; }
      if (xKind === 'week') { return 'W' + raw; }
      if (xKind === 'yearmonth') { var p = raw.split('-'); return new Date(+p[0], +p[1] - 1, 1).toLocaleDateString(locale, { month: 'short', year: '2-digit' }); }
      if (xKind === 'yearquarter') { var q = raw.split('-'); return 'Q' + q[1] + ' ' + q[0]; }
    } catch (e) {  }
    return raw;
  }

  /** Builds the drawable chart model from the server chart data using the caller-supplied translated texts. */
  function model(chart, locale, texts) {
    var n = chart.labels.length;
    var labels = chart.labels.map(function (raw, i) {
      if (chart.xKind === 'category') {
        if (chart.hasOther && i === n - 1) { return texts.other; }
        return raw === '(empty)' ? texts.empty : raw;
      }
      return xLabel(chart.xKind, raw, locale);
    });
    var digits = chart.fn === 'count' || chart.fn === 'countcolumn' ? 0 : 2;
    var nf = new Intl.NumberFormat(locale, { maximumFractionDigits: digits });
    var series = chart.series.map(function (s, i) {
      return { name: s.name, values: s.values, color: PALETTE[i % PALETTE.length], dash: DASHES[i % DASHES.length] };
    });
    var min = 0, max = 0, hi = null, lo = null;
    series.forEach(function (s) {
      s.values.forEach(function (v, i) {
        if (v === null) { return; }
        if (v < min) { min = v; }
        if (v > max) { max = v; }
        if (hi === null || v > hi.v) { hi = { v: v, label: labels[i] }; }
        if (lo === null || v < lo.v) { lo = { v: v, label: labels[i] }; }
      });
    });
    var axis = niceTicks(min, max);
    var kindText = chart.kind === 'line' ? texts.line : texts.bar;
    var summary = kindText + ': ' + chart.measure + ', ' + n + ' ' + (hi ? '(' + hi.label + ' ' + nf.format(hi.v) + ' / ' + lo.label + ' ' + nf.format(lo.v) + ')' : '');
    return {
      kind: chart.kind, labels: labels, series: series, axis: axis, format: function (v) { return nf.format(v); },
      measure: chart.measure, summary: summary, partial: !!chart.partial, omitted: chart.omitted || 0,
      height: chart.kind === 'hbar' ? Math.max(120, 26 * n + 36) : 260
    };
  }

  /** Shortens text with an ellipsis so it fits a maximum pixel width. */
  function fit(ctx, text, maxWidth) {
    if (ctx.measureText(text).width <= maxWidth) { return text; }
    var t = text;
    while (t.length > 1 && ctx.measureText(t + '…').width > maxWidth) { t = t.slice(0, -1); }
    return t + '…';
  }

  /** Returns the widest measured width among the given texts. */
  function widest(ctx, texts) {
    return texts.reduce(function (m, t) { return Math.max(m, ctx.measureText(t).width); }, 0);
  }

  /** Draws a vertical chart (line or bar) onto the canvas context. */
  function draw(ctx, m, w, h) {
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, w, h);
    ctx.font = '12px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.lineWidth = 1;
    if (m.kind === 'hbar') { drawHorizontal(ctx, m, w, h); return; }
    var legendHeight = m.series.length > 1 ? 24 : 0;
    var tickTexts = m.axis.ticks.map(m.format);
    var left = 10 + widest(ctx, tickTexts);
    var x0 = left, x1 = w - 14, y0 = 14 + legendHeight, y1 = h - 34;
    var range = m.axis.max - m.axis.min || 1;
    /** Maps a data value to a vertical canvas coordinate. */
    function yOf(v) { return y1 - (v - m.axis.min) / range * (y1 - y0); }
    ctx.textAlign = 'right';
    m.axis.ticks.forEach(function (tick, i) {
      var y = yOf(tick);
      ctx.strokeStyle = tick === 0 ? '#555' : '#e2e6ea';
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.lineTo(x1, y); ctx.stroke();
      ctx.fillStyle = '#444';
      ctx.fillText(tickTexts[i], x0 - 6, y);
    });
    var n = m.labels.length;
    var slot = (x1 - x0) / n;
    var centerOf = function (i) { return x0 + (i + 0.5) * slot; };
    ctx.textAlign = 'center';
    ctx.fillStyle = '#444';
    var labelWidth = widest(ctx, m.labels) + 8;
    var every = Math.max(1, Math.ceil(labelWidth / slot));
    m.labels.forEach(function (label, i) {
      if (i % every === 0) { ctx.fillText(fit(ctx, label, Math.max(slot * every - 4, 20)), centerOf(i), y1 + 14); }
    });
    var showValues = n <= 12 && m.series.length === 1;
    if (m.kind === 'bar') {
      var barWidth = Math.min(slot * 0.7, 48);
      m.series[0].values.forEach(function (v, i) {
        if (v === null) { return; }
        ctx.fillStyle = m.series[0].color;
        ctx.fillRect(centerOf(i) - barWidth / 2, Math.min(yOf(v), yOf(0)), barWidth, Math.abs(yOf(v) - yOf(0)));
        if (showValues) { ctx.fillStyle = '#222'; ctx.fillText(m.format(v), centerOf(i), (v >= 0 ? yOf(v) - 9 : yOf(v) + 9)); }
      });
    } else {
      m.series.forEach(function (s) {
        ctx.strokeStyle = s.color; ctx.fillStyle = s.color; ctx.lineWidth = 2; ctx.setLineDash(s.dash);
        ctx.beginPath();
        var started = false;
        s.values.forEach(function (v, i) {
          if (v === null) { started = false; return; }
          if (!started) { ctx.moveTo(centerOf(i), yOf(v)); started = true; } else { ctx.lineTo(centerOf(i), yOf(v)); }
        });
        ctx.stroke();
        ctx.setLineDash([]);
        s.values.forEach(function (v, i) {
          if (v === null) { return; }
          ctx.beginPath(); ctx.arc(centerOf(i), yOf(v), 3, 0, Math.PI * 2); ctx.fill();
          if (showValues) { ctx.fillStyle = '#222'; ctx.fillText(m.format(v), centerOf(i), yOf(v) - 10); ctx.fillStyle = s.color; }
        });
      });
      ctx.lineWidth = 1;
    }
    if (legendHeight) {
      var x = x0;
      ctx.textAlign = 'left';
      m.series.forEach(function (s) {
        ctx.strokeStyle = s.color; ctx.lineWidth = 2; ctx.setLineDash(s.dash);
        ctx.beginPath(); ctx.moveTo(x, 12); ctx.lineTo(x + 24, 12); ctx.stroke();
        ctx.setLineDash([]);
        ctx.fillStyle = '#222';
        ctx.fillText(s.name, x + 30, 12);
        x += 30 + ctx.measureText(s.name).width + 16;
      });
      ctx.lineWidth = 1;
    }
  }

  /** Draws a horizontal bar chart onto the canvas context. */
  function drawHorizontal(ctx, m, w, h) {
    var maxLabel = Math.min(widest(ctx, m.labels), 150);
    var x0 = 10 + maxLabel + 8, x1 = w - 60, y0 = 8, y1 = h - 8;
    var n = m.labels.length;
    var rowHeight = (y1 - y0) / n;
    var range = m.axis.max - m.axis.min || 1;
    /** Maps a data value to a horizontal canvas coordinate. */
    function xOf(v) { return x0 + (v - m.axis.min) / range * (x1 - x0); }
    ctx.strokeStyle = '#555';
    ctx.beginPath(); ctx.moveTo(xOf(0), y0); ctx.lineTo(xOf(0), y1); ctx.stroke();
    m.series[0].values.forEach(function (v, i) {
      var cy = y0 + (i + 0.5) * rowHeight;
      ctx.textAlign = 'right'; ctx.fillStyle = '#444';
      ctx.fillText(fit(ctx, m.labels[i], maxLabel), x0 - 8, cy);
      if (v === null) { return; }
      var barHeight = Math.min(rowHeight * 0.7, 22);
      ctx.fillStyle = m.series[0].color;
      ctx.fillRect(Math.min(xOf(v), xOf(0)), cy - barHeight / 2, Math.abs(xOf(v) - xOf(0)), barHeight);
      ctx.textAlign = 'left'; ctx.fillStyle = '#222';
      ctx.fillText(m.format(v), Math.max(xOf(v), xOf(0)) + 6, cy);
    });
  }

  return { wants: wants, niceTicks: niceTicks, model: model, draw: draw };
})();
// END chartKit

// BEGIN explainText
/** Builds the plain-language explanation of the executed query from the server structured explanation. */
function explainText(e, over, fmt, lang) {
  var X_EN = {
    among: 'Among the records you can see', records: '{n} records', record: 'a specific record', or: 'or',
    by: 'by {field}', asc: 'ascending', desc: 'descending', top: 'first {n}', grouped: 'grouped by {fields}', aggregate: 'Aggregate',
    measures: { 'count': 'Count', 'countcolumn': 'Count of {field}', 'sum': 'Sum of {field}', 'avg': 'Average of {field}', 'min': 'Lowest {field}', 'max': 'Highest {field}' },
    ops: {
      'eq': '=', 'ne': '≠', 'gt': '>', 'ge': '≥', 'lt': '<', 'le': '≤', 'like': 'matches', 'not-like': 'does not match',
      'contains': 'contains', 'begins-with': 'starts with', 'ends-with': 'ends with',
      'not-contains': 'does not contain', 'not-begins-with': 'does not start with', 'not-ends-with': 'does not end with',
      'in': 'is one of', 'not-in': 'is not one of', 'null': 'is empty', 'not-null': 'is not empty', 'between': 'is between', 'not-between': 'is not between',
      'contain-values': 'includes', 'not-contain-values': 'does not include', 'neq': '≠',
      'eq-userid': 'is me', 'ne-userid': 'is not me', 'eq-businessid': 'is my business unit', 'ne-businessid': 'is not my business unit'
    },
    dates: {
      'today': 'Today', 'yesterday': 'Yesterday', 'tomorrow': 'Tomorrow',
      'this-week': 'This week', 'last-week': 'Last week', 'next-week': 'Next week',
      'this-month': 'This month', 'last-month': 'Last month', 'next-month': 'Next month',
      'this-year': 'This year', 'last-year': 'Last year', 'next-year': 'Next year',
      'last-seven-days': 'Last 7 days',
      'last-x-days': 'Last {n} days', 'next-x-days': 'Next {n} days', 'last-x-weeks': 'Last {n} weeks', 'next-x-weeks': 'Next {n} weeks',
      'last-x-months': 'Last {n} months', 'next-x-months': 'Next {n} months', 'last-x-years': 'Last {n} years', 'next-x-years': 'Next {n} years',
      'olderthan-x-days': 'Older than {n} days', 'olderthan-x-weeks': 'Older than {n} weeks', 'olderthan-x-months': 'Older than {n} months', 'olderthan-x-years': 'Older than {n} years',
      'on': 'is on', 'on-or-after': 'is on or after', 'on-or-before': 'is on or before',
      'last-x-hours': 'Last {n} hours', 'next-x-hours': 'Next {n} hours', 'this-fiscal-year': 'This fiscal year', 'last-fiscal-year': 'Last fiscal year', 'next-fiscal-year': 'Next fiscal year',
      'this-fiscal-period': 'This fiscal period', 'last-fiscal-period': 'Last fiscal period', 'next-fiscal-period': 'Next fiscal period'
    }
  };
  var X_TR = {
    among: 'Görebildiğiniz kayıtlar arasında', records: '{n} kayıt', record: 'belirli bir kayıt', or: 'veya',
    by: '{field} alanına göre', asc: 'artan', desc: 'azalan', top: 'ilk {n}', grouped: '{fields} alanına göre gruplanmış', aggregate: 'Toplama',
    measures: { 'count': 'Sayı', 'countcolumn': '{field} sayısı', 'sum': '{field} toplamı', 'avg': '{field} ortalaması', 'min': 'En düşük {field}', 'max': 'En yüksek {field}' },
    ops: {
      'eq': '=', 'ne': '≠', 'gt': '>', 'ge': '≥', 'lt': '<', 'le': '≤', 'like': 'şuna uyar:', 'not-like': 'şuna uymaz:',
      'contains': 'şunu içerir:', 'begins-with': 'şununla başlar:', 'ends-with': 'şununla biter:',
      'not-contains': 'şunu içermez:', 'not-begins-with': 'şununla başlamaz:', 'not-ends-with': 'şununla bitmez:',
      'in': 'şunlardan biri:', 'not-in': 'şunlardan biri değil:', 'null': 'boş', 'not-null': 'dolu', 'between': 'şu aralıkta:', 'not-between': 'şu aralıkta değil:',
      'contain-values': 'şunları içerir:', 'not-contain-values': 'şunları içermez:', 'neq': '≠',
      'eq-userid': 'ben', 'ne-userid': 'ben değil', 'eq-businessid': 'iş birimim', 'ne-businessid': 'iş birimim değil'
    },
    dates: {
      'today': 'Bugün', 'yesterday': 'Dün', 'tomorrow': 'Yarın',
      'this-week': 'Bu hafta', 'last-week': 'Geçen hafta', 'next-week': 'Gelecek hafta',
      'this-month': 'Bu ay', 'last-month': 'Geçen ay', 'next-month': 'Gelecek ay',
      'this-year': 'Bu yıl', 'last-year': 'Geçen yıl', 'next-year': 'Gelecek yıl',
      'last-seven-days': 'Son 7 gün',
      'last-x-days': 'Son {n} gün', 'next-x-days': 'Önümüzdeki {n} gün', 'last-x-weeks': 'Son {n} hafta', 'next-x-weeks': 'Önümüzdeki {n} hafta',
      'last-x-months': 'Son {n} ay', 'next-x-months': 'Önümüzdeki {n} ay', 'last-x-years': 'Son {n} yıl', 'next-x-years': 'Önümüzdeki {n} yıl',
      'olderthan-x-days': '{n} günden eski', 'olderthan-x-weeks': '{n} haftadan eski', 'olderthan-x-months': '{n} aydan eski', 'olderthan-x-years': '{n} yıldan eski',
      'on': 'şu tarihte:', 'on-or-after': 'şu tarihte ya da sonrasında:', 'on-or-before': 'şu tarihte ya da öncesinde:',
      'last-x-hours': 'Son {n} saat', 'next-x-hours': 'Önümüzdeki {n} saat', 'this-fiscal-year': 'Bu mali yıl', 'last-fiscal-year': 'Geçen mali yıl', 'next-fiscal-year': 'Gelecek mali yıl',
      'this-fiscal-period': 'Bu mali dönem', 'last-fiscal-period': 'Geçen mali dönem', 'next-fiscal-period': 'Gelecek mali dönem'
    }
  };
  var X = lang === 'tr' ? X_TR : X_EN;
  if (over) { Object.keys(over).forEach(function (k) { X[k] = over[k]; }); }

  /** Replaces placeholders in a text template with the given values. */
  function fill(s, v) { return String(s).replace(/\{(\w+)\}/g, function (m, k) { return v[k] === undefined ? m : v[k]; }); }
  /** Formats an ISO day with the supplied formatter or returns it unchanged. */
  function day(iso) { return fmt ? fmt(iso) : iso; }

  /** Renders a filter value as its label or raw value, never writing record ids. */
  function valueText(f) {
    var named = [];
    var unnamed = 0;
    (f.values || []).forEach(function (v) {
      var s = v.label || v.value;
      if (s) { named.push(s); } else { unnamed++; }
    });
    if (named.length === 0 && unnamed > 0) { return unnamed === 1 ? X.record : fill(X.records, { n: unnamed }); }
    return named.join(', ');
  }

  /** Renders one explanation filter as readable text. */
  function filterText(f) {
    var label = f.fieldLabel;
    var phrase = X.dates[f.op];
    var n = f.values && f.values.length ? f.values[0].value : '';
    var text;
    if (phrase && (f.from || f.to)) {
      if (f.op === 'on') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.from); }
      else if (f.op === 'on-or-after') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.from); }
      else if (f.op === 'on-or-before') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.to); }
      else {
        var range;
        if (f.from && f.to && f.from === f.to) { range = '(' + day(f.from) + ')'; }
        else if (f.from && f.to) { range = '(' + day(f.from) + ' – ' + day(f.to) + ')'; }
        else if (f.from) { range = '(≥ ' + day(f.from) + ')'; }
        else { range = '(≤ ' + day(f.to) + ')'; }
        text = label + ': ' + fill(phrase, { n: n }) + ' ' + range + ' ≈';
      }
    } else {
      var hasN = n !== '' && n !== null && n !== undefined;
      var word = X.ops[f.op] || (phrase && (hasN || phrase.indexOf('{n}') < 0) ? fill(phrase, { n: n }) : f.op);
      var numbered = !X.ops[f.op] && phrase && phrase.indexOf('{n}') >= 0 && hasN;
      var vals = f.op === 'null' || f.op === 'not-null' || numbered ? '' : valueText(f);
      text = label + ' ' + word + (vals ? ' ' + vals : '');
    }
    return f.via ? f.via + ': ' + text : text;
  }

  var parts = [];
  var groups = {};
  var groupIds = {};
  (e.filters || []).forEach(function (f) {
    if (f.group) {
      if (!groups[f.group]) { groups[f.group] = []; groupIds[f.group] = []; parts.push({ group: f.group }); }
      groups[f.group].push(filterText(f));
      groupIds[f.group].push(f.id);
    } else {
      parts.push({ text: filterText(f), ids: [f.id] });
    }
  });
  var items = parts.map(function (p) {
    return p.group ? { text: '(' + groups[p.group].join(' ' + X.or + ' ') + ')', ids: groupIds[p.group] } : { text: p.text, ids: p.ids };
  });
  var lines = items.map(function (i) { return i.text; });

  /** Renders an aggregate measure as readable text. */
  function measureText(m) { return fill(X.measures[m.fn] || X.measures.count, { field: m.fieldLabel || '' }); }
  if (e.aggregate) {
    var head = (e.measures || []).length ? e.measures.map(measureText).join(', ') : X.aggregate;
    lines.push(e.groupBy && e.groupBy.length ? head + ' ' + fill(X.grouped, { fields: e.groupBy.join(', ') }) : head);
  }
  (e.sort || []).forEach(function (s) {
    var what = s.fn ? measureText({ fn: s.fn, fieldLabel: s.label }) : s.label;
    lines.push(fill(X.by, { field: what }) + ' ' + (s.descending ? X.desc : X.asc));
  });
  if (e.top) { lines.push(fill(X.top, { n: e.top })); }

  var summary = e.entityLabel + (lines.length ? ' · ' + lines.join(' · ') : '');
  if (summary.length > 160) { summary = summary.substring(0, 159) + '…'; }
  return { summary: summary, lines: lines.concat([X.among]), items: items };
}
// END explainText

/** Converts an ISO day to a short local display, adding the year when it differs from the current year. */
function formatDay(iso) {
  const p = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso || "");
  if (!p) return iso;
  try {
    const date = new Date(+p[1], +p[2] - 1, +p[3]);
    const options = { day: "numeric", month: "short" };
    if (date.getFullYear() !== new Date().getFullYear()) options.year = "numeric";
    return date.toLocaleDateString(locale(), options);
  } catch (e) { return iso; }
}

/** Renders the result data into the page. */
function render(d) {
  document.title = t("pageTitleWith", { entity: d.entity });
  meta.textContent = t("fresh", { entity: d.entity });
  const fixedList = d.fromChain || (d.explanation && d.explanation.filters.some(f => f.values.length >= 2 && f.values.every(v => v.value === null && v.label === null)));
  if (fixedList) {
    out.append(el("p", t("fixedList"), "muted"));
  }
  if (d.truncated) {
    const warn = el("p", t("truncated"), "warn");
    out.append(warn);
    loadCount(warn, d.table.rows.length);
    if (d.narrowHints && d.narrowHints.length) out.append(narrowBox(d.narrowHints));
  }

  if (d.explanation) {
    const x = explainText(d.explanation, null, formatDay, language);
    const exp = el("details", undefined, "explain");
    exp.append(el("summary", x.summary));
    x.lines.forEach(line => exp.append(el("div", line, "explainline")));
    out.append(exp);
  }
  if (d.fetchXml) {
    const det = el("details");
    det.append(el("summary", t("fetchXml")));
    det.append(el("pre", d.fetchXml));
    const copy = el("button", t("copy"));
    copy.onclick = () => {
      if (navigator.clipboard) navigator.clipboard.writeText(d.fetchXml);
      copy.textContent = t("copied");
      setTimeout(() => { copy.textContent = t("copy"); }, 1500);
    };
    det.append(copy);
    out.append(det);
  }

  const tbl_data = d.table;
  const wrap = el("div", undefined, "tablewrap");
  const tbl = el("table");
  wrap.append(tbl);

  if (tbl_data.rows.length === 0) {
    const head = el("tr");
    tbl_data.columns.forEach((c, i) => head.append(el("th", tbl_data.labels && tbl_data.labels[i] ? tbl_data.labels[i] : c)));
    const note = el("td", t("noRecords"));
    note.colSpan = Math.max(1, tbl_data.columns.length);
    const noteRow = el("tr");
    noteRow.append(note);
    tbl.append(head, noteRow);
    out.append(wrap);
    return;
  }

  const search = el("input");
  search.placeholder = t("searchResults");
  const info = el("span", "", "muted");
  const bar = el("div", undefined, "bar");
  bar.append(search, info);
  const pager = el("div", undefined, "pager");
  out.append(bar, wrap, pager);

  if (d.chart) {
    const texts = { other: t("chartOther"), empty: t("chartEmpty"), line: t("chartLine"), bar: t("chartBar") };
    const model = chartKit.model(d.chart, locale(), texts);
    const switchBar = el("div", undefined, "bar");
    const chartButton = el("button", t("chartView"));
    const tableButton = el("button", t("tableView"));
    const png = el("button", t("chartPng"));
    switchBar.append(chartButton, tableButton, png);
    const box = el("div", undefined, "chartbox");
    const canvas = document.createElement("canvas");
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", model.summary);
    canvas.style.cssText = "width:100%;max-width:900px;display:block;";
    box.append(canvas);
    if (model.partial) box.append(el("p", t("chartPartial"), "warn"));
    if (model.omitted) box.append(el("p", t("chartOmitted", { n: model.omitted }), "muted"));
    out.insertBefore(switchBar, bar);
    out.insertBefore(box, bar);
    let drawn = 0;
    /** Paints the chart on the canvas. */
    function paint() {
      const width = Math.max(280, Math.round(box.clientWidth || 640));
      if (drawn === width) return;
      drawn = width;
      const ratio = window.devicePixelRatio || 1;
      canvas.width = Math.round(width * ratio);
      canvas.height = Math.round(model.height * ratio);
      canvas.style.height = model.height + "px";
      const ctx = canvas.getContext("2d");
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
      chartKit.draw(ctx, model, width, model.height);
    }
    /** Shows the given chart on the page. */
    function show(chart) {
      box.style.display = chart ? "" : "none";
      png.style.display = chart ? "" : "none";
      [bar, wrap, pager].forEach(e => { e.style.display = chart ? "none" : ""; });
      chartButton.setAttribute("aria-pressed", chart ? "true" : "false");
      tableButton.setAttribute("aria-pressed", chart ? "false" : "true");
      if (chart) paint();
    }
    chartButton.onclick = () => show(true);
    tableButton.onclick = () => show(false);
    png.onclick = () => {
      canvas.toBlob(blob => {
        if (!blob) return;
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = "chart.png";
        link.click();
        setTimeout(() => URL.revokeObjectURL(link.href), 1000);
      });
    };
    show(false);
  }

  const links = !!(tbl_data.rowIds && d.recordUrlBase && /^https?:\/\//i.test(d.recordUrlBase));
  /** Renders the open-record link cell for a row id. */
  function recordCell(id) {
    const td = el("td");
    if (id) {
      const a = el("a", t("openRecord"));
      a.href = d.recordUrlBase + encodeURIComponent(id);
      a.target = "_blank";
      a.rel = "noopener noreferrer";
      a.title = t("openRecordHint");
      td.append(a);
    }
    return td;
  }

  const size = 50;
  let page = 0;
  let filtered = tbl_data.rows;
  let printAll = false;

  /** Draws the chart onto the canvas. */
  function draw() {
    const pageSize = printAll ? Math.max(1, filtered.length) : size;
    const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
    if (page >= pages) page = pages - 1;
    tbl.replaceChildren();
    const head = el("tr");
    if (links) head.append(el("th"));
    tbl_data.columns.forEach((c, i) => head.append(el("th", tbl_data.labels && tbl_data.labels[i] ? tbl_data.labels[i] : c)));
    tbl.append(head);
    for (const row of filtered.slice(page * pageSize, page * pageSize + pageSize)) {
      const tr = el("tr");
      if (links) tr.append(recordCell(tbl_data.rowIds[tbl_data.rows.indexOf(row)]));
      for (const cell of row) tr.append(el("td", cell));
      tbl.append(tr);
    }
    info.textContent = t("rowsOf", { a: filtered.length, b: tbl_data.rows.length });
    pager.replaceChildren();
    const prev = el("button", t("prev")); prev.disabled = page === 0; prev.onclick = () => { page--; draw(); };
    const next = el("button", t("next")); next.disabled = page >= pages - 1; next.onclick = () => { page++; draw(); };
    const printBtn = el("button", t("print"));
    printBtn.title = t("printHint");
    printBtn.onclick = () => {
      printAll = true;
      draw();
      window.print();
    };
    pager.append(prev, el("span", t("pageOf", { a: page + 1, b: pages })), next, printBtn);
  }
  window.addEventListener("afterprint", () => {
    if (printAll) { printAll = false; draw(); }
  });

  search.oninput = () => {
    const needle = fold(search.value.trim());
    filtered = needle ? tbl_data.rows.filter(r => r.some(c => fold(c).includes(needle))) : tbl_data.rows;
    page = 0;
    draw();
  };
  draw();
}

load();
