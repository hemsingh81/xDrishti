/* xDrishti prototype — spec-driven shared kit (honest states, evidence, owner acts, spec tags) + Assistant page.
   Specs: P9-04 (chat UI), P9-00 rules. All demo data; nothing here places an order. */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const { $, $$, esc, num, inr, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const I = BT.icon;
  const K = (BT.kit = {});

  /* ---------------- persistent prototype state (inside the demo state, so Reset clears it) ---------------- */
  K.P = () => { const S = BT.S; S.proto = S.proto || {}; S.proto.sw = S.proto.sw || {}; return S.proto; };
  K.on = (id) => !!K.P().sw[id];

  /* ---------------- tags, banners, headers ---------------- */
  K.spec = (id) => `<span class="spec-tag" tabindex="0" title="${esc(id)} is specified in docs/specs (Draft). The owner must set it Approved before it is built; this screen is the UX reference, not a built feature.">${I('fileText')} Spec ${esc(id)} (Draft)</span>`;
  K.specs = (ids) => ids.map(K.spec).join(' ');
  K.head = (title, sub, specs, actions = '') => `<div class="page-head"><div><h1>${title}</h1><p>${sub}</p><div class="spec-row">${K.specs(specs)}</div></div>${actions ? `<div class="row">${actions}</div>` : ''}</div>`;
  K.phase = (text) => `<div class="phase-banner" role="note">${I('alert')}<div>${text}</div></div>`;
  K.safety = (t = 'SAFETY · default OFF') => `<span class="safety">${I('lock')} ${t}</span>`;

  /* ---------------- honest states: icon + word + reason; never a number, never green ---------------- */
  const ST = { insufficient: ['minus', 'Insufficient'], unavailable: ['ban', 'Unavailable'], not_evaluable: ['help', 'Not evaluable'], empty: ['database', 'Empty'], provisional: ['clock', 'Provisional'], unknown: ['help', 'Unknown'] };
  K.st = (kind, reason, block) => `<span class="st${block ? ' block' : ''}" data-state="${kind}">${I(ST[kind][0])}<b>${ST[kind][1]}</b>${reason ? `<span>${esc(reason)}</span>` : ''}</span>`;
  // metric = { state: 'value', v, n } or { state: 'insufficient'|..., reason, n }
  K.metric = (m, fmt = (v) => num(v, 2)) => (!m || m.state === 'value') ? `<span class="num">${m ? fmt(m.v) : '—'}</span>${m && m.n != null ? ` <span class="small muted">n ${m.n}</span>` : ''}` : K.st(m.state, m.reason);
  K.R = (x, d = 2) => (x > 0 ? '+' : x < 0 ? '−' : '') + Math.abs(x).toFixed(d) + 'R';

  K.evidence = ({ basis, population, range, n, limits = [], more = [] }) => `<div class="evidence" role="note"><div class="ev-h">${I('clipboard')} Evidence and limits</div><dl>
    <dt>Basis</dt><dd>${basis}</dd><dt>Population</dt><dd>${population}</dd>${range ? `<dt>Date range</dt><dd>${range}</dd>` : ''}${n != null ? `<dt>n</dt><dd>${n}</dd>` : ''}${more.map(([a, b]) => `<dt>${a}</dt><dd>${b}</dd>`).join('')}</dl>
    ${limits.length ? `<ul>${limits.map((l) => `<li>${l}</li>`).join('')}</ul>` : ''}</div>`;

  /* ---------------- tabs (WAI-ARIA tablist, arrow keys) ---------------- */
  K.tabs = (tabs, cur, label) => `<div class="card" data-nocollapse="1" style="margin-bottom:16px"><div class="tabs" role="tablist" aria-label="${esc(label)}">${tabs.map(([k, l, x = '']) => `<button role="tab" id="kt-${k}" aria-selected="${cur === k}" tabindex="${cur === k ? 0 : -1}" data-ktab="${k}" class="${cur === k ? 'on' : ''}">${l}${x}</button>`).join('')}</div></div>`;
  K.bindTabs = (root, set) => {
    const btns = $$('[data-ktab]', root);
    btns.forEach((b, i) => {
      b.onclick = () => set(b.dataset.ktab);
      b.onkeydown = (e) => { const d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0; if (d) { e.preventDefault(); const t = btns[(i + d + btns.length) % btns.length]; t.focus(); t.click(); } };
    });
  };
  K.hashTab = (state, tabs) => { const qs = new URLSearchParams(location.hash.split('?')[1] || ''); const t = qs.get('tab'); if (t && state.seen !== location.hash) { state.seen = location.hash; if (tabs.some((x) => x[0] === t)) state.tab = t; } };

  /* ---------------- owner act dialog: reason + password step-up; every raising change asks for it ---------------- */
  K.ownerAct = ({ title, lead, fields = '', okLabel = 'Confirm', reason = true, password = true, danger = false, collect, validate }) => new Promise((resolve) => {
    let finished = false;
    BT.modal({
      title,
      body: `<div class="row" style="flex-wrap:nowrap;align-items:flex-start">${BT.navIcon('accounts')}<div style="min-width:0"><p style="margin:0">${lead}</p></div></div>${fields}
        ${reason ? '<label class="field">Reason (1 to 500 characters, required)<textarea class="input" id="oa-reason" maxlength="500" rows="2" style="font-family:var(--font)"></textarea></label>' : ''}
        ${password ? '<label class="field">Password (checked again on every change)<input class="input" type="password" id="oa-pw" autocomplete="current-password" value="demo-password"></label>' : ''}
        <div class="err" id="oa-err" role="alert"></div>
        <div class="small muted">${password ? 'Prototype: the demo password is prefilled. The real app audits names and numbers, never the password, and rate-limits attempts like sign-in (5 wrong = 15-minute lock).' : 'Lowering risk needs no password, only a confirmation.'}</div>`,
      foot: `<button class="btn" data-close>Cancel</button><button class="btn ${danger ? 'bad solid' : 'primary'}" id="oa-ok">${esc(okLabel)}</button>`,
      onMount: (ov, close) => {
        const ok = () => {
          const r = reason ? $('#oa-reason', ov).value.trim() : '';
          const err = (m) => { $('#oa-err', ov).textContent = m; };
          if (reason && (r.length < 1 || r.length > 500)) return err('Enter a reason of 1 to 500 characters.');
          if (validate) { const v = validate(ov); if (v) return err(v); }
          if (password && D.hash($('#oa-pw', ov).value) !== BT.S.auth.pw) { BT.audit('owner', 'Re-authentication failed', title); BT.save(); return err('Wrong password.'); }
          const extra = collect ? collect(ov) : {};
          finished = true; close();
          BT.audit('owner', title, 'Owner act confirmed with password' + (reason ? ' and a reason' : ''));
          BT.save(); resolve(Object.assign({ reason: r }, extra));
        };
        $('#oa-ok', ov).onclick = ok;
        ov.addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') ok(); });
        ov.addEventListener('click', (e) => { if (!finished && (e.target === ov || e.target.closest('[data-close]'))) resolve(null); });
      },
    });
  });

  // a switch that is OFF by default and needs the owner's act to change (SAFETY rule)
  K.switchRow = ({ id, label, desc, safety = true, effectOn, lowering = false }) => `<div class="owner-row"><div><b>${label}</b> ${safety ? K.safety() : ''}<div class="desc">${desc}</div></div>
    <label class="switch" title="${K.on(id) ? 'ON' : 'OFF'}: changing it needs your password"><input type="checkbox" role="switch" data-osw="${id}" aria-label="${esc(label)}" ${K.on(id) ? 'checked' : ''}><span></span><i style="font-style:normal;min-width:26px">${K.on(id) ? 'ON' : 'OFF'}</i></label></div>`;
  K.bindSwitches = (root, meta, after) => $$('[data-osw]', root).forEach((c) => {
    c.onchange = async () => {
      const id = c.dataset.osw, want = c.checked, m = meta[id] || {};
      c.checked = !want; // stays as it was until the act succeeds
      const down = !want; // switching OFF lowers risk
      const r = await K.ownerAct({ title: (want ? 'Turn on: ' : 'Turn off: ') + (m.label || id), lead: want ? (m.on || 'Turning this on changes what the system may do. It stays on until you turn it off.') : 'Turning this off is the safe direction. It takes effect from the next run.', password: want || m.offNeedsPassword, okLabel: want ? 'Turn on' : 'Turn off' });
      if (!r) return;
      K.P().sw[id] = want; BT.audit('owner', (want ? 'Switched ON: ' : 'Switched OFF: ') + (m.label || id), 'Setting snapshot stored (insert-only)'); BT.save();
      BT.toast((m.label || id) + (want ? ' is ON' : ' is OFF'), want ? '' : 'good'); after ? after() : BT.rerender();
    };
  });

  K.table = (heads, rows, { cls = '', stack = false } = {}) => `<div class="tbl-wrap"><table class="tbl ${cls} ${stack ? 'tbl-stack' : ''}"><thead><tr>${heads.map((h) => `<th${h.startsWith('>') ? ' class="r"' : ''}>${esc(h.replace(/^>/, ''))}</th>`).join('')}</tr></thead><tbody>${rows.map((r) => `<tr>${r.map((c, i) => `<td data-l="${esc(heads[i].replace(/^>/, ''))}"${heads[i].startsWith('>') ? ' class="r num"' : ''}>${c}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
  K.card = (title, body, extra = '', cls = '') => `<div class="card ${cls}"><div class="card-h"><h3>${title}</h3>${extra ? `<div class="row">${extra}</div>` : ''}</div>${body}</div>`;
  K.lastDay = () => D.DAYS[D.DAYS.length - 1];

  /* =====================================================================================
     ASSISTANT  (P9-04)  — read-only chat over the owner's stored data
     ===================================================================================== */
  const A = { sim: 'down', eval: 'not_yet_evaluated', conv: null, convs: null, busy: false, turn: null, timers: [], since: Date.now(), draft: '', histOpen: false };
  const GATES = {
    off: ['power', 'Assistant is switched off', 'The assistant is off by default. Turning it on is an owner act (password).'],
    down: ['server', 'Model server not running', 'The app cannot start it. Run this in a terminal, then this screen switches by itself:'],
    loading: ['clock', 'Loading the model, usually 20 to 105 seconds', 'The model server is warming up. This screen checks every 3 seconds.'],
    misconfigured: ['key', 'The model server rejected the API key', 'Check the secret file written by deploy/xd-up.sh; the key is never shown here.'],
    tools_down: ['server', 'Tool server not running', 'The read-only data tools are not reachable. Run this in a terminal:'],
    busy: ['clock', 'The assistant is answering another question', 'One question at a time. The composer is off until it finishes.'],
    nightly: ['clock', 'The nightly pipeline is running', 'The assistant waits so the model does not slow the plan. It is back when the run ends.'],
    ready: ['checkCircle', 'Ready', 'Ask about your stored data.'],
  };
  const status = () => (K.on('assistant') ? A.sim : 'off');

  // ----- scripted, deterministic answers built from the demo data (so every figure has a tool result behind it) -----
  const taken = () => BT.tradesData().filter((t) => t.taken);
  const MIN_N = 30;
  const metricOf = (list) => { const s = BT.tradeStats(list); return s.n >= MIN_N ? { state: 'value', v: s.exp, n: s.n } : { state: 'insufficient', n: s.n, reason: `n ${s.n} of at least ${MIN_N}` }; };
  const EV = (n, extra = []) => ({ basis: 'gross, on simulated fills; costed on the stored sheet, not confirmed as yours', population: 'stored demo simulation runs (graded trades)', range: `${D.DAYS[D.DAYS.length - 250]} to ${K.lastDay()}`, n, limits: ['Expectancy below the minimum count is not shown.', 'Many looks at the same data: a high-looking cell can be chance.'].concat(extra) });
  function buildTurn(q) {
    const ql = q.toLowerCase();
    const asOf = Date.now();
    if (/portfolio|holding|xirr|balance|funds/.test(ql) && !K.P().sw['assistant.portfolio']) {
      return { calls: [{ tool: 'get_portfolio', args: { scope: 'holdings' }, status: 'error', code: 'assistant.portfolio_off', message: "Portfolio data is off for the assistant.", cols: [], rows: [], asOf }], text: "Portfolio data is switched off for the assistant, so portfolio questions answer 'not available'. Turn on \"Portfolio data\" in the assistant settings (owner act) if you want it.", marks: [], grounding: 'no_numbers' };
    }
    if (/enough|rank|cells?\b/.test(ql)) {
      const g = {}; taken().forEach((t) => { const k = `${t.strategy} · ${t.side} · ${t.tf}`; (g[k] = g[k] || []).push(t); });
      const rows = Object.entries(g).map(([k, l]) => ({ cell: k, n: l.length, exp: metricOf(l) })).sort((a, b) => b.n - a.n);
      const ok = rows.filter((r) => r.exp.state === 'value');
      const text = ok.length
        ? `${ok.length} of ${rows.length} setups have at least ${MIN_N} trades. By trade count: ${ok.slice(0, 3).map((r) => `${r.cell} (${r.n} trades, ${K.R(r.exp.v)})`).join('; ')}.\n\nThe list is ordered by count, not ranked: with this many looks, a high figure can be chance. The other ${rows.length - ok.length} setups are shown as insufficient in the table.`
        : `No setup has at least ${MIN_N} trades yet; the largest has ${rows[0] ? rows[0].n : 0}. Nothing can be ranked.`;
      return { calls: [{ tool: 'get_cells', args: { minTrades: MIN_N, group: 'strategy,side,timeframe' }, status: 'ok', cols: [['cell', 'Setup'], ['n', 'Trades', 'n'], ['exp', 'Expectancy', 'metric']], rows, asOf, evidence: EV(taken().length) }], text, marks: [], grounding: 'grounded' };
    }
    if (/plan|tomorrow|ticket/.test(ql)) {
      const items = BT.S.plan.items.filter((i) => !i.reserve);
      const rows = items.map((i) => ({ sym: `${i.symbol} ${i.side}`, setup: `${i.strategy} ${i.tf}`, p: { state: 'value', v: i.pWin, n: null }, st: i.status }));
      const text = items.length
        ? `The plan for ${fmtDate(new Date(BT.S.plan.date))} has ${items.length} primary tickets: ${items.slice(0, 4).map((i) => `${i.symbol} ${i.side} (${i.strategy} ${i.tf}, P(win) ${Math.round(i.pWin * 100)}%)`).join('; ')}${items.length > 4 ? ' and more' : ''}.\n\nArmed means watched, not ordered. No order exists in this system.`
        : 'There are no primary tickets in tomorrow\'s plan. The market gate or the data checks produced none; see Plan review for the reason.';
      return { calls: [{ tool: 'get_plan', args: { date: BT.S.plan.date }, status: 'ok', cols: [['sym', 'Ticket'], ['setup', 'Setup'], ['p', 'P(win)', 'pct'], ['st', 'State']], rows, asOf, evidence: { basis: 'stored plan', population: `plan for ${BT.S.plan.date}`, n: items.length, limits: ['P(win) is a calibrated estimate from stored cells, not a promise.'] } }], text, marks: [], grounding: items.length ? 'grounded' : 'no_numbers' };
    }
    if (/expectancy|sell|15m|basket/.test(ql)) {
      const names = [...new Set(taken().map((t) => t.basket))];
      const rows = names.map((b) => ({ basket: b, n: taken().filter((t) => t.basket === b && t.side === 'SELL' && t.tf === '15m').length, exp: metricOf(taken().filter((t) => t.basket === b && t.side === 'SELL' && t.tf === '15m')) }));
      const ok = rows.filter((r) => r.exp.state === 'value');
      let text = ok.length
        ? `15m SELL expectancy by basket, where there are at least ${MIN_N} trades: ${ok.map((r) => `${r.basket} ${K.R(r.exp.v)} (n ${r.n})`).join('; ')}. The other baskets have too few trades to state a figure.`
        : `No basket has enough 15m SELL trades to state an expectancy: the largest group has ${Math.max(...rows.map((r) => r.n))} trades and at least ${MIN_N} are needed. I will not give a number for them.`;
      const extra = ' Setups like this usually win about 45% of the time.';
      const marks = [[text.length + extra.indexOf('45%'), text.length + extra.indexOf('45%') + 3]];
      text += extra;
      return { calls: [{ tool: 'get_report', args: { by: 'basket', side: 'SELL', timeframe: '15m', metric: 'expectancy' }, status: 'ok', cols: [['basket', 'Basket'], ['n', 'Trades', 'n'], ['exp', 'Expectancy', 'metric']], rows, asOf, evidence: EV(rows.reduce((s, r) => s + r.n, 0)) }], text, marks, grounding: 'ungrounded' };
    }
    return { calls: [{ tool: 'get_report', args: { query: q.slice(0, 80) }, status: 'ok', cols: [['k', 'Result']], rows: [], asOf, evidence: EV(0) }], text: 'I could not find that in the stored data, so I did not calculate anything. Try one of the example questions, or ask for a report, a setup or tomorrow\'s plan.', marks: [], grounding: 'no_numbers' };
  }
  function seedConvs() {
    const mk = (title, q, ago_) => { const t = buildTurn(q); return { id: 'c' + Math.random().toString(36).slice(2, 6), title, at: Date.now() - ago_, msgs: [{ role: 'user', text: q }, { role: 'asst', text: t.text, calls: t.calls.map((c, i) => Object.assign({ n: i + 1 }, c)), marks: t.marks, grounding: t.grounding }] }; };
    return [mk('Which cells have enough trades to rank?', 'Which cells have enough trades to rank?', 3600000), mk("What is in tomorrow's plan?", "What is in tomorrow's plan?", 26 * 3600000), mk('Expectancy of 15m SELL setups', 'What is the expectancy of 15m SELL setups by basket?', 3 * 86400000)];
  }

  // ----- plain-text renderer: paragraphs, "- " and "1." lists, **bold**; everything else is inert text -----
  function plain(text, marks = []) {
    let t = text, off = 0;
    marks.slice().sort((a, b) => a[0] - b[0]).forEach(([s, e]) => { t = t.slice(0, s + off) + '\u0001' + t.slice(s + off, e + off) + '\u0002' + t.slice(e + off); off += 2; });
    const inline = (s) => esc(s).replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>').replace(/\u0001/g, '<mark class="ungrounded">').replace(/\u0002/g, '<span class="sr-only"> (not from a tool)</span></mark>');
    return t.split(/\n{2,}/).map((p) => {
      const lines = p.split('\n');
      if (lines.every((l) => /^- /.test(l))) return `<ul>${lines.map((l) => `<li>${inline(l.slice(2))}</li>`).join('')}</ul>`;
      if (lines.every((l) => /^\d+\. /.test(l))) return `<ol>${lines.map((l) => `<li>${inline(l.replace(/^\d+\. /, ''))}</li>`).join('')}</ol>`;
      return `<p>${lines.map(inline).join('<br>')}</p>`;
    }).join('');
  }
  A.plain = plain;

  const callDetail = (c) => {
    if (c.status === 'error') return `<div class="cite-detail"><div class="code-copy">${esc(c.tool)}(${esc(JSON.stringify(c.args))})</div><div class="st block">${I('alert')}<b>Error ${esc(c.code)}</b><span>${esc(c.message)}</span></div></div>`;
    const cell = (v, type) => type === 'metric' ? K.metric(v, (x) => K.R(x)) : type === 'pct' ? K.metric(v, (x) => Math.round(x * 100) + '%') : type === 'n' ? `<span class="num">${v}</span>` : esc(v);
    return `<div class="cite-detail"><div class="small muted">Arguments</div><div class="code-copy">${esc(c.tool)}(${esc(JSON.stringify(c.args))})</div>
      ${c.rows.length ? K.table(c.cols.map(([, l, ty]) => (ty === 'n' ? '>' : '') + l), c.rows.map((r) => c.cols.map(([k, , ty]) => cell(r[k], ty))), { stack: true }) : K.st('empty', 'the tool returned no rows', true)}
      ${c.truncated ? `<div class="small muted">Showing 50 of 143 rows</div>` : ''}${c.evidence ? K.evidence(c.evidence) : ''}</div>`;
  };
  const chipLabel = (c) => `#${c.n} ${c.tool} · ${c.status === 'error' ? 'error' : c.rows.length + ' rows'} · as of ${fmtDT(c.asOf)} · ${c.status === 'error' ? 'error' : c.running ? 'running' : 'ok'}`;
  function msgHtml(m, mi) {
    if (m.role === 'user') return `<div class="msg user"><div class="who">You</div><div class="bubble">${esc(m.text)}</div></div>`;
    const g = { grounded: ['checkCircle', 'Checked: every figure was found in the tool results above.'], ungrounded: ['alert', `${m.marks.length} figure${m.marks.length > 1 ? 's' : ''} in this answer ${m.marks.length > 1 ? 'were' : 'was'} not found in any tool result.`], no_numbers: null, not_checked: ['help', 'Not checked (stopped or failed).'] }[m.grounding || 'not_checked'];
    return `<div class="msg asst"><div class="who">Assistant</div>
      ${m.working ? `<div class="working" role="status">${I('refresh')} Working: ${esc(m.working)}</div>` : ''}
      ${m.text || m.stopped ? `<div class="bubble">${plain(m.text, m.marks || [])}${m.stopped ? `<p class="small muted">${I('octagon')} Stopped. The text above is partial.</p>` : ''}${m.cut ? `<p class="small muted">${I('alert')} The connection closed before the answer finished.</p>` : ''}</div>` : ''}
      ${g && !m.working ? `<div class="sysline" ${m.grounding === 'ungrounded' ? 'style="border-style:solid;border-color:var(--warn)"' : ''}>${I(g[0])}<span>${g[1]}</span></div>` : ''}
      ${m.calls && m.calls.length ? `<div class="cites" role="group" aria-label="Tool calls behind this answer">${m.calls.map((c, ci) => `<button class="cite" aria-expanded="${!!c.open}" data-cite="${mi}:${ci}" aria-label="Tool call ${c.n}, ${c.tool}, ${c.status === 'error' ? 'error' : c.rows.length + ' rows'}, ${c.status === 'error' ? 'error' : 'ok'}">${I(c.status === 'error' ? 'alert' : 'database')}<b>${esc(chipLabel(c))}</b></button>`).join('')}</div>${m.calls.map((c, ci) => (c.open ? callDetail(c) : '')).join('')}` : ''}</div>`;
  }

  function paintThread() {
    const th = $('#as-thread'); if (!th) return false;
    const c = A.conv;
    th.innerHTML = c.msgs.length ? c.msgs.map(msgHtml).join('') : `<div style="padding:8px"><b>Ask about your stored data.</b><div class="small muted" style="margin:4px 0 10px">Answers come from read-only tools. Each answer lists the tool calls behind it; a figure that no tool returned is underlined and flagged.</div>
      <div class="chips-row" id="as-ex">${["What is the expectancy of 15m SELL setups by basket?", 'Which cells have enough trades to rank?', "What is in tomorrow's plan?"].map((q) => `<button type="button">${esc(q)}</button>`).join('')}</div></div>`;
    $$('[data-cite]', th).forEach((b) => (b.onclick = () => { const [mi, ci] = b.dataset.cite.split(':').map(Number); const cl = c.msgs[mi].calls[ci]; cl.open = !cl.open; paintThread(); const nb = $(`[data-cite="${mi}:${ci}"]`); nb && nb.focus(); }));
    $$('#as-ex button', th).forEach((b) => (b.onclick = () => { $('#as-q').value = b.textContent; $('#as-q').focus(); updateCounter(); }));
    th.scrollTop = th.scrollHeight;
    const live = $('#as-live'); if (live && !A.busy && A.announce) { live.textContent = A.announce; A.announce = ''; }
    return true;
  }
  const updateCounter = () => {
    const q = $('#as-q'), b = $('#as-act'); if (!q || !b) return;
    const n = q.value.length; $('#as-count').textContent = n > 1500 ? `${n} / 2000` : '';
    b.disabled = !A.busy && (status() !== 'ready' || !q.value.trim() || n > 2000);
  };
  function say(text) { const live = $('#as-live'); if (live) live.textContent = text; }

  function ask(q) {
    const conv = A.conv;
    if (conv.id === 'new') { conv.id = 'c' + Math.random().toString(36).slice(2, 6); A.convs.unshift(conv); }
    conv.msgs.push({ role: 'user', text: q });
    if (conv.msgs.length === 1) conv.title = q.slice(0, 60);
    const turn = buildTurn(q);
    const m = { role: 'asst', text: '', calls: [], marks: [], working: 'reading your question', grounding: null };
    conv.msgs.push(m);
    A.busy = true; A.turn = { m, stop: false };
    $('#as-q').value = ''; A.draft = '';
    setComposer(); paintThread(); say('Assistant is working');
    const h3 = $('.chat-card .card-h h3'); if (h3) h3.lastChild.textContent = conv.title;
    const at = (ms, fn) => A.timers.push(setTimeout(() => { if (!$('#as-thread') || A.turn.stop) return; fn(); }, ms));
    const call = Object.assign({ n: 1 }, turn.calls[0], { running: true });
    at(500, () => { m.calls.push(call); m.working = 'calling ' + call.tool; paintThread(); });
    at(1300, () => { call.running = false; m.working = 'answering'; paintThread(); });
    const words = turn.text.split(/(\s+)/);
    let pos = 0;
    const tick = () => {
      if (!$('#as-thread') || A.turn.stop) return;
      for (let k = 0; k < 3 && words.length; k++) { const w = words.shift(); m.text += w; pos += w.length; }
      paintThread();
      if (words.length) A.timers.push(setTimeout(tick, 45));
      else { m.working = null; m.marks = turn.marks; m.grounding = turn.grounding; A.busy = false; A.turn = null; A.announce = 'Answer ready'; setComposer(); paintThread(); const qq = $('#as-q'); qq && qq.focus(); BT.audit('owner', 'Assistant question', 'Read-only tools used: ' + turn.calls.map((c) => c.tool).join(', ')); BT.save(); }
    };
    at(1450, tick);
  }
  function stop() {
    if (!A.turn) return;
    A.turn.stop = true; A.timers.forEach(clearTimeout); A.timers = [];
    const m = A.turn.m; m.working = null; m.stopped = true; m.grounding = 'not_checked'; m.calls.forEach((c) => (c.running = false));
    A.busy = false; A.turn = null; setComposer(); paintThread(); say('Stopped'); const qq = $('#as-q'); qq && qq.focus();
  }
  function setComposer() {
    const btn = $('#as-act'); if (!btn) return;
    btn.innerHTML = A.busy ? `${I('octagon')} Stop` : `${I('send')} Send`;
    btn.className = 'btn ' + (A.busy ? 'bad' : 'primary');
    $('#as-q').disabled = A.busy || status() !== 'ready';
    updateCounter();
  }

  function settingsDialog() {
    const cur = { en: K.on('assistant'), pf: !!K.P().sw['assistant.portfolio'], max: K.P().maxCalls || 6 };
    BT.modal({
      title: 'Assistant settings (owner act)',
      body: `<div class="banner info" style="margin:0">${I('lock')}<div>Saving needs a reason and your password. The assistant only reads stored data and cannot place an order, change a setting, or write anywhere.</div></div>
        <div class="card"><div class="card-b flush">
          <div class="owner-row"><div><b>Assistant</b> ${K.safety()}<div class="desc">Off by default. When off, the screen says so and nothing is sent to the model.</div></div><label class="switch"><input type="checkbox" role="switch" id="st-en" aria-label="Assistant" ${cur.en ? 'checked' : ''}><span></span></label></div>
          <div class="owner-row"><div><b>Portfolio data</b> ${K.safety()}<div class="desc" id="st-pf-d">${cur.pf ? 'On: the assistant may read holdings and positions.' : "Off: portfolio questions answer 'not available'"}</div></div><label class="switch"><input type="checkbox" role="switch" id="st-pf" aria-label="Portfolio data" ${cur.pf ? 'checked' : ''}><span></span></label></div></div></div>
        <label class="field">Maximum tool calls per answer (1 to 12)<input class="input" type="number" id="st-max" min="1" max="12" value="${cur.max}"></label>
        <label class="field">Reason (1 to 500 characters, required)<textarea class="input" id="st-reason" maxlength="500" rows="2" style="font-family:var(--font)"></textarea></label>
        <label class="field">Password<input class="input" type="password" id="st-pw" autocomplete="current-password" value="demo-password"></label>
        <div class="err" id="st-err" role="alert"></div><div class="small muted">The dialog never shows the password again and clears it when closed. Prototype: demo password prefilled.</div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="st-save">Save</button>',
      onMount: (ov, close) => {
        $('#st-pf', ov).onchange = (e) => { $('#st-pf-d', ov).textContent = e.target.checked ? 'On: the assistant may read holdings and positions.' : "Off: portfolio questions answer 'not available'"; };
        $('#st-save', ov).onclick = () => {
          const err = (m) => { $('#st-err', ov).textContent = m; };
          const r = $('#st-reason', ov).value.trim(), mx = +$('#st-max', ov).value;
          if (!r) return err('Enter a reason.'); if (!(mx >= 1 && mx <= 12)) return err('Maximum tool calls must be 1 to 12.');
          if (D.hash($('#st-pw', ov).value) !== BT.S.auth.pw) return err('Wrong password.');
          const wasOn = K.on('assistant');
          K.P().sw.assistant = $('#st-en', ov).checked; K.P().sw['assistant.portfolio'] = $('#st-pf', ov).checked; K.P().maxCalls = mx;
          if (!wasOn && K.P().sw.assistant) { A.sim = 'down'; }
          BT.audit('owner', 'Assistant settings saved', `assistant ${K.P().sw.assistant ? 'ON' : 'OFF'}, portfolio data ${K.P().sw['assistant.portfolio'] ? 'ON' : 'OFF'}, max ${mx} tool calls`);
          BT.save(); $('#st-pw', ov).value = ''; close(); BT.toast('Settings saved', 'good'); BT.rerender();
        };
      },
    });
  }

  BT.pages.assistant = function (el) {
    A.timers.forEach(clearTimeout); A.timers = [];
    if (A.busy) { A.busy = false; A.turn = null; }
    if (!A.convs) { A.convs = seedConvs(); A.conv = { id: 'new', title: 'New conversation', at: Date.now(), msgs: [] }; }
    const st = status();
    const gate = GATES[st];
    const sims = [['off', 'Off'], ['down', 'Model down'], ['loading', 'Loading'], ['misconfigured', 'Key rejected'], ['tools_down', 'Tools down'], ['busy', 'Busy'], ['nightly', 'Nightly running'], ['ready', 'Ready']];
    const simbar = `<div class="simbar" role="group" aria-label="Prototype only: simulate the status"><span>${I('lightbulb')} Prototype only, pretend the status is:</span>${sims.map(([k, l]) => `<button data-sim="${k}" class="${(k === 'off' ? !K.on('assistant') : K.on('assistant') && A.sim === k) ? 'on' : ''}">${l}</button>`).join('')}<span class="spacer"></span><span>Evaluation:</span>${[['not_yet_evaluated', 'not yet'], ['failed', 'failed'], ['passed', 'passed']].map(([k, l]) => `<button data-eval="${k}" class="${A.eval === k ? 'on' : ''}">${l}</button>`).join('')}</div>`;
    const evalLine = { not_yet_evaluated: [I('alert'), 'Not yet evaluated: answers have not been checked against the test set'], failed: [I('alert'), 'Evaluation failed on 12 Oct 2026: some answers did not match the test set. Read answers with care.'], passed: [I('clipboard'), 'Evaluated 20 Oct 2026'] }[A.eval];
    let body;
    if (st !== 'ready' && !['busy', 'nightly'].includes(st)) {
      body = K.card('Assistant', `<div class="card-b" style="display:grid;gap:12px;max-width:640px">
        <div class="st block" style="font-size:15px">${I(gate[0])}<b>${gate[1]}</b></div><div>${gate[2]}</div>
        ${st === 'down' ? '<div class="code-copy" id="as-cmd">scripts/xd-ai.sh start</div><div class="small muted">The page cannot start the model server. This screen checks again every 5 seconds.</div>' : ''}
        ${st === 'tools_down' ? '<div class="code-copy">scripts/xd-ai.sh start</div>' : ''}
        ${st === 'loading' ? `<div class="progress"><i class="grow" style="width:45%"></i></div><div class="small muted" id="as-elapsed">Waiting for ${Math.max(1, Math.round((Date.now() - A.since) / 1000))} s</div>` : ''}
        ${st === 'off' ? `<div class="row"><button class="btn primary" id="as-turnon">Turn on</button><span class="small muted">opens the settings dialog</span></div>` : `<div class="row"><button class="btn" id="as-settings">${I('sliders')} Settings</button></div>`}
        <div class="small muted">No number is drawn in this state.</div></div>`);
    } else {
      body = `<div class="chat-split"><div class="chat-hist-wrap"><div class="card"><div class="card-h"><h3>Conversations</h3></div><div class="card-b">${histHtml()}</div></div></div>
        <div class="card chat-card" data-nocollapse="1"><div class="card-h"><h3>${esc(A.conv.title)}</h3><div class="row"><button class="btn sm chat-hist-btn" id="as-histbtn">${I('history')} History</button><button class="btn sm" id="as-settings">${I('sliders')} Settings</button></div></div>
          ${['busy', 'nightly'].includes(st) ? `<div class="sysline" style="margin:12px 16px 0">${I(gate[0])}<span><b>${gate[1]}.</b> ${gate[2]}</span></div>` : ''}
          <div class="thread" id="as-thread" role="log" aria-live="off" aria-label="Conversation"></div>
          <div class="sr-only" id="as-live" role="status" aria-live="polite"></div>
          <form class="composer" id="as-form"><label class="field" for="as-q">Ask about your data</label>
            <textarea class="input" id="as-q" rows="2" ${st !== 'ready' ? 'disabled' : ''} placeholder="${st === 'ready' ? 'Ask about your stored data. Enter sends, Shift+Enter adds a line.' : esc(gate[1])}"></textarea>
            <div class="row between"><span class="small muted" id="as-count"></span><span class="small muted">Read-only: it cannot place an order or change anything.</span><button class="btn primary" id="as-act" type="submit" ${st !== 'ready' ? 'disabled' : ''}>${I('send')} Send</button></div>
            </form></div></div>`;
    }
    el.innerHTML = K.head('Assistant', 'Ask questions about your stored data. It reads through fixed read-only tools; every answer shows the tool calls and the rows behind it.', ['P9-04'], '') + simbar
      + `<div class="sysline" style="margin-bottom:14px" role="note">${evalLine[0]}<span>${evalLine[1]}</span></div>` + body
      + K.evidence({ basis: 'answers are written by a local model from tool results; the model has no write path', population: 'the owner\'s stored records only (plans, cells, runs); portfolio data only if switched on', limits: ['Plain text only: no links, images or HTML from the model.', 'A figure the server cannot find in a tool result is marked "not from a tool".', 'Not a recommendation. Nothing here places or changes an order.'] });
    $$('[data-sim]', el).forEach((b) => (b.onclick = () => { const k = b.dataset.sim; if (k === 'off') K.P().sw.assistant = false; else { K.P().sw.assistant = true; A.sim = k; A.since = Date.now(); } BT.save(); BT.rerender(); }));
    $$('[data-eval]', el).forEach((b) => (b.onclick = () => { A.eval = b.dataset.eval; BT.rerender(); }));
    const so = $('#as-settings', el); if (so) so.onclick = settingsDialog;
    const to = $('#as-turnon', el); if (to) to.onclick = settingsDialog;
    if (st === 'loading') { const tm = () => { const e = $('#as-elapsed'); if (!e) return; e.textContent = `Waiting for ${Math.round((Date.now() - A.since) / 1000)} s`; setTimeout(tm, 1000); }; setTimeout(tm, 1000); }
    if (!$('#as-thread', el)) return;
    paintThread();
    const q = $('#as-q', el);
    q.value = A.draft || '';
    q.oninput = () => { A.draft = q.value; updateCounter(); };
    q.onkeydown = (e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); $('#as-form').requestSubmit(); } };
    document.onkeydown = null;
    $('#as-form').onsubmit = (e) => { e.preventDefault(); if (A.busy) return stop(); const v = q.value.trim(); if (!v || v.length > 2000 || status() !== 'ready') return; ask(v); };
    $('#as-act').onclick = (e) => { if (A.busy) { e.preventDefault(); stop(); } };
    $('#as-thread', el).addEventListener('keydown', (e) => { if (e.key === 'Escape') stop(); });
    q.addEventListener('keydown', (e) => { if (e.key === 'Escape') stop(); });
    $$('[data-conv]', el).forEach((b) => (b.onclick = () => { openConv(b.dataset.conv); }));
    const nb = $('#as-new', el); if (nb) nb.onclick = () => { A.conv = { id: 'new', title: 'New conversation', at: Date.now(), msgs: [] }; BT.rerender(); };
    const hb = $('#as-histbtn', el); if (hb) hb.onclick = () => BT.drawer({ title: 'Conversations', body: histHtml(), onMount: (ov, close) => $$('[data-conv]', ov).forEach((b) => (b.onclick = () => { close(); openConv(b.dataset.conv); })) });
    setComposer();
  };
  function histHtml() {
    return `<div class="chat-hist"><button class="btn sm" id="as-new">${I('chat')} New conversation</button>${A.convs.length ? A.convs.map((c) => `<button data-conv="${c.id}" class="${A.conv.id === c.id ? 'on' : ''}">${esc(c.title)}<small>${fmtDT(c.at)}</small></button>`).join('') : '<div class="empty">No conversations yet</div>'}</div>`;
  }
  function openConv(id) { const c = A.convs.find((x) => x.id === id); if (c) { A.conv = c; BT.rerender(); } }
  // iconless fallback for the "power" icon used in gate states
  BT.assistantA = A;
})();
