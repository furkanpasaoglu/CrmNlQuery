/** Looks up an element by id. */
const $ = id => document.getElementById(id);
let currentQuestion = "";
let answers = {};

/** Folds text to a Turkish-safe lowercase form matching the server's name folding. */
function fold(s) {
  return s
    .replace(/[İIıi]/g, "i").replace(/[Şş]/g, "s").replace(/[Ğğ]/g, "g")
    .replace(/[Üü]/g, "u").replace(/[Öö]/g, "o").replace(/[Çç]/g, "c")
    .toLowerCase();
}

/** Creates a DOM element with optional text and class, setting text safely. */
function el(tag, text, cls) {
  const e = document.createElement(tag);
  if (text !== undefined) e.textContent = text;
  if (cls) e.className = cls;
  return e;
}

/** Sends the typed question to the ask endpoint and renders the outcome. */
async function ask() {
  currentQuestion = $("q").value.trim();
  answers = {};
  await run();
}

/** Runs the current action of the page, such as a query or a request. */
async function run() {
  $("status").textContent = "Thinking…";
  $("out").replaceChildren();
  try {
    const res = await fetch("api/ask", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ question: currentQuestion, answers })
    });
    const data = await res.json();
    $("status").textContent = "";
    render(data);
  } catch (e) {
    $("status").textContent = "Error: " + e.message;
  }
}

/** Renders a server outcome into the page. */
function render(d) {
  if (d.kind === "failure") $("out").append(el("p", d.message, "error"));
  else if (d.kind === "question") renderQuestion(d);
  else renderAnswer(d);
}

/** Renders the question part of an outcome. */
function renderQuestion(d) {
  const out = $("out");
  out.append(el("p", d.text, "question"));
  const box = el("div", undefined, "choices");
  for (const c of d.choices) {
    const b = el("button", c.label);
    b.onclick = () => { answers[d.key] = c.value; run(); };
    box.append(b);
  }
  if (d.allowFreeText) {
    const input = el("input");
    input.placeholder = d.questionKind === "columns" ? "or type column names, e.g. id, name" : "or rephrase your question";
    const go = el("button", "Send");
    go.onclick = () => {
      const v = input.value.trim();
      if (!v) return;
      if (d.questionKind === "columns") { answers[d.key] = "custom:" + v; }
      else { currentQuestion = v; answers = {}; }
      run();
    };
    box.append(input, go);
  }
  out.append(box);
}

/** Renders the answer part of an outcome, such as the result table. */
function renderAnswer(d) {
  const out = $("out");
  (d.stepNotes || []).forEach(n => out.append(el("p", n, "warn")));
  out.append(el("p", d.understood));

  if (d.fetchXml) {
    const det = el("details");
    det.append(el("summary", "Generated FetchXML"));
    det.append(el("pre", d.fetchXml));
    const copy = el("button", "Copy");
    copy.onclick = () => navigator.clipboard.writeText(d.fetchXml);
    det.append(copy);
    out.append(det);
  }

  if (d.truncated) out.append(el("p", "Result truncated at 500 rows — narrow your question.", "warn"));

  if (d.resultId) {
    const link = el("a", "Open this result on its own page (new tab)");
    link.href = "result.html?id=" + encodeURIComponent(d.resultId);
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    out.append(link);
  }

  const t = d.table;
  if (t.rows.length === 0) {
    const empty = el("table");
    const head = el("tr");
    for (const c of t.columns) head.append(el("th", c));
    const note = el("td", "No records found.");
    note.colSpan = Math.max(1, t.columns.length);
    const noteRow = el("tr");
    noteRow.append(note);
    empty.append(head, noteRow);
    out.append(empty);
    return;
  }

  const search = el("input");
  search.placeholder = "Search in results…";
  const info = el("span", "", "info");
  const tbl = el("table");
  const pager = el("div", undefined, "pager");
  out.append(search, info, tbl, pager);

  const size = 25;
  let page = 0;
  let filtered = t.rows;

  /** Redraws the table for the current page of the filtered rows, clamping the page number and rebuilding headers and body. */
  function draw() {
    const pages = Math.max(1, Math.ceil(filtered.length / size));
    if (page >= pages) page = pages - 1;
    tbl.replaceChildren();
    const head = el("tr");
    for (const c of t.columns) head.append(el("th", c));
    tbl.append(head);
    for (const row of filtered.slice(page * size, page * size + size)) {
      const tr = el("tr");
      for (const cell of row) tr.append(el("td", cell));
      tbl.append(tr);
    }
    info.textContent = filtered.length + " of " + t.rows.length + " rows";
    pager.replaceChildren();
    const prev = el("button", "Prev"); prev.disabled = page === 0; prev.onclick = () => { page--; draw(); };
    const next = el("button", "Next"); next.disabled = page >= pages - 1; next.onclick = () => { page++; draw(); };
    pager.append(prev, el("span", "Page " + (page + 1) + " / " + pages), next);
  }

  search.oninput = () => {
    const needle = fold(search.value.trim());
    filtered = needle ? t.rows.filter(r => r.some(c => fold(c).includes(needle))) : t.rows;
    page = 0;
    draw();
  };
  draw();
}

$("ask").onclick = ask;
$("q").addEventListener("keydown", e => { if (e.key === "Enter") ask(); });
$("refresh").onclick = async () => {
  const res = await fetch("api/admin/metadata/refresh", { method: "POST" });
  $("status").textContent = res.ok ? "Metadata cache cleared; it reloads on the next question."
    : res.status === 404 ? "You need the Metadata.Refresh permission (admin panel) to do this." : "Could not refresh the metadata (" + res.status + ").";
};
