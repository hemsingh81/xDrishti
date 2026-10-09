/* xDrishti delivery plan — application (vanilla JS, no build step). Data: plan-data-*.js. Local edits live in localStorage. */
(function () {
  "use strict";
  const XD = window.XD;
  const KEY = "xd-plan-v1";
  const DAY = 864e5;
  const CAL_BOUND = ["P10", "P11", "OPS"]; // calendar-bound epics: duration is waiting/reviewing time, not build effort
  const SHORT = { P0: "Foundations", P1: "Data platform", P2: "Portfolio & live", P3: "Simulator & DSL", P4: "Exits", P5: "Strategy Lab", P6: "Learning engine", P7: "Nightly plan", P8: "Reports & UI", P9: "AI assistant", P10: "Paper trading", P11: "Small live", P12: "Execution", P13: "Tick & depth", P14: "Options", P15: "Fundamentals", OPS: "Operations" };
  const DOCS = ["01-overview", "02-requirements", "03-target-architecture", "04-accounts-access-and-operations", "05-data-and-brokers", "06-instruments-and-baskets", "07-strategies", "08-exits-and-risk", "09-learning-engine", "10-trade-selection", "11-strategy-lab", "12-portfolio-monitoring", "13-reports-and-ui", "14-ai-assistant", "15-smart-insights-and-automation", "16-configuration", "17-deployment-and-operations", "18-roadmap", "19-risks-and-compliance", "20-extended-modules", "21-glossary"];
  const NAV = [
    ["overview", "◉", "Overview"], ["roadmap", "▤", "Roadmap"], ["flow", "⇢", "Flow"], ["backlog", "☰", "Backlog"],
    ["board", "▦", "Board"], ["sprints", "◷", "Sprints"], ["risks", "⚑", "Risks & decisions"], ["process", "⟳", "How we work"],
  ];
  const TITLES = Object.fromEntries(NAV.map((n) => [n[0], n[2]]));
  const TYPES = ["feature", "tech", "spike", "ops", "docs", "test"];
  const AREAS = ["backend", "frontend", "fullstack", "data", "ml", "infra", "docs"];
  const PRIS = ["must", "should", "could"];

  /* ---------- helpers ---------- */
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  const parse = (s) => { const [y, m, d] = s.split("-").map(Number); return new Date(y, m - 1, d); };
  const addDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
  const days = (a, b) => Math.round((b - a) / DAY);
  const fmt = (d, o) => d.toLocaleDateString("en-GB", { day: "numeric", month: "short", ...(o || {}) });
  const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const pct = (a, b) => (b ? Math.round((a / b) * 100) : 0);
  const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 48);
  const stName = (id) => XD.statuses.find((s) => s.id === id)?.name ?? id;
  const wave = (id) => XD.waves.find((w) => w.id === id);
  const ucfirst = (s) => s[0].toUpperCase() + s.slice(1);

  /* ---------- state (local, per browser) ---------- */
  const defaults = () => ({ st: {}, done: {}, note: {}, ac: {}, q: {}, qd: {}, risk: {}, custom: [], f: { epic: "", status: "", type: "", area: "", pri: "", q: "", group: true, sort: "flow" }, b: { scope: "focus", q: "" }, sp: "next", rt: "questions", closed: {} });
  let state = defaults();
  try { const raw = localStorage.getItem(KEY); if (raw) { const p = JSON.parse(raw); state = { ...defaults(), ...p, f: { ...defaults().f, ...p.f }, b: { ...defaults().b, ...p.b } }; } } catch (e) { /* storage unavailable: run in memory */ }
  const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) { /* ignore */ } };

  /* ---------- model ---------- */
  const d0 = parse(XD.meta.start);
  const today = new Date(new Date().getFullYear(), new Date().getMonth(), new Date().getDate());
  let M = null;

  function build() {
    const epics = XD.epics.map((e) => ({ ...e }));
    const byEpic = Object.fromEntries(epics.map((e) => [e.id, e]));
    epics.forEach((e) => {
      const ends = e.after.map((a) => byEpic[a].end);
      e.start = ends.length ? new Date(Math.max(...ends)) : d0;
      e.end = addDays(e.start, e.weeks * 7);
    });
    const stories = [];
    XD.epics.forEach((src, i) => {
      const e = epics[i];
      const custom = state.custom.filter((c) => c.epic === e.id).map((c) => ({ ...c }));
      e.stories = [...src.stories.map((s) => ({ ...s })), ...custom];
      e.stories.forEach((s) => {
        s.epic = e.id;
        s.st = state.st[s.id] ?? s.st;
        s.note = state.note[s.id] ?? s.note;
        s.done = s.st === "done" ? state.done[s.id] ?? s.done ?? iso(today) : undefined;
        stories.push(s);
      });
    });
    const byId = Object.fromEntries(stories.map((s) => [s.id, s]));
    epics.forEach((e) => {
      const open = e.stories.filter((s) => s.st !== "done");
      const tot = open.reduce((a, s) => a + s.pts, 0) || 1;
      const dur = days(e.start, e.end);
      let cum = 0;
      e.stories.forEach((s) => {
        if (s.st === "done") { s.sprint = 0; s.date = parse(s.done); } else {
          cum += s.pts;
          s.date = addDays(e.start, Math.max(0, Math.round((dur * cum) / tot) - 1));
          s.sprint = Math.max(1, Math.floor(days(d0, s.date) / XD.meta.sprintDays) + 1);
        }
        s.unlocks = [];
      });
      e.pts = e.stories.reduce((a, s) => a + s.pts, 0);
      e.donePts = e.stories.filter((s) => s.st === "done").reduce((a, s) => a + s.pts, 0);
      e.remPts = e.pts - e.donePts;
      e.count = e.stories.length;
      e.doneCount = e.stories.length - open.length;
      e.state = e.doneCount === e.count ? "done" : e.doneCount > 0 || e.stories.some((s) => s.st === "doing" || s.st === "review") ? "progress" : "planned";
      e.color = wave(e.wave).color;
      e.unlocks = [];
    });
    stories.forEach((s) => s.dep.forEach((d) => byId[d] && byId[d].unlocks.push(s.id)));
    epics.forEach((e) => e.needs.forEach((n) => byEpic[n].unlocks.push(e.id)));
    const level = {};
    const lv = (e) => (level[e.id] ??= e.needs.length ? 1 + Math.max(...e.needs.map((n) => lv(byEpic[n]))) : 0);
    epics.forEach(lv);
    const active = epics.filter((e) => e.state !== "done").slice(0, 2).map((e) => e.id);
    M = { epics, byEpic, stories, byId, level, focus: active, end: new Date(Math.max(...epics.map((e) => e.end))) };
    M.sprintCount = Math.max(...stories.filter((s) => s.st !== "done").map((s) => s.sprint), 1);
  }

  const epicOf = (s) => M.byEpic[s.epic];
  const epicChip = (e, full) => `<span class="chip epic-chip" style="--wc:${e.color}" title="${esc(e.title)}">${esc(e.id)}${full ? " · " + esc(SHORT[e.id] || e.title) : ""}</span>`;
  const stChip = (s) => `<span class="chip st-${s}">${esc(stName(s))}</span>`;
  const priChip = (p) => `<span class="chip pri-${p}">${ucfirst(p)}</span>`;
  const bar = (a, b, color) => `<div class="bar" style="--wc:${color || "var(--accent)"}"><i style="width:${pct(a, b)}%"></i></div>`;
  const waiting = (s) => s.st !== "done" && s.dep.filter((d) => M.byId[d] && M.byId[d].st !== "done");
  const sprintRange = (n) => { const a = addDays(d0, (n - 1) * XD.meta.sprintDays); return `${fmt(a)} – ${fmt(addDays(a, XD.meta.sprintDays - 1))}`; };
  const qState = (q) => (state.qd[q.id] === undefined ? q.st : state.qd[q.id] ? "answered" : "open");
  const openQs = () => XD.questions.filter((q) => qState(q) === "open");
  const curSprint = () => { const n = Math.floor(days(d0, today) / XD.meta.sprintDays) + 1; return n; };
  const docLinks = (doc) => String(doc || "").split(/,\s*/).map((part) => {
    const m = part.match(/\b(0[1-9]|1\d|2[01])\b/);
    return m ? `<a href="${docsBase()}${DOCS[+m[1] - 1]}.html" target="_blank" rel="noopener">${esc(part)}</a>` : esc(part);
  }).join(", ");
  const inStack = () => location.pathname.startsWith("/prototype/"); // served by xd-proxy (same origin as the app)
  const docsBase = () => (inStack() ? "/docs/" : `${location.protocol}//${location.hostname || "localhost"}:8765/`);

  /* ---------- views ---------- */
  const views = {};

  views.overview = () => {
    const all = M.stories;
    const done = all.filter((s) => s.st === "done");
    const rem = all.filter((s) => s.st !== "done");
    const remPts = rem.reduce((a, s) => a + s.pts, 0);
    const donePts = done.reduce((a, s) => a + s.pts, 0);
    const doing = all.filter((s) => s.st === "doing" || s.st === "review");
    const ready = all.filter((s) => s.st === "ready");
    const blocked = all.filter((s) => s.st === "blocked");
    const openQ = openQs();
    const cur = M.epics.find((e) => e.state !== "done");
    const core = M.byEpic.P9, paper = M.byEpic.P10, live = M.byEpic.P11;
    const preStart = today < d0;
    const headline = preStart ? `Plan starts Mon ${fmt(d0)} — ${days(today, d0)} days from now` : `Sprint ${curSprint()} · ${sprintRange(curSprint())}`;
    return `
    <section class="hero">
      <h2>${esc(XD.meta.name)} delivery plan</h2>
      <p class="lead">${M.epics.length} epics, ${all.length} stories, ${remPts + donePts} points. Worked in order, one vertical slice at a time: <b>spec → agent builds → check.sh green → independent review → you approve</b>. Capacity assumed: ${XD.meta.capacity} focused hours a week.</p>
      <div class="dates">
        <span>${esc(headline)}</span>
        <span><b>${fmt(core.end, { year: "numeric" })}</b>core system complete (P0–P9)</span>
        <span><b>${fmt(paper.start, { year: "numeric" })}</b>paper trading starts</span>
        <span><b>${fmt(live.start, { year: "numeric" })}</b>earliest small live capital</span>
        <span><b>${fmt(M.end, { year: "numeric" })}</b>full roadmap (P0–P15)</span>
      </div>
    </section>
    <div class="tiles">
      <div class="tile"><div class="k">Stories done</div><div class="v">${done.length}<small class="dim"> / ${all.length}</small></div><div class="s">${pct(done.length, all.length)}% of all stories</div></div>
      <div class="tile"><div class="k">Effort done</div><div class="v">${donePts}<small class="dim"> pts</small></div><div class="s">${remPts} pts remaining ≈ ${Math.ceil(remPts / XD.meta.capacity)} weeks at capacity</div></div>
      <div class="tile"><div class="k">In progress / review</div><div class="v">${doing.length}</div><div class="s">${ready.length} ready to start</div></div>
      <div class="tile"><div class="k">Blocked</div><div class="v" style="color:${blocked.length ? "var(--bad)" : "inherit"}">${blocked.length}</div><div class="s">${blocked.length ? "waiting on your input" : "nothing blocked"}</div></div>
      <div class="tile"><div class="k">Open questions</div><div class="v" style="color:${openQ.length ? "var(--warn)" : "inherit"}">${openQ.length}</div><div class="s"><a href="#/risks">answer them →</a></div></div>
    </div>
    <div class="grid g-main">
      <div class="grid">
        <div class="card"><h2>Now <span class="muted" style="font-weight:400;font-size:12px">in progress · ready · blocked</span></h2>
          <div class="list">${[...doing, ...blocked, ...ready].slice(0, 12).map(storyRow).join("") || '<div class="empty">Nothing in progress — pick from Ready on the Board.</div>'}</div></div>
        <div class="card"><h2>Epics</h2><div class="list">${M.epics.map(epicRow).join("")}</div></div>
      </div>
      <div class="grid" style="align-content:start">
        ${cur ? `<div class="card" style="border-top:3px solid ${cur.color}"><h2>Current epic ${epicChip(cur)}</h2>
          <p><b>${esc(cur.title)}</b></p><p class="muted" style="margin:4px 0 10px">${esc(cur.goal)}</p>
          <div class="pbar">${bar(cur.donePts, cur.pts, cur.color)}<span>${pct(cur.donePts, cur.pts)}%</span></div>
          <p style="margin-top:10px"><b>Exit gate:</b> <span class="muted">${esc(cur.exit)}</span></p>
          <p style="margin-top:10px"><button class="btn sm" data-act="open-epic" data-id="${cur.id}" type="button">Open epic</button> <a class="btn sm" href="#/flow">See the flow</a></p></div>` : ""}
        <div class="card"><h2>Needs your input</h2>
          ${openQ.length ? `<div class="list">${openQ.slice(0, 5).map((q) => `<a class="row" href="#/risks"><span class="id">${q.id}</span><span class="t" title="${esc(q.t)}">${esc(q.t)}</span></a>`).join("")}</div>` : '<div class="empty">All open questions answered.</div>'}
        </div>
        <div class="card"><h2>v1 definition of success</h2>${XD.success.map((c) => { const ok = c.epics.every((id) => M.byEpic[id].state === "done"); return `<div class="check ${ok ? "ok" : ""}"><span class="mark">${ok ? "✓" : ""}</span><span>${esc(c.t)} <span class="dim">${c.epics.map((id) => epicChip(M.byEpic[id])).join(" ")}</span></span></div>`; }).join("")}</div>
      </div>
    </div>`;
  };

  function storyRow(s) {
    const e = epicOf(s);
    return `<button class="row" data-act="open-story" data-id="${s.id}" type="button"><span class="dot ${s.st}" title="${esc(stName(s.st))}"></span><span class="id">${s.id}</span><span class="t">${esc(s.t)}</span>${epicChip(e)}<span class="dim mono">${s.pts}</span></button>`;
  }
  function epicRow(e) {
    return `<button class="epic-row" style="--wc:${e.color}" data-act="open-epic" data-id="${e.id}" type="button">
      ${epicChip(e)}<span><b>${esc(SHORT[e.id] || e.title)}</b><br><span class="dim" style="font-size:12px">${fmt(e.start)} – ${fmt(e.end)} · ${e.weeks}w</span></span>
      <span class="pbar hide-s">${bar(e.donePts, e.pts, e.color)}<span>${pct(e.donePts, e.pts)}%</span></span>
      <span class="muted hide-s" style="font-size:12px">${e.doneCount}/${e.count} stories</span><span class="chip ${e.state === "done" ? "st-done" : e.state === "progress" ? "st-doing" : ""}">${e.state === "done" ? "Done" : e.state === "progress" ? "In progress" : "Planned"}</span></button>`;
  }

  views.roadmap = () => {
    const ppd = 3.2;
    const total = days(d0, M.end) + 14;
    const w = Math.round(total * ppd);
    const months = [];
    for (let d = new Date(d0.getFullYear(), d0.getMonth(), 1); d < M.end; d = new Date(d.getFullYear(), d.getMonth() + 1, 1)) {
      const x = Math.round(days(d0, d) * ppd);
      months.push(`<span style="left:${Math.max(0, x)}px">${d.toLocaleDateString("en-GB", { month: "short", year: d.getMonth() === 0 || x <= 0 ? "numeric" : undefined })}</span>`);
    }
    const grid = [...Array(Math.ceil(total / 14))].map((_, i) => `<i class="g-grid" style="left:${Math.round(i * 14 * ppd)}px"></i>`).join("");
    const todayX = Math.round(days(d0, today) * ppd);
    const rows = XD.waves.map((wv) => {
      const es = M.epics.filter((e) => e.wave === wv.id);
      return `<div class="g-row wave" style="--wc:${wv.color}"><div class="lab">${esc(wv.name)}</div><div class="g-track" style="width:${w}px"></div></div>` + es.map((e) => {
        const x = Math.round(days(d0, e.start) * ppd), bw = Math.round(days(e.start, e.end) * ppd);
        return `<div class="g-row" style="--wc:${e.color}"><div class="lab" data-act="open-epic" data-id="${e.id}" title="${esc(e.title)}">${epicChip(e)}<span style="font-size:13px">${esc(SHORT[e.id])}</span></div>
          <div class="g-track" style="width:${w}px">${grid}${todayX >= 0 ? `<div class="g-today" style="left:${todayX}px"></div>` : ""}
          <div class="g-bar" style="left:${x}px;width:${bw}px" data-act="open-epic" data-id="${e.id}" title="${esc(e.title)} — ${fmt(e.start)} to ${fmt(e.end)}"><i style="width:${pct(e.donePts, e.pts)}%"></i><span>${e.weeks}w · ${e.count} stories</span></div>
          <div class="g-ms ${e.state === "done" ? "ok" : ""}" style="left:${x + bw}px" data-act="open-epic" data-id="${e.id}" title="Exit gate: ${esc(e.exit)}"></div></div></div>`;
      }).join("");
    }).join("");
    return `<p class="muted" style="margin-bottom:10px">Dates follow the roadmap: each epic starts when the one it follows ends. ◆ marks the exit gate — the epic is only finished when its gate is met. Bars fill as stories are marked done.</p>
      <div class="legend">${XD.waves.map((wv) => `<span><i style="background:${wv.color}"></i>${wv.name}</span>`).join("")}<span>◆ exit gate</span></div>
      <div class="gantt"><div class="g-inner" style="width:${w + 230}px"><div class="g-head"><div class="lab">Epic</div><div class="g-months" style="width:${w}px">${months.join("")}</div></div>${rows}</div></div>`;
  };

  views.flow = () => {
    const NW = 116, NH = 46, GX = 52, GY = 14;
    const byLevel = {};
    M.epics.forEach((e) => (byLevel[M.level[e.id]] ??= []).push(e));
    const pos = {};
    const maxL = Math.max(...Object.keys(byLevel).map(Number));
    let maxRows = 0;
    for (let l = 0; l <= maxL; l++) { (byLevel[l] || []).forEach((e, i) => { pos[e.id] = { x: l * (NW + GX), y: i * (NH + GY) }; }); maxRows = Math.max(maxRows, (byLevel[l] || []).length); }
    const W = (maxL + 1) * (NW + GX) - GX, H = maxRows * (NH + GY) - GY;
    const edges = M.epics.flatMap((e) => e.needs.map((n) => {
      const a = pos[n], b = pos[e.id], x1 = a.x + NW, y1 = a.y + NH / 2, x2 = b.x, y2 = b.y + NH / 2, mx = (x1 + x2) / 2;
      return `<path class="edge" data-from="${n}" data-to="${e.id}" d="M${x1},${y1} C${mx},${y1} ${mx},${y2} ${x2},${y2}" marker-end="url(#arr)"/>`;
    })).join("");
    const nodes = M.epics.map((e) => `<button class="node ${e.state === "done" ? "done" : ""}" style="--wc:${e.color};left:${pos[e.id].x}px;top:${pos[e.id].y}px" data-act="open-epic" data-id="${e.id}" data-node="${e.id}" type="button" title="${esc(e.title)}"><b>${e.id}</b><span>${esc(SHORT[e.id])}</span><i class="p" style="width:${pct(e.donePts, e.pts)}%"></i></button>`).join("");
    const map = `<div class="map"><div style="position:relative;width:${W}px;height:${H}px"><svg width="${W}" height="${H}" aria-hidden="true"><defs><marker id="arr" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" fill="var(--border-strong)"/></marker></defs>${edges}</svg>${nodes}</div></div>`;
    const jr = M.epics.map((e, i) => {
      const open = state.closed[e.id] === undefined ? (i < 3 || e.state === "progress") : !state.closed[e.id];
      return `<details class="jr" style="--wc:${e.color}" data-epic="${e.id}" ${open ? "open" : ""}>
        <summary>${epicChip(e)}<span><span class="ttl">${esc(e.title)}</span><br><span class="sub">${fmt(e.start)} – ${fmt(e.end)} · ${e.weeks}w · ${e.count} stories · ${e.pts} pts${e.needs.length ? " · needs " + e.needs.join(", ") : ""}${e.unlocks.length ? " · unlocks " + e.unlocks.join(", ") : ""}</span></span>
        <span class="pbar">${bar(e.donePts, e.pts, e.color)}<span>${pct(e.donePts, e.pts)}%</span></span></summary>
        <p class="muted" style="padding:0 16px 8px">${esc(e.goal)}</p>
        <ol class="steps">${e.stories.map((s, n) => `<li class="step"><button class="sc ${s.st === "done" ? "done" : ""}" data-act="open-story" data-id="${s.id}" data-deps="${esc(s.dep.join(" "))}" type="button" title="${esc(s.t)}"><span class="n">${n + 1}</span><span class="dot ${s.st}"></span><span class="id">${s.id}</span><span class="nm">${esc(s.t)}</span><span class="pt">${s.pts}</span></button></li>`).join("")}</ol>
        <div class="gate"><b>◆ Exit gate</b><span>${esc(e.exit)}</span></div></details>`;
    }).join("");
    return `<p class="muted" style="margin-bottom:10px">Left to right: what must be finished before the next epic can start. Click a node for details. Below, every epic lists its stories in build order (numbered); hover a story to highlight what it depends on.</p>${map}<div class="section-title">Epics and stories in flow order</div><div style="display:flex;gap:8px;margin-bottom:10px"><button class="btn sm" data-act="flow-all" data-v="1" type="button">Expand all</button><button class="btn sm" data-act="flow-all" data-v="0" type="button">Collapse all</button></div>${jr}`;
  };

  function filtered() {
    const f = state.f, q = f.q.trim().toLowerCase();
    let list = M.stories.filter((s) => (!f.epic || s.epic === f.epic) && (!f.status || s.st === f.status) && (!f.type || s.type === f.type) && (!f.area || s.area === f.area) && (!f.pri || s.pri === f.pri) && (!q || `${s.id} ${s.t} ${s.doc} ${s.ac.join(" ")}`.toLowerCase().includes(q)));
    if (f.sort === "pri") list = [...list].sort((a, b) => PRIS.indexOf(a.pri) - PRIS.indexOf(b.pri));
    else if (f.sort === "pts") list = [...list].sort((a, b) => b.pts - a.pts);
    else if (f.sort === "sprint") list = [...list].sort((a, b) => a.sprint - b.sprint);
    return list;
  }
  const opts = (arr, cur, label) => `<option value="">${label}</option>${arr.map((a) => `<option value="${esc(a.v ?? a)}" ${(a.v ?? a) === cur ? "selected" : ""}>${esc(a.l ?? ucfirst(a))}</option>`).join("")}`;
  const epicOpts = () => M.epics.map((e) => ({ v: e.id, l: `${e.id} · ${SHORT[e.id]}` }));

  views.backlog = () => {
    const f = state.f, list = filtered();
    const pts = list.reduce((a, s) => a + s.pts, 0);
    const row = (s) => `<tr class="link" data-act="open-story" data-id="${s.id}"><td class="mono">${s.id}</td><td class="title">${esc(s.t)}</td><td>${epicChip(epicOf(s))}</td><td>${esc(s.type)}</td><td>${esc(s.area)}</td><td>${priChip(s.pri)}</td><td class="right">${s.pts}</td><td class="nowrap">${s.sprint ? "S" + s.sprint : "—"}</td>
      <td><select class="field" data-change="status" data-id="${s.id}" aria-label="Status of ${s.id}">${XD.statuses.map((x) => `<option value="${x.id}" ${x.id === s.st ? "selected" : ""}>${x.name}</option>`).join("")}</select></td></tr>`;
    let body = "";
    if (f.group && f.sort === "flow") {
      M.epics.forEach((e) => {
        const es = list.filter((s) => s.epic === e.id);
        if (!es.length) return;
        body += `<tr class="group"><td colspan="9" style="color:${e.color}">${epicChip(e)} ${esc(e.title)} <span class="dim" style="font-weight:400">· ${es.length} stories · ${es.reduce((a, s) => a + s.pts, 0)} pts</span></td></tr>${es.map(row).join("")}`;
      });
    } else body = list.map(row).join("");
    return `<div class="filters">
      <select class="field" data-filter="epic" aria-label="Epic">${opts(epicOpts(), f.epic, "All epics")}</select>
      <select class="field" data-filter="status" aria-label="Status">${opts(XD.statuses.map((s) => ({ v: s.id, l: s.name })), f.status, "Any status")}</select>
      <select class="field" data-filter="type" aria-label="Type">${opts(TYPES, f.type, "Any type")}</select>
      <select class="field" data-filter="area" aria-label="Area">${opts(AREAS, f.area, "Any area")}</select>
      <select class="field" data-filter="pri" aria-label="Priority">${opts(PRIS, f.pri, "Any priority")}</select>
      <select class="field" data-filter="sort" aria-label="Sort">${[["flow", "Flow order"], ["pri", "Priority"], ["pts", "Effort"], ["sprint", "Sprint"]].map(([v, l]) => `<option value="${v}" ${f.sort === v ? "selected" : ""}>Sort: ${l}</option>`).join("")}</select>
      <label class="chip" style="cursor:pointer"><input type="checkbox" data-filter="group" ${f.group ? "checked" : ""}> Group by epic</label>
      <button class="btn sm" data-act="clear-filters" type="button">Clear</button>
      <span class="count">${list.length} stories · ${pts} pts</span></div>
      <div class="tbl-wrap"><table><thead><tr><th>ID</th><th>Story</th><th>Epic</th><th>Type</th><th>Area</th><th>Priority</th><th class="right">Pts</th><th>Sprint</th><th>Status</th></tr></thead><tbody>${body || '<tr><td colspan="9" class="empty">No stories match the filters.</td></tr>'}</tbody></table></div>`;
  };

  views.board = () => {
    const b = state.b, q = b.q.trim().toLowerCase();
    const inScope = (s) => (b.scope === "all" ? true : b.scope === "focus" ? M.focus.includes(s.epic) : s.epic === b.scope) && (!q || `${s.id} ${s.t}`.toLowerCase().includes(q));
    const list = M.stories.filter(inScope);
    const scopes = [{ v: "focus", l: `Focus (${M.focus.join(" + ")})` }, { v: "all", l: "All epics" }, ...epicOpts()];
    return `<div class="filters"><select class="field" data-board="scope" aria-label="Scope">${scopes.map((o) => `<option value="${o.v}" ${o.v === b.scope ? "selected" : ""}>${esc(o.l)}</option>`).join("")}</select>
      <input class="field" style="width:200px" data-board="q" value="${esc(b.q)}" placeholder="Filter cards" aria-label="Filter cards"><span class="count">Drag a card to change its status — or open it and use the status menu. ${list.length} stories · ${list.reduce((a, s) => a + s.pts, 0)} pts</span></div>
      <div class="board">${XD.statuses.map((st) => {
        const cards = list.filter((s) => s.st === st.id);
        return `<div class="col" data-col="${st.id}"><h3><span>${st.name}</span><span>${cards.length} · ${cards.reduce((a, s) => a + s.pts, 0)}</span></h3>${cards.map((s) => `<div class="kc" style="--wc:${epicOf(s).color}" draggable="true" data-card="${s.id}" data-act="open-story" data-id="${s.id}"><div class="m"><span class="mono">${s.id}</span>${priChip(s.pri)}<span class="mono">${s.pts} pts</span></div><div class="t">${esc(s.t)}</div><div class="m"><span>${esc(s.area)}</span>${waiting(s).length && s.st !== "blocked" ? `<span class="chip st-blocked" title="Waiting for ${waiting(s).join(", ")}">waits ${waiting(s).length}</span>` : ""}</div></div>`).join("") || '<div class="empty">—</div>'}</div>`;
      }).join("")}</div>`;
  };

  views.sprints = () => {
    const n = M.sprintCount, cap = XD.meta.capacity * (XD.meta.sprintDays / 7);
    const load = Array.from({ length: n + 1 }, () => ({}));
    M.stories.filter((s) => s.st !== "done").forEach((s) => { const w = epicOf(s).wave; load[s.sprint][w] = (load[s.sprint][w] || 0) + s.pts; });
    const totals = load.map((l) => Object.values(l).reduce((a, b) => a + b, 0));
    const over = totals.slice(1).filter((t) => t > cap * 1.15).length;
    const maxY = Math.max(cap * 1.4, ...totals), BW = 22, GAP = 5, CH = 170;
    const bars = totals.slice(1).map((t, i) => {
      let y = CH;
      const x = 34 + i * (BW + GAP);
      const segs = XD.waves.map((wv) => { const v = load[i + 1][wv.id] || 0; if (!v) return ""; const h = (v / maxY) * CH; y -= h; return `<rect x="${x}" y="${y}" width="${BW}" height="${h}" fill="${wv.color}" rx="2"><title>S${i + 1} ${wv.name}: ${v} pts</title></rect>`; }).join("");
      return `${segs}<text x="${x + BW / 2}" y="${CH + 14}" font-size="9" text-anchor="middle" fill="var(--text-3)">${(i + 1) % 2 === 1 ? i + 1 : ""}</text>`;
    }).join("");
    const capY = CH - (cap / maxY) * CH;
    const chart = `<div class="chart"><div class="card-h" style="display:flex;justify-content:space-between;margin-bottom:6px"><b>Planned effort per sprint vs capacity</b><span class="muted" style="font-size:12px">${over ? `<span class="over-cap">${over} of ${n} sprints are more than 15% over capacity</span> — re-plan at the gates` : "Within capacity"}</span></div>
      <svg width="${34 + n * (BW + GAP) + 10}" height="${CH + 24}" role="img" aria-label="Planned effort per sprint"><line x1="30" x2="${34 + n * (BW + GAP)}" y1="${capY}" y2="${capY}" stroke="var(--bad)" stroke-dasharray="4 3"/><text x="0" y="${capY + 3}" font-size="10" fill="var(--bad)">${cap}</text>${bars}</svg>
      <div class="legend" style="margin:6px 0 0">${XD.waves.map((wv) => `<span><i style="background:${wv.color}"></i>${wv.name}</span>`).join("")}<span>Sprint = ${XD.meta.sprintDays} days · capacity ${cap} pts</span></div></div>`;
    const cur = curSprint();
    const shown = state.sp === "all" ? n : Math.min(n, Math.max(cur, 1) + 5);
    const first = state.sp === "all" ? 1 : Math.max(cur, 1);
    let list = "";
    const doneStories = M.stories.filter((s) => s.st === "done");
    list += `<div class="sprint"><div class="h"><b>Done so far</b><span class="muted">${doneStories.length} stories · ${doneStories.reduce((a, s) => a + s.pts, 0)} pts</span></div><div class="list">${doneStories.slice(0, state.sp === "all" ? 99 : 6).map(storyRow).join("")}${state.sp !== "all" && doneStories.length > 6 ? `<div class="dim" style="padding:4px 10px">+ ${doneStories.length - 6} more</div>` : ""}</div></div>`;
    for (let i = first; i <= shown; i++) {
      const ss = M.stories.filter((s) => s.st !== "done" && s.sprint === i);
      if (!ss.length) continue;
      const t = ss.reduce((a, s) => a + s.pts, 0);
      list += `<div class="sprint ${i === cur ? "now" : ""}"><div class="h"><b>Sprint ${i}${i === cur ? " · current" : ""}</b><span class="muted">${sprintRange(i)}</span><span class="${t > cap * 1.15 ? "over-cap" : "muted"}">${t} / ${cap} pts</span></div><div class="pbar" style="margin-bottom:8px">${bar(Math.min(t, cap), cap, t > cap * 1.15 ? "var(--bad)" : "var(--accent)")}<span>${pct(t, cap)}%</span></div><div class="list">${ss.map(storyRow).join("")}</div></div>`;
    }
    return `${chart}<div class="filters" style="margin-top:16px"><div class="tabs" style="margin:0;border:0"><button class="tab ${state.sp !== "all" ? "active" : ""}" data-act="sprint-scope" data-v="next" type="button">Next 6 sprints</button><button class="tab ${state.sp === "all" ? "active" : ""}" data-act="sprint-scope" data-v="all" type="button">All sprints</button></div></div>
      <p class="muted" style="margin-bottom:12px">Stories are spread over each epic's dates in flow order, so the sprint list is a forecast — it moves as you change statuses.</p>${list}`;
  };

  const RISK_ST = ["open", "mitigating", "closed"];
  views.risks = () => {
    const tab = state.rt;
    const tabs = `<div class="tabs">${[["questions", `Questions (${openQs().length} open)`], ["risks", `Risks (${XD.risks.filter((r) => (state.risk[r.id] || "open") !== "closed").length})`], ["decisions", "Decisions (ADRs)"]].map(([id, l]) => `<button class="tab ${tab === id ? "active" : ""}" data-act="risk-tab" data-v="${id}" type="button">${l}</button>`).join("")}</div>`;
    if (tab === "questions") {
      const badge = (q) => ({ answered: '<span class="chip st-done">Answered</span>', deferred: `<span class="chip st-ready">Decided in ${esc(q.by)}</span>`, open: '<span class="chip st-doing">Open</span>' })[qState(q)];
      return tabs + `<p class="muted" style="margin-bottom:12px">Recorded answers come from <code>docs/design/01-overview.md</code> §5. Anything you type below is saved in this browser only — tell Claude to write it into the docs.</p>` + XD.questions.map((q) => `<div class="qcard ${qState(q) === "answered" ? "ans" : ""}"><div class="qh"><span class="chip">${q.id}</span><b style="flex:1;font-weight:500">${esc(q.t)}</b>${badge(q)}</div>
        ${q.a ? `<div class="check ${qState(q) === "answered" ? "ok" : ""}" style="padding:0"><span class="mark">${qState(q) === "answered" ? "✓" : ""}</span><span><b>${qState(q) === "open" ? "Status" : "Decision"}</b>${q.d ? ` <span class="dim">· ${esc(q.d)}</span>` : ""}<br>${esc(q.a)}</span></div>` : ""}
        <div class="chips"><span class="dim" style="font-size:12px">Blocks:</span>${q.blocks.map((id) => `<button class="chip st-${M.byId[id].st}" style="cursor:pointer;border:0" data-act="open-story" data-id="${id}" type="button">${id}</button>`).join("")}</div>
        <textarea class="field" data-q="${q.id}" placeholder="${qState(q) === "open" ? "Your answer…" : "Change of mind? Note it here…"}" aria-label="Notes on ${q.id}">${esc(state.q[q.id] || "")}</textarea>
        <div><button class="btn sm" data-act="toggle-q" data-id="${q.id}" type="button">${qState(q) === "answered" ? "Reopen" : "Mark answered"}</button></div></div>`).join("");
    }
    if (tab === "risks") {
      const cell = (l, i) => { const rs = XD.risks.filter((r) => r.l === l && r.i === i && (state.risk[r.id] || "open") !== "closed"); const sc = l * i; return `<div class="${sc >= 15 ? "c4" : sc >= 10 ? "c3" : sc >= 5 ? "c2" : "c1"}">${rs.map((r) => `<span class="rid" data-act="focus-risk" data-id="${r.id}" title="${esc(r.t)}">${r.id}</span>`).join("")}</div>`; };
      const heat = `<div class="heat"><div class="ax"></div>${[1, 2, 3, 4, 5].map((i) => `<div class="ax">I${i}</div>`).join("")}${[5, 4, 3, 2, 1].map((l) => `<div class="ax">L${l}</div>` + [1, 2, 3, 4, 5].map((i) => cell(l, i)).join("")).join("")}</div>`;
      return tabs + `<div class="grid g-main"><div class="tbl-wrap"><table><thead><tr><th>ID</th><th>Risk</th><th>Epic</th><th class="right">L×I</th><th>Mitigation</th><th>Status</th></tr></thead><tbody>${[...XD.risks].sort((a, b) => b.l * b.i - a.l * a.i).map((r) => `<tr id="risk-${r.id}"><td class="mono">${r.id}</td><td>${esc(r.t)}</td><td>${epicChip(M.byEpic[r.epic])}</td><td class="right"><b>${r.l * r.i}</b></td><td class="muted">${esc(r.m)}</td><td><select class="field" data-change="risk" data-id="${r.id}" aria-label="Status of ${r.id}">${RISK_ST.map((s) => `<option ${(state.risk[r.id] || "open") === s ? "selected" : ""}>${s}</option>`).join("")}</select></td></tr>`).join("")}</tbody></table></div>
        <div class="card"><h2>Heat map <span class="muted" style="font-weight:400;font-size:12px">likelihood × impact</span></h2>${heat}</div></div>`;
    }
    return tabs + `<div class="tbl-wrap"><table><thead><tr><th>Decision</th><th>Title</th><th>Status</th><th>Needed before</th></tr></thead><tbody>${XD.decisions.map((d) => `<tr><td class="mono nowrap">${d.id}</td><td>${esc(d.t)}</td><td><span class="chip ${d.st === "Accepted" ? "st-done" : "st-ready"}">${d.st}</span></td><td>${d.needed ? `<button class="chip st-${M.byId[d.needed].st}" style="cursor:pointer;border:0" data-act="open-story" data-id="${d.needed}" type="button">${d.needed}</button>` : '<span class="dim">—</span>'}</td></tr>`).join("")}</tbody></table></div>
      <p class="muted" style="margin-top:10px">Planned decisions become ADRs in <code>docs/adr/</code> when the story that needs them starts.</p>`;
  };

  views.process = () => `
    <div class="card" style="margin-bottom:16px"><h2>The loop</h2><div class="steps-flow"><span class="s">1 · Spec</span><span class="a">→</span><span class="s">2 · Plan</span><span class="a">→</span><span class="s">3 · Agent builds a slice</span><span class="a">→</span><span class="s">4 · scripts/check.sh</span><span class="a">→</span><span class="s">5 · Independent review</span><span class="a">→</span><span class="s">6 · You approve &amp; commit</span></div>
      <p class="muted" style="margin-top:12px;max-width:80ch">Every story here becomes one spec in <code>docs/specs/</code>. Open a story and use <b>Copy spec</b> to start from a pre-filled file, then <b>Copy agent prompt</b> once the spec is Approved.</p></div>
    <div class="grid g2">
      <div class="card"><h2>Who does what</h2><div class="prose"><ul>
        <li><b>You</b> — answer questions, approve specs and plans, review results, decide go/no-go at each exit gate, commit.</li>
        <li><b>backend-dev / frontend-dev</b> — implement a slice from an Approved spec following the CLAUDE.md rules.</li>
        <li><b>test-writer</b> — writes tests from acceptance criteria; never edits production code.</li>
        <li><b>reviewer</b> — read-only, independent review of the diff: spec, layering, SOLID, performance, security.</li>
        <li><b>Skills</b> — <code>/new-feature</code>, <code>/add-endpoint</code>, <code>/new-migration</code>, <code>/phase-done</code>.</li></ul></div></div>
      <div class="card"><h2>Cadence (≈ ${XD.meta.capacity} h/week)</h2><div class="prose"><ul>
        <li><b>Monday, 30 min</b> — pick this week's stories on the Board; approve or write specs; answer open questions.</li>
        <li><b>During the week</b> — agents build; you review diffs and run the app (<code>deploy/xd-up.sh</code>).</li>
        <li><b>Friday, 30 min</b> — update statuses here, look at Sprints and Risks, run <code>/phase-done</code> when an epic ends.</li>
        <li><b>At each exit gate</b> — re-plan the next epic from what you learned; update design docs and add ADRs.</li></ul></div></div>
      <div class="card"><h2>Definition of ready</h2><div class="prose"><ul><li>Spec exists and is <b>Approved</b>; open questions answered.</li><li>Acceptance criteria are testable; design refs listed.</li><li>Dependencies are done or can be stubbed behind a port.</li><li>Slice fits in 1–3 days of agent work (≈ 2–8 pts).</li></ul></div></div>
      <div class="card"><h2>Definition of done</h2><div class="prose"><ul><li><code>scripts/check.sh</code> green (build with warnings as errors, all tests, lint, format).</li><li>Tests cover every acceptance criterion; bug fixes start with a failing test.</li><li>API change → <code>scripts/gen-api.sh</code>; DB change → migration; design change → docs + ADR.</li><li>Reviewer agent: no blockers or majors. Stack still starts and System status is green.</li></ul></div></div>
      <div class="card"><h2>Everyday commands</h2><div class="prose"><ul><li><code>deploy/xd-up.sh</code> / <code>xd-down.sh</code> — start / stop the stack</li><li><code>scripts/check.sh</code> — all quality gates</li><li><code>scripts/gen-api.sh</code> — regenerate the API contract and frontend types</li><li><code>scripts/serve.sh</code> — this page (8766) and the docs site (8765)</li></ul></div></div>
      <div class="card"><h2>Where things live</h2><div class="prose"><ul><li><a href="${docsBase()}" target="_blank" rel="noopener">Design docs</a> — source of truth (<code>docs/design</code>)</li><li><code>docs/specs/</code> — slice specs · <code>docs/adr/</code> — decisions</li><li><code>docs/engineering/README.md</code> — standards and agentic workflow</li><li><a href="../index.html">Clickable prototype</a> — the UX each screen should match</li></ul></div></div>
    </div>`;

  /* ---------- drawer ---------- */
  function storyDrawer(s) {
    const e = epicOf(s), w = waiting(s);
    const checked = new Set(state.ac[s.id] || []);
    return `<div class="d-head"><div class="top-row"><span class="chips">${epicChip(e, true)} <span class="mono dim">${s.id}</span>${s.custom ? '<span class="chip">custom</span>' : ""}</span><button class="btn sm" data-act="close" type="button" aria-label="Close">✕</button></div><h2>${esc(s.t)}</h2></div>
    <div class="d-body">
      ${w && w.length ? `<div class="chip st-blocked" style="white-space:normal;border-radius:8px;padding:6px 10px">Waiting for ${w.map((d) => `<a href="#/${currentView()}/${d}">${d}</a>`).join(", ")} to be done first.</div>` : ""}
      <dl class="kv"><dt>Status</dt><dd><select class="field" data-change="status" data-id="${s.id}" aria-label="Status">${XD.statuses.map((x) => `<option value="${x.id}" ${x.id === s.st ? "selected" : ""}>${x.name}</option>`).join("")}</select></dd>
        <dt>Priority</dt><dd>${priChip(s.pri)}</dd><dt>Effort</dt><dd>${s.pts} pts <span class="dim">(≈ ${s.pts} focused hours incl. review)</span></dd>
        <dt>Type · area</dt><dd>${esc(s.type)} · ${esc(s.area)}</dd><dt>Planned</dt><dd>${s.st === "done" ? `Done ${fmt(s.date, { year: "numeric" })}` : `Sprint ${s.sprint} · by ${fmt(s.date, { year: "numeric" })}`}</dd>
        <dt>Design refs</dt><dd>${docLinks(s.doc) || "—"}</dd><dt>Spec file</dt><dd class="mono">docs/specs/${specName(s)}.md</dd></dl>
      <div><h4>Acceptance criteria</h4><div class="ac">${s.ac.map((a, i) => `<label><input type="checkbox" data-ac="${s.id}" data-i="${i}" ${checked.has(i) ? "checked" : ""}><span>${esc(a)}</span></label>`).join("")}</div></div>
      ${s.dep.length ? `<div><h4>Depends on</h4><div class="chips">${s.dep.map((d) => depChip(d)).join("")}</div></div>` : ""}
      ${s.unlocks.length ? `<div><h4>Unlocks</h4><div class="chips">${s.unlocks.map((d) => depChip(d)).join("")}</div></div>` : ""}
      <div><h4>Notes</h4><textarea class="field" data-note="${s.id}" placeholder="Decisions, links, findings…" aria-label="Notes">${esc(s.note || "")}</textarea></div>
      <div><h4>Work with the agents</h4><div class="chips"><button class="btn sm" data-act="copy-spec" data-id="${s.id}" type="button">Copy spec</button><button class="btn sm" data-act="copy-prompt" data-id="${s.id}" type="button">Copy agent prompt</button>${s.custom ? `<button class="btn sm" data-act="delete-story" data-id="${s.id}" type="button">Delete story</button>` : ""}</div></div>
    </div>`;
  }
  const depChip = (id) => { const d = M.byId[id]; return d ? `<a class="chip st-${d.st}" href="#/${currentView()}/${id}" title="${esc(d.t)}"><span class="dot ${d.st}"></span>${id} · ${esc(d.t.slice(0, 34))}${d.t.length > 34 ? "…" : ""}</a>` : ""; };
  const specName = (s) => `${s.id}-${slug(s.t)}`;

  function epicDrawer(e) {
    return `<div class="d-head"><div class="top-row"><span class="chips">${epicChip(e, true)}<span class="chip">${esc(wave(e.wave).name)}</span></span><button class="btn sm" data-act="close" type="button" aria-label="Close">✕</button></div><h2>${esc(e.title)}</h2></div>
    <div class="d-body"><p>${esc(e.goal)}</p>
      <dl class="kv"><dt>Dates</dt><dd>${fmt(e.start, { year: "numeric" })} → ${fmt(e.end, { year: "numeric" })} (${e.weeks} weeks)</dd><dt>Progress</dt><dd><div class="pbar">${bar(e.donePts, e.pts, e.color)}<span>${pct(e.donePts, e.pts)}%</span></div><span class="dim">${e.doneCount}/${e.count} stories · ${e.donePts}/${e.pts} pts</span></dd>
        <dt>Load</dt><dd>${CAL_BOUND.includes(e.id) ? '<span class="dim">Calendar-bound: duration is waiting/review time, not build effort.</span>' : loadText(e)}</dd>
        <dt>Needs</dt><dd>${e.needs.length ? e.needs.map((n) => epicChip(M.byEpic[n], true)).join(" ") : "—"}</dd><dt>Unlocks</dt><dd>${e.unlocks.length ? e.unlocks.map((n) => epicChip(M.byEpic[n], true)).join(" ") : "—"}</dd><dt>Design docs</dt><dd>${docLinks(e.refs)}</dd></dl>
      <div class="gate" style="margin:0"><b style="color:${e.color}">◆ Exit gate</b><span>${esc(e.exit)}</span></div>
      <div><h4>Stories</h4><div class="list">${e.stories.map(storyRow).join("")}</div></div></div>`;
  }
  function loadText(e) {
    const cap = e.weeks * XD.meta.capacity, r = e.remPts / cap;
    return `${e.remPts} pts remaining vs ${cap} pts capacity — <b style="color:${r > 1.15 ? "var(--bad)" : r > 0.9 ? "var(--warn)" : "var(--good)"}">${Math.round(r * 100)}%</b>${r > 1.15 ? ' <span class="dim">(over capacity: cut scope or extend)</span>' : ""}`;
  }

  /* ---------- router & render ---------- */
  const currentView = () => (location.hash.replace(/^#\/?/, "").split("/")[0] || "overview");
  const currentArg = () => decodeURIComponent(location.hash.replace(/^#\/?/, "").split("/")[1] || "");

  function render() {
    build();
    let v = currentView();
    if (!views[v]) v = "overview";
    $("#page-title").textContent = TITLES[v];
    document.title = `${TITLES[v]} — xDrishti delivery plan`;
    $("#nav").innerHTML = NAV.map(([id, ico, name]) => `<a href="#/${id}" class="${id === v ? "active" : ""}" ${id === v ? 'aria-current="page"' : ""}><span class="ico" aria-hidden="true">${ico}</span>${name}${id === "backlog" ? `<span class="count">${M.stories.length}</span>` : ""}${id === "risks" ? `<span class="count">${openQs().length}</span>` : ""}</a>`).join("");
    const scroll = $("#view").dataset.v === v ? window.scrollY : 0;
    $("#view").innerHTML = views[v]();
    $("#view").dataset.v = v;
    if (scroll) window.scrollTo(0, scroll);
    renderDrawer();
  }
  function renderDrawer() {
    const arg = currentArg(), dr = $("#drawer");
    const s = M.byId[arg], e = M.byEpic[arg];
    if (s || e) { dr.innerHTML = s ? storyDrawer(s) : epicDrawer(e); dr.classList.add("open"); dr.setAttribute("aria-hidden", "false"); $("#scrim").hidden = false; } else { dr.classList.remove("open"); dr.setAttribute("aria-hidden", "true"); $("#scrim").hidden = true; }
  }
  const go = (view, arg) => { location.hash = `#/${view}${arg ? "/" + encodeURIComponent(arg) : ""}`; };
  const closeDrawer = () => go(currentView());

  /* ---------- actions ---------- */
  function toast(msg) { const t = $("#toast"); t.textContent = msg; t.classList.add("show"); clearTimeout(toast.t); toast.t = setTimeout(() => t.classList.remove("show"), 1800); }
  async function copy(text, msg) { try { await navigator.clipboard.writeText(text); toast(msg); } catch (e) { const ta = document.createElement("textarea"); ta.value = text; document.body.append(ta); ta.select(); document.execCommand("copy"); ta.remove(); toast(msg); } }
  function download(name, text, mime) { const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob([text], { type: mime })); a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000); }
  function setStatus(id, st) { const base = XD.epics.flatMap((e) => e.stories).find((s) => s.id === id)?.st; if (st === base) delete state.st[id]; else state.st[id] = st; if (st === "done") state.done[id] = iso(today); else delete state.done[id]; save(); render(); }

  function specText(s) {
    const e = epicOf(s);
    return `# ${s.id} — ${s.t}

- **Status:** Draft
- **Phase / roadmap row:** ${e.id} — ${e.title}
- **Design refs:** ${s.doc || "—"}

## Goal
${e.goal} This slice: ${s.t}.

## Scope
**In:** ${s.t}.
**Out (not in this slice):** _list neighbouring stories: ${[...s.dep, ...s.unlocks].join(", ") || "none"}_

## Acceptance criteria
${s.ac.map((a, i) => `${i + 1}. ${a}`).join("\n")}

## Contract
- **API:** _TBD_
- **Data:** _TBD_
- **UI:** _TBD — match docs/prototype_
- **Events/jobs:** _TBD_

## Non-functional
_Performance budget, security, observability._

## Test plan
_Unit (domain/application), integration (API + DB), frontend (MSW), architecture impact; edge cases._

## Open questions
${XD.questions.filter((q) => q.blocks.includes(s.id)).map((q) => `- ${q.id}: ${q.t}`).join("\n") || "_None_"}

## Done when
- [ ] All acceptance criteria have evidence
- [ ] \`scripts/check.sh\` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (\`scripts/gen-api.sh\`); migration added
- [ ] Design docs/ADR updated if behaviour changed
`;
  }
  const mdBacklog = () => `# xDrishti backlog\n\n_Exported ${iso(today)}_\n\n` + M.epics.map((e) => `## ${e.id} — ${e.title}\n\n*${fmt(e.start, { year: "numeric" })} → ${fmt(e.end, { year: "numeric" })} · Exit gate: ${e.exit}*\n\n` + e.stories.map((s) => `- [${s.st === "done" ? "x" : " "}] **${s.id}** ${s.t} _(${s.pri}, ${s.pts} pts, ${stName(s.st)})_`).join("\n")).join("\n\n") + "\n";
  const csvBacklog = () => { const q = (v) => `"${String(v ?? "").replace(/"/g, '""')}"`; return ["id,title,epic,type,area,priority,points,status,sprint,depends_on,design_refs", ...M.stories.map((s) => [s.id, s.t, s.epic, s.type, s.area, s.pri, s.pts, s.st, s.sprint || "", s.dep.join(" "), s.doc].map(q).join(","))].join("\n"); };

  function newStoryDialog() {
    const m = $("#modal");
    m.innerHTML = `<form class="form" method="dialog" id="new-story"><h2>New story</h2>
      <label>Title<input class="field" name="t" required maxlength="140" placeholder="As the owner I can …"></label>
      <div class="two"><label>Epic<select class="field" name="epic" style="width:100%">${epicOpts().map((o) => `<option value="${o.v}">${esc(o.l)}</option>`).join("")}</select></label>
      <label>Status<select class="field" name="st" style="width:100%">${XD.statuses.map((s) => `<option value="${s.id}">${s.name}</option>`).join("")}</select></label></div>
      <div class="two"><label>Type<select class="field" name="type" style="width:100%">${TYPES.map((t) => `<option>${t}</option>`).join("")}</select></label><label>Area<select class="field" name="area" style="width:100%">${AREAS.map((t) => `<option>${t}</option>`).join("")}</select></label></div>
      <div class="two"><label>Priority<select class="field" name="pri" style="width:100%">${PRIS.map((t) => `<option>${t}</option>`).join("")}</select></label><label>Effort (pts)<select class="field" name="pts" style="width:100%">${[1, 2, 3, 5, 8, 13].map((t) => `<option ${t === 3 ? "selected" : ""}>${t}</option>`).join("")}</select></label></div>
      <label>Acceptance criteria (one per line)<textarea class="field" name="ac" rows="4"></textarea></label>
      <div class="acts"><button class="btn" value="cancel" type="button" data-act="close-modal">Cancel</button><button class="btn primary" type="submit">Add story</button></div></form>`;
    m.showModal();
    $("#new-story").addEventListener("submit", (ev) => {
      ev.preventDefault();
      const f = new FormData(ev.target);
      const n = state.custom.length + 1;
      state.custom.push({ id: `U-${String(n).padStart(3, "0")}`, t: f.get("t").trim(), epic: f.get("epic"), st: f.get("st"), type: f.get("type"), area: f.get("area"), pri: f.get("pri"), pts: +f.get("pts"), doc: "", dep: [], ac: f.get("ac").split("\n").map((x) => x.trim()).filter(Boolean), custom: true });
      if (!state.custom.at(-1).ac.length) state.custom.at(-1).ac = ["Acceptance criteria to be defined"];
      save(); m.close(); toast("Story added"); render();
    });
  }

  const actions = {
    "open-story": (el) => go(currentView(), el.dataset.id),
    "open-epic": (el) => go(currentView(), el.dataset.id),
    close: closeDrawer,
    "close-modal": () => $("#modal").close(),
    "new-story": newStoryDialog,
    theme: () => { const t = document.documentElement.dataset.theme === "dark" ? "light" : "dark"; document.documentElement.dataset.theme = t; try { localStorage.setItem("xd-proto-theme", t); } catch (e) { /* ignore */ } },
    "copy-spec": (el) => copy(specText(M.byId[el.dataset.id]), `Spec copied — save as docs/specs/${specName(M.byId[el.dataset.id])}.md`),
    "copy-prompt": (el) => { const s = M.byId[el.dataset.id]; copy(`Implement docs/specs/${specName(s)}.md (status: Approved) with /new-feature. Follow the CLAUDE.md rules, add tests for every acceptance criterion, run scripts/check.sh and the reviewer agent, then report. Do not commit.`, "Agent prompt copied"); },
    "delete-story": (el) => { if (confirm("Delete this custom story?")) { state.custom = state.custom.filter((c) => c.id !== el.dataset.id); save(); closeDrawer(); render(); } },
    "clear-filters": () => { state.f = { ...defaults().f }; save(); render(); },
    "export-md": () => { download("xdrishti-backlog.md", mdBacklog(), "text/markdown"); $("#export-menu").open = false; },
    "export-csv": () => { download("xdrishti-backlog.csv", csvBacklog(), "text/csv"); $("#export-menu").open = false; },
    "export-json": () => { download("xdrishti-plan-changes.json", JSON.stringify(state, null, 2), "application/json"); $("#export-menu").open = false; },
    reset: () => { $("#export-menu").open = false; if (confirm("Reset all your status changes, notes, answers and custom stories in this browser?")) { state = defaults(); save(); render(); toast("Reset"); } },
    "sprint-scope": (el) => { state.sp = el.dataset.v; save(); render(); },
    "risk-tab": (el) => { state.rt = el.dataset.v; save(); render(); },
    "toggle-q": (el) => { const q = XD.questions.find((x) => x.id === el.dataset.id); state.qd[q.id] = qState(q) !== "answered"; save(); render(); },
    "focus-risk": (el) => { const r = $(`#risk-${el.dataset.id}`); r?.scrollIntoView({ block: "center", behavior: "smooth" }); r?.animate([{ background: "var(--accent-soft)" }, { background: "transparent" }], 1400); },
    "flow-all": (el) => { M.epics.forEach((e) => (state.closed[e.id] = el.dataset.v === "0")); save(); $$("details.jr").forEach((d) => (d.open = el.dataset.v === "1")); },
  };

  document.addEventListener("click", (ev) => {
    const el = ev.target.closest("[data-act]");
    if (!el || ev.target.closest("select,input,textarea,a")) return;
    if (ev.target.closest("[data-card]") && el.dataset.act === "open-story" && ev.target.closest("select")) return;
    actions[el.dataset.act]?.(el, ev);
  });
  document.addEventListener("change", (ev) => {
    const t = ev.target;
    if (t.dataset.change === "status") setStatus(t.dataset.id, t.value);
    else if (t.dataset.change === "risk") { if (t.value === "open") delete state.risk[t.dataset.id]; else state.risk[t.dataset.id] = t.value; save(); render(); }
    else if (t.dataset.filter) { state.f[t.dataset.filter] = t.type === "checkbox" ? t.checked : t.value; save(); render(); }
    else if (t.dataset.board === "scope") { state.b.scope = t.value; save(); render(); }
    else if (t.dataset.ac) { const set = new Set(state.ac[t.dataset.ac] || []); t.checked ? set.add(+t.dataset.i) : set.delete(+t.dataset.i); state.ac[t.dataset.ac] = [...set]; save(); }
  });
  document.addEventListener("input", (ev) => {
    const t = ev.target;
    if (t.dataset.note) { state.note[t.dataset.note] = t.value; save(); }
    else if (t.dataset.q) { state.q[t.dataset.q] = t.value; save(); }
    else if (t.dataset.board === "q") { state.b.q = t.value; save(); const pos = t.selectionStart; render(); const n = $('[data-board="q"]'); n.focus(); n.setSelectionRange(pos, pos); }
    else if (t.id === "q") { state.f.q = t.value; save(); if (currentView() !== "backlog") go("backlog"); else { const pos = t.selectionStart; render(); $("#q").focus(); $("#q").setSelectionRange(pos, pos); } }
  });
  document.addEventListener("toggle", (ev) => { const d = ev.target; if (d.matches?.("details.jr")) { state.closed[d.dataset.epic] = !d.open; save(); } }, true);

  /* board drag & drop */
  let dragId = null;
  document.addEventListener("dragstart", (ev) => { const c = ev.target.closest?.("[data-card]"); if (!c) return; dragId = c.dataset.card; c.classList.add("drag"); ev.dataTransfer.effectAllowed = "move"; ev.dataTransfer.setData("text/plain", dragId); });
  document.addEventListener("dragend", () => { dragId = null; $$(".kc.drag").forEach((c) => c.classList.remove("drag")); $$(".col.over").forEach((c) => c.classList.remove("over")); });
  document.addEventListener("dragover", (ev) => { const col = ev.target.closest?.("[data-col]"); if (!col || !dragId) return; ev.preventDefault(); $$(".col.over").forEach((c) => c !== col && c.classList.remove("over")); col.classList.add("over"); });
  document.addEventListener("drop", (ev) => { const col = ev.target.closest?.("[data-col]"); if (!col || !dragId) return; ev.preventDefault(); const id = dragId; dragId = null; setStatus(id, col.dataset.col); toast(`${id} → ${stName(col.dataset.col)}`); });

  /* flow: highlight dependencies on hover */
  document.addEventListener("mouseover", (ev) => {
    const sc = ev.target.closest?.(".sc"), nd = ev.target.closest?.("[data-node]");
    $$(".sc.hl,.sc.dep,.edge.hl").forEach((n) => n.classList.remove("hl", "dep"));
    if (sc) { sc.classList.add("hl"); (sc.dataset.deps || "").split(" ").filter(Boolean).forEach((d) => $$(`.sc[data-act="open-story"][data-id="${d}"]`).forEach((n) => n.classList.add("dep"))); }
    if (nd) $$(`.edge[data-from="${nd.dataset.node}"],.edge[data-to="${nd.dataset.node}"]`).forEach((p) => p.classList.add("hl"));
  });

  document.addEventListener("keydown", (ev) => {
    if (ev.key === "Escape") { if ($("#modal").open) return; if ($("#drawer").classList.contains("open")) closeDrawer(); }
    if (ev.key === "/" && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) { ev.preventDefault(); $("#q").focus(); }
  });
  $("#scrim").addEventListener("click", closeDrawer);
  window.addEventListener("hashchange", render);
  $("#lnk-docs").href = docsBase();
  $("#lnk-app").href = inStack() ? "/" : `${location.protocol}//${location.hostname || "localhost"}:8080/`;
  $("#q").value = state.f.q;
  if (!location.hash) location.hash = "#/overview";
  render();
})();
