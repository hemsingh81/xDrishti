/* xDrishti prototype — Paper & live (P10 / P11) and Execution (P12) pages, app-bar stop chip, bell notices.
   Every screen is marked with its spec; all demo data; nothing here places an order. */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData, K = BT.kit;
  const { $, $$, esc, num, inr, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const I = BT.icon, R = K.R;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);
  const mean = (a) => (a.length ? sum(a) / a.length : null);
  const r2 = (x) => Math.round(x * 100) / 100;
  const DAYMS = 86400000;

  /* =====================================================================================
     PAPER & LIVE   P10-02 Ledger · P10-03 Review, Discipline · P10-04 Backtest vs paper
                    P10-05 Governance · P10-06 Phase check · P11-01/03/04 Live tab (placeholders)
     ===================================================================================== */
  const pp = { tab: 'ledger', live: 'fills', week: 0, seen: '' };
  const PTABS = [['ledger', 'Ledger'], ['review', 'Review'], ['discipline', 'Discipline'], ['compare', 'Backtest vs paper'], ['governance', 'Governance'], ['phase', 'Phase check'], ['live', 'Live (P11)']];
  const CHARGE_R = 0.035; // modelled charges per trade in R (stored demo sheet, not confirmed as yours)
  const paperAcc = () => BT.S.accounts.find((a) => a.roles.trading.enabled && a.roles.trading.mode === 'paper');
  const epochStart = () => D.DAYS[D.DAYS.length - 1 - 29];
  function paperTrades() {
    const acc = paperAcc(); if (!acc) return [];
    const from = epochStart();
    return BT.tradesData().filter((t) => t.account === acc.id && t.date >= from).sort((a, b) => (a.date < b.date ? -1 : 1));
  }
  const counted = () => paperTrades().filter((t) => t.taken);
  const isoWeek = (iso) => { const d = new Date(iso + 'T00:00:00Z'); const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())); const day = t.getUTCDay() || 7; t.setUTCDate(t.getUTCDate() + 4 - day); const y0 = new Date(Date.UTC(t.getUTCFullYear(), 0, 1)); return t.getUTCFullYear() + '-W' + String(Math.ceil(((t - y0) / DAYMS + 1) / 7)).padStart(2, '0'); };
  // 60 counted -> value, 30..59 -> provisional (interim), under 30 -> insufficient (P10-04 criterion 6)
  function band(n) { return n >= 60 ? 'value' : n >= 30 ? 'provisional' : 'insufficient'; }
  const stateMetric = (n, v, fmt) => { const b = band(n); return b === 'value' ? `<span class="num">${fmt(v)}</span> <span class="small muted">n ${n}</span>` : b === 'provisional' ? `<span class="num">${fmt(v)}</span> ${K.st('provisional', `interim: n ${n} of 60`)}` : K.st('insufficient', `n ${n} of at least 30`); };

  function noPaper(el) {
    el.innerHTML = K.head('Paper & live', 'Forward evidence from simulated trades, then your own fills.', ['P10-02'])
      + `<div class="card"><div class="card-b">${K.st('empty', 'no account is in paper mode. Set an account to paper in Accounts, Trading.', true)}</div></div>`;
  }

  BT.pages.paper = function (el) {
    K.hashTab(pp, PTABS);
    const acc = paperAcc();
    if (!acc) return noPaper(el);
    const ct = counted(), n = ct.length;
    el.innerHTML = K.head('Paper & live', 'Twelve weeks of forward, rules-fixed evidence: every ticket you approved, followed to a timed, priced, costed paper trade. No order exists; nothing on these tabs writes anything.', ['P10-02', 'P10-03', 'P10-04', 'P10-05', 'P10-06', 'P11-03', 'P11-04'])
      + `<div class="banner info">${I('fileText')}<div><b>Paper account: ${esc(acc.name)}</b> · epoch <b>first</b> since ${fmtDate(new Date(epochStart()))} · counted trades <b>${n} of 60</b> · week <b>${Math.min(12, Math.ceil(30 / 5))} of 12</b>. Every figure on these tabs is labelled paper: simulated fills on official 1-minute bars with modelled slippage, never a real fill.</div></div>`
      + K.tabs(PTABS, pp.tab, 'Paper and live') + '<div id="pp-body" class="fade-swap"></div>';
    K.bindTabs(el, (t) => { pp.tab = t; BT.rerender(); });
    ({ ledger, review, discipline, compare, governance, phase, live }[pp.tab] || ledger)($('#pp-body', el), acc);
  };

  /* ---------------- Ledger (P10-02) ---------------- */
  function ledger(body, acc) {
    const tr = paperTrades(), ct = counted();
    const cap = acc.roles.trading.capital, gross = sum(ct, (t) => t.pnl + 180), charges = sum(ct, () => 180), net = gross - charges;
    const days = [...new Set(ct.map((t) => t.date))].sort();
    let eq = cap; const curve = days.map((d) => { eq += sum(ct.filter((t) => t.date === d), (t) => t.pnl); return [d, Math.round(eq)]; });
    const last = days[days.length - 1], lastRes = last ? sum(ct.filter((t) => t.date === last), (t) => t.pnl) : null;
    const plan = BT.S.plan.items.filter((i) => !i.reserve).slice(0, 2);
    const open = plan.map((i) => { const a = (i.alloc || []).find((x) => x.account === acc.id); const qty = a ? a.qty : 40; const entry = i.trigger, ltp = BT.ltp(i.symbol); const dir = i.side === 'BUY' ? 1 : -1; return { sym: i.symbol, side: i.side, qty, entry, ltp, stop: i.stop, unreal: (ltp - entry) * qty * dir, risk: Math.abs(entry - i.stop) * qty }; });
    const openRisk = sum(open, (o) => o.risk);
    body.innerHTML = `<div class="tiles">
        <div class="tile"><div class="l">Paper equity</div><div class="v">${inr(eq + sum(open, (o) => o.unreal))}</div><div class="s">capital ${inr(cap)} + realised + open marks · paper</div></div>
        <div class="tile"><div class="l">Realised, gross</div><div class="v ${sgn(gross)}">${inr(gross)}</div><div class="s">gross, no charges · ${ct.length} closed trades</div></div>
        <div class="tile"><div class="l">Realised, after charges</div><div class="v ${sgn(net)}">${inr(net)}</div><div class="s">${K.st('provisional', 'sheet not confirmed by the owner (ISS-25)')}</div></div>
        <div class="tile"><div class="l">Open risk</div><div class="v">${(openRisk / cap * 100).toFixed(2)}%</div><div class="s">of equity · ${open.length} open · risk to stop</div></div>
        <div class="tile"><div class="l">Funds available</div><div class="v">${inr(Math.max(0, cap - sum(open, (o) => o.entry * o.qty)))}</div><div class="s">equity − open notional</div></div>
        <div class="tile"><div class="l">Last session result</div><div class="v ${sgn(lastRes)}">${lastRes == null ? '—' : inr(lastRes)}</div><div class="s">${last ? fmtDate(new Date(last)) : K.st('empty', 'no closed session yet')}</div></div></div>
      <div class="grid g2">
        ${K.card('Realised equity (closed trades, paper)', ct.length ? '<div class="card-b"><div class="chart sm" id="pp-eq" role="img" aria-label="Paper realised equity over the epoch"></div><details><summary class="small">Table of the same numbers</summary>' + K.table(['Date', '>Equity'], curve.slice(-12).map(([d, v]) => [d, inr(v)])) + '</details></div>' : `<div class="card-b">${K.st('empty', 'no paper trade yet: a ticket must be approved, armed and captured', true)}</div>`)}
        ${K.card('Open paper positions', open.length ? K.table(['Instrument', 'Side', '>Qty', '>Entry', '>Last', '>Unrealised', '>Risk to stop'], open.map((o) => [`<b>${esc(o.sym)}</b>`, o.side, o.qty, num(o.entry), num(o.ltp), `<span class="${sgn(o.unreal)}">${inr(o.unreal)}</span>`, inr(o.risk)]), { stack: true }) + `<div class="card-b small muted">Marks at the last price (${ago(BT.lastTick || Date.now())}); a stale mark would read ${'`unavailable`'.replace(/`/g, '')}, never 0. Paper positions: no order exists.</div>` : `<div class="card-b">${K.st('empty', 'no paper position is open')}</div>`, '', 'flush')}
      </div>
      ${K.card('Closed paper trades', ct.length ? K.table(['Closed', 'Instrument', 'Setup', '>Plan R', '>Paper R (gross)', '>After charges', '>Result'], ct.slice(-15).reverse().map((t) => [t.date, `<b>${esc(t.symbol)}</b> <span class="chip ${t.side === 'BUY' ? 'green' : 'red'}">${t.side}</span>`, `${esc(t.strategy)} ${t.tf}`, R(t.planR), R(t.R), R(t.R - CHARGE_R) + ' <span class="small muted">sheet not confirmed</span>', inr(t.pnl)]), { stack: true }) + `<div class="grid-foot small muted"><span>Newest 15 of ${ct.length}. Skipped tickets are listed on the Discipline tab with their skip code.</span></div>` : `<div class="card-b">${K.st('empty', 'no paper trade yet: a ticket must be approved, armed and captured', true)}</div>`, '', 'flush')}
      ${K.evidence({ basis: 'paper: simulated fills on official 1-minute bars, modelled slippage; gross, with charges shown only from the stored sheet', population: `paper account ${esc(acc.name)}, epoch first, counted trades only`, range: `${epochStart()} to ${K.lastDay()}`, n: ct.length, limits: ['Paper has no real fills: missed triggers and real slippage are not evaluable here (P11).', 'The charge sheet is not confirmed as yours: net figures are labelled, never a result of record.', 'No write control exists on this tab; the ledger derives from stored paper trades.'] })}`;
    if (ct.length) BT.echart($('#pp-eq', body), { grid: { left: 60, right: 14, top: 14, bottom: 26 }, tooltip: { trigger: 'axis' }, xAxis: { type: 'category', data: curve.map((c) => c[0].slice(5)) }, yAxis: { type: 'value', scale: true, axisLabel: { formatter: (v) => (v / 1000).toFixed(0) + 'k' } }, series: [{ type: 'line', step: 'end', data: curve.map((c) => c[1]), showSymbol: false, areaStyle: { opacity: .08 } }] });
  }

  /* ---------------- Review (P10-03) ---------------- */
  function review(body) {
    const ct = counted();
    const weeks = [...new Set(ct.map((t) => isoWeek(t.date)))].sort().reverse();
    const wks = weeks.length ? weeks : [isoWeek(K.lastDay())];
    pp.week = Math.min(pp.week, wks.length - 1);
    const w = wks[pp.week];
    const tw = ct.filter((t) => isoWeek(t.date) === w);
    const skipped = paperTrades().filter((t) => !t.taken && isoWeek(t.date) === w);
    const changed = tw.filter((t) => t.decision === 'modified');
    const cur = pp.week === 0;
    body.innerHTML = `<div class="row" style="margin-bottom:12px"><label class="field" style="flex-direction:row;align-items:center;gap:8px">ISO week<select class="input" id="rv-w" style="width:auto">${wks.map((x, i) => `<option value="${i}" ${i === pp.week ? 'selected' : ''}>${x}</option>`).join('')}</select></label>
        <span class="chip">${cur ? 'stored Saturday 10:30 IST, insert-only' : 'stored earlier'}</span><span class="small muted">The review is deterministic text from stored facts. Nothing in it is applied.</span></div>
      <div class="grid g2">
        ${K.card('1 · Where it stands', `<div class="card-b"><dl class="kv"><dt>Epoch</dt><dd>first · ${esc(band(ct.length) === 'insufficient' ? 'measuring' : 'measuring')}</dd><dt>Counted trades</dt><dd>${ct.length} of 60 ${K.st('not_evaluable', 'the exit needs 60')}</dd><dt>Weeks</dt><dd>6 of 12</dd><dt>Charge sheet</dt><dd>${K.st('provisional', 'stored sheet, not confirmed as yours')}</dd></dl></div>`)}
        ${K.card(`2 · What happened in ${w}`, `<div class="card-b"><dl class="kv"><dt>Closed trades</dt><dd>${tw.length}</dd><dt>Mean R, gross</dt><dd>${tw.length >= 10 ? `<span class="num">${R(mean(tw.map((t) => t.R)))}</span> <span class="small muted">n ${tw.length}</span>` : K.st('insufficient', `n ${tw.length} of at least 10 for a weekly mean`)}</dd><dt>Total R, gross</dt><dd>${tw.length ? R(sum(tw, (t) => t.R)) : K.st('empty', 'no trade closed this week')}</dd></dl></div>`)}
        ${K.card('3 · Skipped, added or resized', `<div class="card-b"><dl class="kv"><dt>Skipped by you</dt><dd>${skipped.length} tickets ${skipped.length ? `<span class="small muted">plan R they would have had: ${R(sum(skipped, (t) => t.planR))} (not taken, so not a result)</span>` : ''}</dd><dt>Resized down</dt><dd>${changed.length} tickets</dd><dt>Added outside the plan</dt><dd>${K.st('not_evaluable', 'paper records only plan tickets; outside-plan adds are measured with real fills (P11)')}</dd></dl></div>`)}
        ${K.card('4 · Configuration changes this week', `<div class="card-b">${K.table(['Change', 'Class'], [['Review cutoff 19:30 → 19:45', 'noted only (irrelevant to the count)']])}<div class="small muted" style="margin-top:8px">No change restarted the count. A change that would is shown on the Governance tab before it is captured.</div></div>`)}
      </div>
      <div class="card" style="margin-top:16px"><div class="card-b"><b>Review text</b><p style="margin:6px 0 0">Week ${w}: ${tw.length} paper trades closed, ${skipped.length} tickets skipped. The count stands at ${ct.length} of 60 for the first epoch, so no figure here is a result. Nothing was applied: every item above is a proposal; applying any of it is your own act.</p></div></div>
      ${K.card('Closed trades of the week', tw.length ? K.table(['Closed', 'Instrument', 'Setup', '>R (gross)', 'Decision'], tw.map((t) => [t.date, `<b>${esc(t.symbol)}</b> ${t.side}`, `${esc(t.strategy)} ${t.tf}`, R(t.R), t.decision])) : `<div class="card-b">${K.st('empty', 'no trade closed in this week', true)}</div>`, '', 'flush')}
      ${K.evidence({ basis: 'paper, gross; charges from the stored sheet are not confirmed', population: `counted paper trades of ${w}`, n: tw.length, limits: ['Weekly numbers on this few trades are noise; the review prints counts, not conclusions.', 'A dry-run review of an older week says "State as of the build date, not as of the end of the week".'] })}`;
    $('#rv-w', body).onchange = (e) => { pp.week = +e.target.value; BT.rerender(); };
  }

  /* ---------------- Discipline (P10-03) ---------------- */
  function discipline(body) {
    const all = paperTrades();
    const by = (f) => all.filter(f);
    const lines = [['Taken as approved', by((t) => t.decision === 'approved')], ['Resized (modified)', by((t) => t.decision === 'modified')], ['Declined by you', by((t) => t.decision === 'rejected')], ['Lapsed (not reviewed in time)', by((t) => t.decision === 'lapsed')]];
    const ct = counted();
    const planR = mean(ct.map((t) => t.planR)), slipR = mean(ct.map((t) => t.R - t.planR)), chR = -CHARGE_R;
    const rows = [['Plan prices (mean R)', planR], ['Modelled slippage', slipR], ['Charges (stored sheet, not confirmed)', chR]];
    const net = planR + slipR + chR; const mx = Math.max(...rows.map((r) => Math.abs(r[1])), Math.abs(net), 0.01);
    body.innerHTML = `<div class="grid g2">
      ${K.card('Discipline lines', K.table(['Line', '>Tickets', '>R of the plan prices', 'Reading'], lines.map(([l, a]) => [l, a.length, a.length >= 10 ? `<span class="num">${R(sum(a, (t) => t.planR))}</span> <span class="small muted">n ${a.length}</span>` : K.st('insufficient', `n ${a.length} of at least 10`), l.startsWith('Declined') || l.startsWith('Lapsed') ? 'R the plan would have had; not a result' : 'in the count']), { stack: true }) + '<div class="card-b small muted">The lines count how you used the plan. They are not part of the waterfall and are not scored.</div>', '', 'flush')}
      ${K.card('Paper execution gap (waterfall)', ct.length >= 10 ? `<div class="card-b">${rows.concat([['Net, after charges', net]]).map(([l, v]) => `<div class="bar-row"><span>${l}</span><div class="bar" aria-hidden="true"><i style="${v >= 0 ? `left:50%;width:${Math.abs(v) / mx * 50}%` : `right:50%;width:${Math.abs(v) / mx * 50}%`}"></i></div><b class="num">${R(v, 3)}</b></div>`).join('')}
        <div class="small muted" style="margin-top:6px">n ${ct.length} counted trades. The same numbers as text: ${rows.map(([l, v]) => esc(l) + ' ' + R(v, 3)).join('; ')}; net ${R(net, 3)}.</div>
        <div style="margin-top:10px;display:grid;gap:6px"><div>Missed triggers: ${K.st('not_evaluable', 'paper has no real fills (P11)')}</div><div>Real slippage: ${K.st('not_evaluable', 'paper has no real fills (P11)')}</div></div></div>` : `<div class="card-b">${K.st('insufficient', `n ${ct.length} of at least 10`, true)}</div>`)}
    </div>
    ${K.evidence({ basis: 'paper: plan R, modelled slippage and charges; net labelled as sheet not confirmed', population: 'paper tickets of the epoch (all decisions) and counted trades', n: all.length, limits: ['Decisions are counted as you made them; the system does not judge them.', 'The waterfall carries a text alternative of every number.', 'No composite score exists, on purpose.'] })}`;
  }

  /* ---------------- Backtest vs paper (P10-04) ---------------- */
  function compare(body) {
    const ct = counted(), n = ct.length;
    const refs = BT.tradesData().filter((t) => t.taken && t.date < epochStart());
    const cells = [...new Set(ct.map((t) => `${t.strategy}|${t.side}|${t.tf}`))];
    const thin = cells.filter((c) => refs.filter((t) => `${t.strategy}|${t.side}|${t.tf}` === c).length < 30);
    const rnd = D.rng('band:' + n), N = 400, m = Math.max(n, 1);
    const paths = [];
    for (let i = 0; i < N; i++) { let c = 0; const p = []; for (let k = 0; k < m; k++) { c += refs[Math.floor(rnd() * refs.length)].R; p.push(c); } paths.push(p); }
    const q = (k, f) => { const a = paths.map((p) => p[k]).sort((x, y) => x - y); return a[Math.floor(f * (a.length - 1))]; };
    const paperPath = []; let c = 0; ct.forEach((t) => { c += t.R - CHARGE_R; paperPath.push(r2(c)); });
    const b = band(n);
    body.innerHTML = `${n < 60 ? `<div class="banner info">${I('alert')}<div><b>Interim.</b> Fewer than 60 counted trades: ${n < 30 ? 'every figure is insufficient and only the count is shown' : 'figures are provisional (interim)'}. The paper line is drawn over the backtest fan so you can see where it runs; it is not a verdict.</div></div>` : ''}
      <div class="grid g2">
        ${K.card('Criterion 1 · mean R after costs inside the backtest band', `<div class="card-b" style="display:grid;gap:8px"><div>Paper mean R after charges: ${n ? stateMetric(n, mean(ct.map((t) => t.R - CHARGE_R)), (v) => R(v)) : K.st('empty', 'no counted trade')}</div>
          <div>Backtest band for these cells in these proportions (5th to 95th percentile of the mean): ${b === 'insufficient' ? K.st('insufficient', `n ${n} of at least 30`) : `<span class="num">${R(q(m - 1, .05) / m)} to ${R(q(m - 1, .95) / m)}</span> <span class="small muted">seeded bootstrap, ${N} draws (the app uses 10,000)</span>`}</div>
          <div>Verdict: ${K.st(b === 'value' ? 'provisional' : 'not_evaluable', b === 'value' ? 'a verdict is printed with its label' : `needs n of 60; now ${n}`)}</div></div>`)}
        ${K.card('Criterion 2 · largest drawdown no worse than the backtest 90th percentile', `<div class="card-b" style="display:grid;gap:8px"><div>Paper max drawdown: ${n >= 5 ? `<span class="num">${R(Math.min(0, ...paperPath.map((v, i) => v - Math.max(0, ...paperPath.slice(0, i + 1)))))}</span> ${band(n) === 'insufficient' ? '<span class="small muted">shown for reading only</span>' : ''}` : K.st('insufficient', 'fewer than 5 trades')}</div>
          <div>Backtest 90th percentile at ${n} trades: ${b === 'insufficient' ? K.st('insufficient', `n ${n} of at least 30`) : '<span class="num">computed</span>'}</div>
          <div>Verdict: ${K.st('not_evaluable', `needs n of 60; now ${n}`)}</div></div>`)}
      </div>
      ${K.card('Paper line over the backtest fan (cumulative R, after charges)', n ? '<div class="card-b"><div class="chart" id="cp-fan" role="img" aria-label="Paper cumulative R over the backtest fan"></div><details><summary class="small">Table of the same numbers</summary>' + K.table(['Trade #', '>Paper', '>Fan 5%', '>Fan 50%', '>Fan 95%'], [0, Math.floor((m - 1) / 2), m - 1].map((k) => [k + 1, paperPath[k] != null ? R(paperPath[k]) : '—', R(q(k, .05)), R(q(k, .5)), R(q(k, .95))])) + '</details></div>' : `<div class="card-b">${K.st('empty', 'no counted trade to draw', true)}</div>`)}
      ${K.evidence({ basis: 'paper after charges (sheet not confirmed) vs a composition-matched bootstrap of backtest trades', population: `${n} counted paper trades; ${refs.length} backtest reference trades in ${cells.length} cells`, n, more: [['Thin references', thin.length ? K.st('insufficient', thin.length + ' cell(s) have fewer than 30 reference trades: ' + thin.slice(0, 3).map(esc).join(', ')) : 'none']], limits: ['Selection and clustering of the cells are not corrected for; the band is an approximation.', 'About 2.1 years of history and no hold-out sit behind the cells.', '60 trades give an interval of about ±0.3R: even a pass is weak evidence.'] })}`;
    if (n) {
      const x = Array.from({ length: m }, (_, k) => k + 1);
      BT.echart($('#cp-fan', body), { grid: { left: 50, right: 16, top: 24, bottom: 30 }, tooltip: { trigger: 'axis' }, legend: { top: 0 }, xAxis: { type: 'category', data: x, name: 'trade #', nameLocation: 'middle', nameGap: 22 }, yAxis: { type: 'value', name: 'R' },
        series: [{ name: 'Backtest 5%', type: 'line', data: x.map((_, k) => r2(q(k, .05))), showSymbol: false, lineStyle: { type: 'dashed' } }, { name: 'Backtest 50%', type: 'line', data: x.map((_, k) => r2(q(k, .5))), showSymbol: false, lineStyle: { type: 'dashed' } }, { name: 'Backtest 95%', type: 'line', data: x.map((_, k) => r2(q(k, .95))), showSymbol: false, lineStyle: { type: 'dashed' } }, { name: 'Paper', type: 'line', data: paperPath, showSymbol: false, lineStyle: { width: 3 } }] });
    }
  }

  /* ---------------- Governance (P10-05) ---------------- */
  function governance(body) {
    const st = BT.S.settings, prof = BT.S.riskProfiles[0];
    const comp = [['Strategies and cells in force', 'cell snapshot 2026-10-12 · 24 cells', 'restarts the count'], ['Risk profile (standard)', `${prof.riskPct}% per trade · max ${prof.maxOpen} open`, 'restarts the count unless risk goes down'], ['Plan limits', `max ${st.maxNewEntries} entries/day · ${st.maxPlanItems} tickets`, 'restarts the count unless risk goes down'], ['Selection threshold', `P(win) ≥ ${(BT.S.learningConfig && BT.S.learningConfig.minPWin) || 0.52}`, 'restarts the count'], ['Smart rules (gate, throttle, windows)', 'as stored', 'restarts the count'], ['Cost sheet', 'stored sheet v3', 'noted only'], ['Review cutoff', st.reviewCutoff, 'noted only (irrelevant to the count)'], ['Engine and schema version', 'engine 0.7 · schema 41', 'noted only']];
    body.innerHTML = `<div class="grid g2">
      ${K.card('Configuration in force (fingerprint)', K.table(['Component', 'Value', 'If it changes'], comp.map(([a, b, c]) => [esc(a), esc(b), esc(c)]), { stack: true }) + `<div class="card-b small muted">Fingerprint <span class="mono">9f3c…e21a</span> captured with the last paper-capture run (${ago(Date.now() - 3 * 3600000)}). Detection, labelling and the restart of the count only: the system never blocks or reverts a change.</div>`, '', 'flush')}
      <div style="display:grid;gap:16px">
        ${K.card('Differences not yet captured', `<div class="card-b">${K.table(['Component', 'Change', 'Effect'], [['Selection threshold', 'P(win) 0.52 → 0.54 (hypothesis H4 accepted)', '<b>would restart the count</b>']])}<div class="small muted" style="margin-top:6px">Preview only. Nothing was applied; applying it starts a new epoch and the 60-trade count begins again.</div></div>`)}
        ${K.card('Epochs', K.table(['Epoch', 'Started', 'Counted', 'Status'], [['first', epochStart(), counted().length + ' of 60', 'measuring']]), '', 'flush')}
        ${K.card('Queue: accepted hypotheses waiting', `<div class="card-b">${K.table(['Hypothesis', 'Cell', 'Knob', 'Accepted'], [['H4', 'ORB 15m BUY', 'min P(win) 0.52 → 0.54', '3 days ago']])}<div class="small muted" style="margin-top:6px">Accepted means judged better offline; nothing was applied; applying it starts a new epoch. The system never applies, proposes to apply, or reorders this queue.</div></div>`)}
      </div></div>
      ${K.evidence({ basis: 'configuration components read from existing stores', population: 'the single paper epoch', limits: ['A fingerprint covers the engine and schema versions, not the code revision.', 'Trades around a change are kept out of the count (straddle rule).', 'There is no apply or revert control on this tab.'] })}`;
  }

  /* ---------------- Phase check (P10-06) ---------------- */
  function phase(body) {
    const n = counted().length;
    const crit = [['C1', 'Duration and coverage', 'NOT EVALUABLE', `week 6 of 12 (84 days needed; coverage is judged at day 84)`], ['C2', 'Counted trades', 'NOT EVALUABLE', `${n} of 60 (never NOT MET for being short)`], ['C3', 'Mean R inside the backtest band, after costs', 'NOT EVALUABLE', `needs 60 counted trades and a confirmed charge sheet (now n ${n}; sheet not confirmed)`], ['C4', 'Drawdown within the 90th percentile', 'NOT EVALUABLE', `needs 60 counted trades (now n ${n})`], ['I', 'Capture integrity', 'MET', 'no integrity mismatch in 29 capture runs; no armed ticket without an outcome for more than 3 sessions']];
    body.innerHTML = `<div class="banner info">${I('clipboard')}<div><b>Overall: NOT EVALUABLE.</b> MET prints only with 60 counted trades, 84 days, a confirmed charge sheet and one unchanged configuration. This tab only reads; it starts and changes nothing.</div></div>
      ${K.card('Exit criteria of P10', K.table(['', 'Criterion', 'Verdict', 'Fact'], crit.map(([a, b, v, d]) => [a, esc(b), `<span class="st">${I(v === 'MET' ? 'checkCircle' : 'help')}<b>${v}</b></span>`, esc(d)]), { stack: true }), '', 'flush')}
      <div class="grid g2" style="margin-top:16px">
        ${K.card('Never judged here', '<div class="card-b"><ul class="insights"><li>No look-ahead: <b>NOT EVALUABLE</b> here, proved by the P3-08 leak tests</li><li>Paper equals grading for the same ticket: P10-01 equivalence test</li><li>Determinism and the no-order rule: architecture tests</li><li>Real-fill behaviour: P11</li></ul></div>')}
        ${K.card('Week lines', K.table(['Week', '>Sessions', '>Counted', '>Cumulative R'], [...new Set(counted().map((t) => isoWeek(t.date)))].sort().map((w, i, arr) => { const up = counted().filter((t) => isoWeek(t.date) <= w); return [w, 5, counted().filter((t) => isoWeek(t.date) === w).length, R(sum(up, (t) => t.R - CHARGE_R))]; })), '', 'flush')}
      </div>
      ${K.evidence({ basis: 'stored paper records only', population: 'current epoch (first)', n, limits: ['Simulated fills on official 1-minute bars with no real execution.', 'About 2.1 years of history and no hold-out behind the cells.', '60 trades give an interval of about ±0.3R, so a pass is weak evidence.', 'The P7 phase check and the owner\'s review remain separate.'] })}`;
  }

  /* ---------------- Live (P11) — placeholder states ---------------- */
  const RAMP = [['S1', 0.25], ['S2', 0.5], ['S3', 1.0]];
  const LTABS = [['fills', 'Fills (P11-01)'], ['slip', 'Slippage report (P11-03)'], ['ramp', 'Ramp and halts (P11-04)']];
  function live(body) {
    const la = BT.S.accounts.find((a) => a.roles.trading.enabled && a.roles.trading.mode === 'manual_live');
    const intro = K.phase(`<b>Phase P11, off by default.</b> You place every order by hand at the broker. The system only reads your fills afterwards (read only), grades them, and sizes the next day at <b>25% of the intended risk</b> until you raise the step on evidence. No order is placed by this application.`);
    const seg = `<div class="seg" id="lv-seg" style="margin-bottom:14px">${LTABS.map(([k, l]) => `<button data-lv="${k}" class="${pp.live === k ? 'on' : ''}">${l}</button>`).join('')}</div>`;
    let inner = '';
    if (!la) inner = K.st('empty', 'no account is in manual_live mode. Set it in Accounts, Trading; paper to manual_live is not gated, its evidence is shown beside the control.', true);
    else if (pp.live === 'fills') inner = liveFills(la);
    else if (pp.live === 'slip') inner = liveSlip(la);
    else inner = liveRamp(la);
    body.innerHTML = intro + seg + inner;
    $$('[data-lv]', body).forEach((b) => (b.onclick = () => { pp.live = b.dataset.lv; BT.rerender(); }));
    const meta = { 'live.auto_ramp_up': { label: 'Auto ramp-up to S2', on: 'The rule may raise the step to S2 by itself when every gate passes. It never goes above S2 and never to S3.' } };
    K.bindSwitches(body, meta);
    if ($('#rp-s2')) $('#rp-s2').onclick = () => BT.toast('Refused: gates G1 to G6 are not all pass (live.gates_not_met)', 'bad');
  }
  function liveFills(la) {
    return K.card(`Capture health · ${esc(la.name)}`, `<div class="card-b" style="display:grid;gap:10px"><div>${K.st('unavailable', 'capture has not run: the broker read of the trade book and order book is off (allow-list entry is an owner security act)', true)}</div>
      <div>${K.table(['Session', 'Capture', 'Tickets matched', 'Reason'], [['—', K.st('empty', 'no session captured'), '—', 'nothing to match yet']])}</div>
      <div class="small muted">When it runs: each approved ticket is matched to your fills with one of exact, differs, ambiguous or unmatched; a missed ticket is recorded as missed, never as an error. Nothing is written to the broker.</div></div>`)
      + K.evidence({ basis: 'your fills read from the broker after the session', population: `account ${esc(la.name)}`, limits: ['Read only: the order endpoint is refused by the allow-list.', 'A fill with no plan levels is shown in rupees with no R.'] });
  }
  function liveSlip(la) {
    const bins = ['< −10', '−10 to −5', '−5 to −2', '−2 to 0', '0 to 2', '2 to 5', '5 to 10', '10 to 20', '≥ 20'];
    return `<div class="grid g2">${K.card('Live slippage distribution (basis points)', `<div class="card-b" style="display:grid;gap:8px">${K.st('insufficient', 'n 0 of at least 10 entries with one reference kind: no chart, no mean', true)}<div class="small muted">Fixed bins: ${bins.join(' · ')}. A bin with 0 would be drawn as 0, never hidden.</div></div>`)}
      ${K.card('Live versus paper on the same tickets', `<div class="card-b" style="display:grid;gap:8px"><div>Paired tickets: <b>0</b> ${K.st('empty', 'no pair yet')}</div>${K.table(['Exclusion', '>Count'], [['live_only', 0], ['paper_only', 0], ['live_open / paper_open', 0], ['live_uncertain', 0], ['no_plan_levels', 0]])}<div class="small muted">Gross and net are never mixed in one mean; the gross comparison is shown when charges are not known on both sides.</div></div>`)}</div>
      ${K.card('Execution gap by strategy (plan R − missed − entry − exit − charges = actual)', K.table(['Strategy', '>Plan R', '>Missed triggers', '>Entry slippage', '>Exit difference', '>Charges', '>Actual R'], [['all', ...Array(6).fill(K.st('unavailable', 'no graded live round trip'))], ['Overrides', K.st('unavailable', 'measured by the decision ledger, not here'), '', '', '', '', '']], { stack: true }), '', 'flush')}
      ${K.evidence({ basis: 'gross until charges are known on both sides', population: `live ${esc(la.name)} paired with the paper account, current epoch`, n: 0, limits: ['Groups under 5 tickets show sums with n and no per-ticket mean.', 'Paper epochs are never pooled.', 'The tab refuses to render without this block; it makes no write call.'] })}`;
  }
  function liveRamp(la) {
    const prof = BT.profileOf(la), cap = la.roles.trading.capital;
    const step = 0; // S1
    const intended = prof.riskPct, liveR = intended * RAMP[step][1];
    const gates = [['G1', 'Sessions since the step', 'insufficient', '0 of 20 trading sessions'], ['G2', 'Graded round trips', 'insufficient', '0 of 20 (40 for S3); uncertain trips ≤ 10%'], ['G3', 'Slippage', 'not_evaluable', 'n 0 of 20; mean ≤ 0.05R and one-sided 95% upper bound ≤ 0.10R'], ['G4', 'Discipline', 'unavailable', 'needs order evidence: capture not running'], ['G5', 'Containment', 'unavailable', 'drawdown of closed live trades is not measurable yet'], ['G6', 'Capture health', 'unavailable', 'capture complete on ≥ 95% of sessions: no session captured']];
    return `<div class="grid g2">${K.card('Risk step', `<div class="card-b" style="display:grid;gap:10px"><div class="ladder-modes" style="grid-template-columns:repeat(3,minmax(0,1fr))">${RAMP.map(([s, f], i) => `<div class="rung ${i === step ? 'on' : ''}"><b>${s}</b>${f * 100}% of intended</div>`).join('')}</div>
        <div>Intended risk <b>${intended.toFixed(3)}%</b> · live risk <b>${liveR.toFixed(4)}%</b> = ${inr(cap * liveR / 100, 2)} per 1R on ${inr(cap)}.</div>
        <div class="small muted">Account ${esc(la.name)}. Eligibility: ${K.st('not_evaluable', 'every gate must be pass; none is')}</div>
        <div class="row"><button class="btn" id="rp-s2" aria-describedby="rp-why">Move to S2</button><button class="btn" disabled>Clear hold</button><button class="btn" disabled>Step down</button></div>
        <div class="small muted" id="rp-why">Move to S2 is refused while any gate is not pass. Raising a step needs your password and a reason; stepping down needs only the session. S3 additionally needs the P11-06 report MET and your recorded exit confirmation.</div></div>`)}
      ${K.card('Gates G1 to G6 (S1 to S2)', K.table(['', 'Gate', 'State and reason'], gates.map(([a, b, s, r]) => [a, esc(b), K.st(s, r)]), { stack: true }), '', 'flush')}</div>
      <div class="grid g2" style="margin-top:16px">
        ${K.card('Loss halts', `<div class="card-b" style="display:grid;gap:8px"><dl class="kv"><dt>Daily limit</dt><dd>${prof.dailyLossPct}% of capital = ${inr(cap * prof.dailyLossPct / 100)} (stops new approvals and invalidates unfilled tickets)</dd><dt>Weekly limit</dt><dd>5 R · action: halve the next week's size (profile setting)</dd><dt>Open halts</dt><dd>${K.st('empty', 'none')}</dd><dt>Today's loss</dt><dd>${K.st('unavailable', 'no realised data: capture gap. This fails account_live_ready and never reads as "no loss".')}</dd></dl></div>`)}
        ${K.card('Auto option', `<div class="card-b flush">${K.switchRow({ id: 'live.auto_ramp_up', label: 'Auto ramp-up to S2', desc: 'Setting OFF: eligible shows as "eligible" and waits for you. ON: the rule steps up by itself when every gate passes, recorded as automatic with the settings snapshot, never above S2.' })}</div>`)}
      </div>
      ${K.card('Ramp history', `<div class="card-b">${K.st('empty', 'no ramp event yet. The first step event is yours: set S1 on the account.')}</div>`)}
      ${K.evidence({ basis: 'rules over your captured fills only', population: `account ${esc(la.name)}`, limits: ['The system sizes and warns; you place every order.', 'insufficient, unavailable and not_evaluable never count as a pass.', 'A halt does not touch positions already open.'] })}`;
  }

  /* =====================================================================================
     EXECUTION   P12-07 modes · P12-08 confirm ticket · P12-10 screens · P12-11 readiness
     "Phase P12, off by default"; nothing here places an order.
     ===================================================================================== */
  const EX = { tab: 'today', seen: '', filt: { acc: 'all', grp: 'all' }, ticket: null, tickAt: 0, tickState: null };
  const ETABS = [['today', 'Today'], ['orders', 'Orders'], ['positions', 'Positions'], ['activity', 'Activity'], ['modes', 'Modes']];
  const LADDER = ['paper', 'manual_live', 'assisted', 'semi_auto', 'auto_live'];
  const SIMS = [['off', 'Off (default)'], ['on', 'On, semi_auto account'], ['notready', 'Not ready (token)'], ['halted', 'Halted'], ['missing', 'Protection missing']];
  const ex = () => { const p = K.P(); p.exec = p.exec || { sim: 'off', modes: {}, req: {}, drillAt: null, halt: null, eligible: false, limits: { orders: 6, notional: 150000, loss: 1500 }, unlocked: false }; return p.exec; };
  const effMode = (a) => ex().modes[a.id] || a.roles.trading.mode;
  const tradingAccs = () => BT.S.accounts.filter((a) => a.roles.trading.enabled);
  const semiAcc = () => tradingAccs().find((a) => effMode(a) === 'semi_auto');
  const anyAutomated = () => ex().sim === 'on' || ex().sim === 'missing' || ex().sim === 'halted' || ex().sim === 'notready';
  const halted = () => !!ex().halt || ex().sim === 'halted';
  BT.exec = { sim: () => ex().sim, halted, anyAutomated };

  function missingBanner() {
    return ex().sim === 'missing' ? `<div class="banner warn" role="alert">${I('xCircle')}<div><b>Position without confirmed protection:</b> TATAPOWER BUY 120 has no stop confirmed at the broker (protection <b>missing</b>). Open Positions. Nothing is placed or moved by this banner.</div></div>` : '';
  }
  BT.pages.execution = function (el) {
    K.hashTab(EX, ETABS);
    const e = ex(), off = e.sim === 'off';
    const sim = `<div class="simbar" role="group" aria-label="Prototype only: simulate the state">${I('lightbulb')} <span>Prototype only, pretend:</span>${SIMS.map(([k, l]) => `<button data-xsim="${k}" class="${e.sim === k ? 'on' : ''}">${l}</button>`).join('')}</div>`;
    el.innerHTML = K.head('Execution', 'What the system is allowed to do, what it did, what waits for you, what could hurt, and one deliberate way to stop it.', ['P12-07', 'P12-08', 'P12-10', 'P12-11'])
      + K.phase('<b>Phase P12, off by default.</b> Modes climb one step at a time: <b>paper → manual_live → assisted → semi_auto → auto_live</b>. Until an account is promoted by you (password, cooling-off, evidence) nothing is sent. This prototype never sends an order; every order text below is a design of the screen.')
      + sim + missingBanner() + K.tabs(ETABS, EX.tab, 'Execution') + '<div id="ex-body" class="fade-swap"></div>';
    $$('[data-xsim]', el).forEach((b) => (b.onclick = () => { e.sim = b.dataset.xsim; if (e.sim === 'on' || e.sim === 'missing') e.modes[(tradingAccs().find((a) => a.roles.trading.mode === 'manual_live') || tradingAccs()[0]).id] = 'semi_auto'; else if (off) { /* keep */ } if (e.sim === 'off') { e.modes = {}; e.halt = null; EX.ticket = null; } BT.save(); BT.renderTopStatus(); BT.rerender(); }));
    K.bindTabs(el, (t) => { EX.tab = t; BT.rerender(); });
    const f = $('#ex-body', el);
    ({ today: exToday, orders: exOrders, positions: exPositions, activity: exActivity, modes: exModes }[EX.tab] || exToday)(f);
    f.insertAdjacentHTML('beforeend', K.evidence({ basis: 'our records and the broker\'s order stream; marks at the last price; costs not included; risk to stop assumes the stop fills at its price', population: 'execution tables (insert-only) of the days shown', more: [['Last broker read', off ? K.st('unavailable', 'execution is off: nothing is read') : '09:41:07 IST'], ['Reconciliation today', off ? K.st('not_evaluable', 'not run') : 'matched']], limits: ['A view older than 30 s while the market is open reads "Last updated, the numbers may be old" and greys dependent controls.', 'No token, client id, secret or balance field is ever returned to this screen.'] }));
  };

  /* ----- readiness card + morning checklist (P12-11) ----- */
  function readinessHtml() {
    const e = ex(), sim = e.sim;
    const bad = sim === 'notready';
    const checks = sim === 'off' ? [['connected', 'unavailable', 'execution is off: nothing is probed'], ['token_covers_session', 'unavailable', 'not read'], ['token_live', 'unavailable', 'not read'], ['ip_configured', 'unavailable', 'exec.expected_static_ip is empty'], ['ip_attested', 'fail', 'no attestation of the static IP on file'], ['feed', 'unavailable', 'feed is not subscribed for execution']]
      : [['connected', 'pass', 'Dhan connection connected'], ['token_covers_session', bad ? 'fail' : 'pass', bad ? 'token expires at 15:00 today: log in again between 16:00 yesterday and 09:00 today' : 'expires 17:00 tomorrow; remaining 7 h 50 min'], ['token_live', bad ? 'fail' : 'pass', bad ? 'the broker refused the token (DH-901)' : 'broker profile answered 3 min ago'], ['ip_configured', 'pass', 'primary static IP equals the expected one (read 6 min ago)'], ['ip_attested', 'pass', 'attested 12 days ago, orders allowed'], ['feed', 'pass', 'feed connected']];
    const w = { pass: ['checkCircle', 'Pass'], fail: ['xCircle', 'Fail'], unavailable: ['ban', 'Unavailable'] };
    const verdict = checks.every((c) => c[1] === 'pass') ? 'ready' : sim === 'off' ? 'unavailable' : 'not_ready';
    return K.card('Readiness · can execution act today?', `<div class="card-b" style="display:grid;gap:10px"><div><span class="st block" style="font-size:14px">${I(verdict === 'ready' ? 'checkCircle' : verdict === 'unavailable' ? 'ban' : 'xCircle')}<b>${verdict === 'ready' ? 'Ready' : verdict === 'unavailable' ? 'Unavailable' : 'Not ready'}</b><span>${verdict === 'ready' ? 'recorded at 09:10:30 IST' : verdict === 'unavailable' ? 'execution is off' : 'recorded at 09:10:30 IST, fail closed: no new order is created'}</span></span></div>
      <div>${checks.map(([c, s, t]) => `<div class="chk-line">${I(w[s][0])}<div><b class="mono">${c}</b> · ${w[s][1]} <span class="muted">— ${esc(t)}</span></div></div>`).join('')}</div>
      <dl class="kv"><dt>Token ends</dt><dd>${sim === 'off' ? K.st('unavailable', 'no token read') : bad ? 'today 15:00 IST' : 'tomorrow 17:00 IST'}</dd><dt>IP can be changed from</dt><dd>${sim === 'off' ? K.st('unavailable', 'not read') : '14 Nov 2026 (seven-day broker rule)'}</dd></dl>
      <div class="row"><button class="btn sm" id="rd-now" ${sim === 'off' ? 'disabled' : ''}>Check now</button><a class="btn sm" href="#/accounts">Connect Dhan</a><button class="btn sm" id="rd-att">Record static IP attestation</button></div>
      <div class="small muted">A reminder appears in the bell at 19:00 the evening before when the token will not cover tomorrow's cut-off, and at 08:45 and 09:05 on the day. Nothing leaves this machine.</div></div>`)
      + K.card('Morning checklist', `<div class="card-b">${[['1', 'Connect Dhan', sim !== 'off' && !bad], ['2', 'Unlock today (password)', ex().unlocked], ['3', 'Plan approved', BT.S.plan.items.some((i) => ['approved', 'modified', 'armed'].includes(i.status))], ['4', 'Feed connected', sim !== 'off']].map(([n, t, d]) => `<div class="chk-line">${I(d ? 'checkCircle' : 'minus')}<div><b>${n}. ${t}</b> <span class="muted">— ${d ? 'done' : 'not yet'}</span></div></div>`).join('')}<div class="row" style="margin-top:8px"><button class="btn sm" id="ex-unlock" ${sim === 'off' || bad ? 'disabled' : ''}>${ex().unlocked ? 'Unlocked until 15:30' : 'Unlock today'}</button></div></div>`);
  }
  function bindReadiness(body) {
    const n = $('#rd-now', body); if (n) n.onclick = () => BT.toast('Readiness probe requested (command readiness_now); result in a second', '');
    const a = $('#rd-att', body); if (a) a.onclick = async () => {
      const r = await K.ownerAct({ title: 'Record static IP attestation', lead: 'You confirm that the broker has this static IP set for orders. The system never contacts the broker for this; it only stores your statement with the time.', fields: '<label class="field">Static IP<input class="input mono" id="at-ip" value="203.0.113.24"></label><label class="check"><input type="checkbox" id="at-ok" checked> Orders are allowed from this IP</label><ol class="small muted" style="margin:0;padding-left:18px"><li>Log in to the broker site</li><li>Open the API settings</li><li>Set the primary static IP</li><li>Wait for the broker to confirm</li><li>Record it here</li></ol>', validate: (ov) => (/^[0-9a-f:.]+$/i.test($('#at-ip', ov).value) ? '' : 'Enter a valid IPv4 or IPv6 address.') });
      if (r) { BT.toast('Attestation recorded (insert-only)', 'good'); }
    };
    const u = $('#ex-unlock', body); if (u) u.onclick = async () => { if (ex().unlocked) return; const r = await K.ownerAct({ title: 'Unlock today for confirmations', lead: 'Without a day unlock a confirm is refused. The unlock expires at 15:30 IST. Locking the session does not remove it, but a confirm still needs the live session.', reason: false, okLabel: 'Unlock today' }); if (r) { ex().unlocked = true; BT.save(); BT.rerender(); } };
  }

  /* ----- Today tab ----- */
  function exToday(body) {
    const e = ex(), sim = e.sim, off = sim === 'off';
    const acc = semiAcc() || tradingAccs()[0];
    const hand = sim !== 'off';
    const cap = (l, v, c, st, reason, pctU) => `<div class="cap"><div class="l">${l}</div>${st === 'value' ? `<div class="v">${v}</div><div class="small muted">of ${c}</div><div class="meter" role="meter" aria-valuenow="${Math.round(pctU)}" aria-valuemin="0" aria-valuemax="100" aria-label="${l}"><i style="width:${Math.min(100, pctU)}%"></i></div>${pctU >= 80 ? `<div class="small" style="margin-top:4px">${I('alert')} near the cap</div>` : ''}` : `<div class="v">${K.st(st, reason)}</div>`}</div>`;
    const mode = acc ? effMode(acc) : '—';
    body.innerHTML = (off ? `<div class="card"><div class="card-b" style="display:grid;gap:10px">${K.st('empty', '', true).replace('<b>Empty</b>', '<b>Execution is off</b>')}
          <ul class="insights"><li>Service <b>xd-exec</b>: not running (the service is paused by default)</li><li>Environment: <b>none</b> (no sandbox or live environment is set)</li>${tradingAccs().map((a) => `<li>${esc(a.name)}: effective mode <b>${effMode(a).replace('_', ' ')}</b>${effMode(a) === 'manual_live' ? '; next gate manual_live → assisted is not eligible: ' + 'execution_enabled is OFF, P11-06 verdict is not evaluable' : '; paper to manual_live is not gated'}</li>`).join('')}</ul>
          <div class="small muted">Nothing is drawn as healthy. The other tabs show empty states. ${K.spec('P12-10')}</div></div></div>` : `<div class="tiles"><div class="tile"><div class="l">Effective mode</div><div class="v" style="font-size:18px">${esc(mode.replace('_', ' '))}</div><div class="s">${acc ? esc(acc.name) : ''} · from 09:00 IST on 21 Oct (insert-only mode trail)</div></div>
          <div class="tile"><div class="l">Trading state</div><div class="v" style="font-size:18px">${halted() ? 'Trading stopped' : sim === 'notready' ? 'Not ready' : 'Armed, watching'}</div><div class="s">${halted() ? 'halt level ' + (e.halt ? e.halt.level : 'cancel') : 'no halt open'}</div></div>
          <div class="tile"><div class="l">Open tickets</div><div class="v">${EX.ticket ? 1 : 0}</div><div class="s">awaiting your tap</div></div>
          <div class="tile"><div class="l">Alarms</div><div class="v">${sim === 'missing' ? 1 : 0}</div><div class="s">${sim === 'missing' ? 'protection missing' : 'none'}</div></div></div>`)
      + K.card('Caps in force (the sheet in force, not the pending one)', `<div class="card-b"><div class="caps-grid">${off ? ['Orders used', 'Notional used', 'Worst case against the daily loss cap', 'Open positions'].map((l) => `<div class="cap"><div class="l">${l}</div><div class="v">${K.st('unavailable', 'execution is off')}</div></div>`).join('') : [cap('Orders used', '3', `${e.limits.orders} orders`, 'value', '', 3 / e.limits.orders * 100), cap('Notional used', inr(148446, 2), inr(e.limits.notional, 2), 'value', '', 148446 / e.limits.notional * 100), cap('Worst case at the stops', inr(1470, 2), inr(e.limits.loss, 2), 'value', '', 1470 / e.limits.loss * 100), cap('Open positions', '2', '5 positions', 'value', '', 40)].join('')}</div>
          <div class="small muted" style="margin-top:8px">A meter whose input is unavailable shows the word and reason, never 0. A pending sheet shows beside this one with its effective-from time.</div></div>`)
      + '<div class="grid g2" style="margin-top:16px"><div style="display:grid;gap:16px;align-content:start">' + ticketHtml() + (hand ? K.card('Last actions', K.table(['Time', 'What happened'], [['09:41:07', 'Placed BUY 120 TATAPOWER at LIMIT 412.35 because you tapped confirm on ticket T-114 (one super order with stop and target).'], ['09:41:09', 'Stop-loss 405.10 and target 425.80 confirmed at the broker (protected).'], ['09:12:30', 'Pre-open check armed 3 tickets, invalidated 1 (gap beyond 0.5 ATR).']]), '', 'flush') : '') + '</div><div style="display:grid;gap:16px;align-content:start">' + readinessHtml() + '</div></div>';
    bindReadiness(body); bindTicket(body);
  }

  /* ----- semi-auto confirm ticket (P12-08) ----- */
  function ticketHtml() {
    const e = ex(), sim = e.sim;
    if (sim === 'off') return K.card('Order waiting for your tap', `<div class="card-b">${K.st('empty', 'No order is waiting. In semi_auto, a ticket appears here and in the bell when a trigger fires and every pre-trade check passes.', true)}</div>`);
    const t = EX.ticket, st = EX.tickState;
    if (!t && !st) return K.card('Order waiting for your tap', `<div class="card-b" style="display:grid;gap:10px">${K.st('empty', 'No order is waiting', true)}<button class="btn" id="tk-issue">Prototype only: issue a ticket (trigger fired, checks passed)</button></div>`);
    const left = Math.max(0, Math.ceil((EX.tickAt + 90000 - Date.now()) / 1000));
    const text = 'BUY 120 TATAPOWER at LIMIT Rs 412.35 (NSE, intraday), valid today. Stop-loss Rs 405.10 and target Rs 425.80 are placed at the broker with the order (one super order). Notional Rs 49,482.00. Worst case at the stop Rs 870.00 (120 x 7.25). Account: ' + esc((semiAcc() || tradingAccs()[0]).name) + '. Plan ticket approved by you on 23 Oct at 19:41. This card expires at ' + fmtTime(EX.tickAt + 90000).slice(0, 8) + '.';
    if (st) {
      const m = { confirmed: ['checkCircle', 'Confirmed. Waiting for the broker.', 'In this prototype no order is sent. In the real system one send is made, exactly once, from the stored text.'], skipped: ['minus', 'Skipped.', 'Counted in the discipline report as declined, not as an error.'], expired: ['clock', 'Expired.', 'Nothing was sent. A new ticket with new text may be issued once while the window is open.'], refused: ['ban', 'Refused: execution.price_moved', 'The price moved 0.301% from the reference (limit 0.30%). Nothing was sent.'] }[st];
      return K.card('Order waiting for your tap', `<div class="card-b" style="display:grid;gap:10px"><div class="st block">${I(m[0])}<b>${m[1]}</b><span>${m[2]}</span></div><button class="btn" id="tk-new">Prototype only: issue another</button></div>`);
    }
    return K.card('Order waiting for your tap', `<div class="card-b"><div class="confirm-card" aria-label="Order ticket">
      <div class="row between"><b>Ticket T-114 · semi_auto</b><span class="countdown" role="timer" aria-live="off" id="tk-cd">${left} s</span></div>
      <div class="order-text" id="tk-text">${text}</div>
      <div class="small muted">Shown verbatim from the stored parameters, never re-typed. The tap checks the text hash, the price drift (0.30%), the mode, the halt state and readiness; the database clock decides the expiry, not this browser.</div>
      <label class="field" id="tk-pwrow">Password (notional Rs 49,482 is at least 10% of the account's Rs 400,000 capital)<input class="input" type="password" id="tk-pw" value="demo-password" autocomplete="current-password"></label>
      <div class="err" id="tk-err" role="alert"></div>
      <div class="row"><button class="btn primary" id="tk-ok" disabled aria-describedby="tk-why">Place this order at Dhan</button><button class="btn" id="tk-skip">Skip</button><button class="btn sm ghost" id="tk-exp">Prototype only: let it expire</button></div>
      <div class="small muted" id="tk-why">The confirm button wakes 800 ms after the card first appears, so a stray key press cannot place it. The card never takes focus, never opens over the page, and ignores Enter unless the button itself has focus.</div></div></div>`);
  }
  let tkTimer = null;
  function bindTicket(body) {
    const issue = (e) => { EX.ticket = true; EX.tickState = null; EX.tickAt = Date.now(); BT.renderTopStatus(); BT.rerender(); };
    ['tk-issue', 'tk-new'].forEach((i) => { const b = $('#' + i, body); if (b) b.onclick = issue; });
    const ok = $('#tk-ok', body);
    if (!ok) return;
    const arm = Date.now();
    setTimeout(() => { if ($('#tk-ok')) $('#tk-ok').disabled = false; }, 800);
    clearInterval(tkTimer);
    tkTimer = setInterval(() => { const cd = $('#tk-cd'); if (!cd) return clearInterval(tkTimer); const left = Math.max(0, Math.ceil((EX.tickAt + 90000 - Date.now()) / 1000)); cd.textContent = left + ' s'; if (left <= 0) { EX.ticket = null; EX.tickState = 'expired'; BT.renderTopStatus(); BT.rerender(); } }, 500);
    const end = (s) => { EX.ticket = null; EX.tickState = s; clearInterval(tkTimer); BT.renderTopStatus(); BT.rerender(); };
    ok.onclick = () => {
      if (!ex().unlocked) { $('#tk-err').textContent = 'Refused: execution.not_unlocked. Unlock today first (password), then confirm.'; return; }
      if (D.hash($('#tk-pw').value) !== BT.S.auth.pw) { $('#tk-err').textContent = 'Wrong password (counts toward the sign-in lockout).'; return; }
      BT.audit('owner', 'Confirm ticket (prototype)', 'Design only: no order is sent'); end('confirmed');
    };
    $('#tk-skip').onclick = () => end('skipped');
    $('#tk-exp').onclick = () => end('expired');
  }

  /* ----- Orders (blotter) ----- */
  const STATE_W = { planned: ['clipboard', 'Planned'], armed: ['eye', 'Armed'], checking: ['shieldCheck', 'Checking'], rejected: ['xCircle', 'Rejected'], submitting: ['send', 'Submitting'], open: ['clock', 'Open'], filled: ['checkCircle', 'Filled'], protected: ['shield', 'Protected'], managing: ['sliders', 'Managing'], closing: ['minus', 'Closing'], closed: ['checkCircle', 'Closed'], reconciled: ['checkCircle', 'Reconciled'], cancelled: ['ban', 'Cancelled'], failed: ['xCircle', 'Failed'] };
  function exOrders(body) {
    const sim = ex().sim;
    const rows = sim === 'off' ? [] : [
      ['09:41:07', 'Dhan · Swing', 'TATAPOWER', 'BUY', 120, 'LIMIT 412.35', 'protected', 'tap', 'XD2610250000A1E1', '1100…4471', '412.35 (+0.00 ticks)', ''],
      ['09:44:20', 'Dhan · Swing', 'DIXON', 'BUY', 12, 'LIMIT 14,820.00', 'rejected', 'system', 'XD2610250000B2E1', '—', 'empty: not filled', 'sector_cap (soft), funds_available (hard)'],
      ['10:02:51', 'Dhan · Swing', 'TRENT', 'SELL', 40, 'LIMIT 5,210.00', 'MYSTERY', 'owner typed', 'XD2610250000C3E1', '—', '—', ''],
    ];
    body.innerHTML = `<div class="row" style="margin-bottom:12px"><label class="field" style="flex-direction:row;align-items:center;gap:6px">Date<input class="input" type="date" value="${K.lastDay()}" style="width:auto"></label>
      <select class="input" style="width:auto" aria-label="Account"><option>All accounts</option>${tradingAccs().map((a) => `<option>${esc(a.name)}</option>`).join('')}</select><select class="input" style="width:auto" aria-label="State group"><option>All states</option><option>Working</option><option>Rejected</option><option>Done</option></select></div>
      ${K.card('Blotter (read only)', rows.length ? K.table(['Time', 'Account', 'Symbol', 'Side', '>Qty', 'Type / price', 'State', 'By', 'Client id', 'Broker id', 'Fill vs ticket', 'Rejection'], rows.map((r) => { const s = STATE_W[r[6]]; return [r[0], esc(r[1]), `<b>${r[2]}</b>`, r[3], r[4], r[5], s ? `<span class="st">${I(s[0])}<b>${s[1]}</b></span>` : `<span class="st" style="border-color:var(--bad);color:var(--bad)">${I('xCircle')}<b>Unknown state: ${esc(r[6])}</b></span>`, r[7], `<span class="mono">${r[8]}</span>`, r[9], r[10].startsWith('empty') ? K.st('empty', 'unfilled') : esc(r[10]), esc(r[11])]; }), { stack: true }) : `<div class="card-b">${K.st('empty', 'no order today. Orders appear here only after an account is promoted and a ticket is confirmed or sent.', true)}</div>`, '', 'flush')}
      <div class="small muted" style="margin-top:8px">No control on this tab can send, change or cancel an order. States use the order state machine's words; an unknown state is shown as an error, never hidden.</div>`;
  }
  function exPositions(body) {
    const sim = ex().sim;
    const rows = sim === 'off' ? [] : [['TATAPOWER', 120, '412.35', '413.10', 90, '870.00', sim === 'missing' ? 'missing' : 'confirmed', 'ours', 'matched'], ['INFY', 30, '1,502.00', '—', null, '—', 'unknown', 'foreign', 'break']];
    const pw = { confirmed: ['checkCircle', 'Confirmed at the broker'], missing: ['xCircle', 'Missing'], unknown: ['alert', 'Unknown'] };
    body.innerHTML = K.card('Positions (read only)', rows.length ? K.table(['Instrument', '>Net qty', '>Average', '>Last', '>Unrealised', '>Risk to stop', 'Protection', 'Source', 'Reconciliation'], rows.map((r) => [`<b>${r[0]}</b>`, r[1], r[2], r[3] === '—' ? K.st('unavailable', 'price older than the feed limit') : r[3], r[4] == null ? K.st('unavailable', 'no fresh price') : inr(r[4]), r[5], `<span class="st">${I(pw[r[6]][0])}<b>${pw[r[6]][1]}</b></span>`, r[7], `<span class="st">${I(r[8] === 'matched' ? 'checkCircle' : 'alert')}<b>${r[8] === 'matched' ? 'Matched' : 'Break'}</b></span>`]), { stack: true }) : `<div class="card-b">${K.st('empty', 'no position. A position with missing protection would also appear in an error banner on every tab.', true)}</div>`, '', 'flush')
      + '<div class="small muted" style="margin-top:8px">Protection shows "confirmed" only when the broker\'s order book shows the stop. "Unknown" has its word and a warning icon, never a green dot.</div>';
  }
  function exActivity(body) {
    const sim = ex().sim;
    const rows = sim === 'off' ? [] : [['09:41:09', 'system', 'Stop-loss 405.10 and target 425.80 confirmed at the broker for TATAPOWER.'], ['09:41:07', 'you (tap)', 'Placed BUY 120 TATAPOWER at LIMIT 412.35 because you confirmed ticket T-114.'], ['09:12:30', 'system', 'Pre-open check armed 3 tickets and invalidated 1.'], ['08:15:00', 'system', 'Reminder: token covers today; nothing to do.'], ['yesterday', 'you', 'Mode of Dhan · Swing moved manual_live to assisted (password, reason on file), effective 09:00 IST.']];
    body.innerHTML = `<div class="row" style="margin-bottom:12px"><select class="input" style="width:auto" aria-label="Kind"><option>All kinds</option><option>Orders</option><option>Alarms</option><option>Halts</option><option>Mode changes</option></select><select class="input" style="width:auto" aria-label="By"><option>By anyone</option><option>You</option><option>System</option></select></div>`
      + K.card('Activity, newest first (the log is insert-only)', rows.length ? K.table(['Time', 'By', 'What'], rows.map((r) => [r[0], r[1], esc(r[2])]), { stack: true }) : `<div class="card-b">${K.st('empty', 'nothing has happened: execution is off', true)}</div>`, '', 'flush') + '<div class="small muted" style="margin-top:8px">50 per page. Every automatic send reads as plain language with its reason; nothing is rewritten after the fact.</div>';
  }

  /* ----- Modes tab (P12-07 ladder, gates, promotion; P12-09 limits) ----- */
  function gateChecks(a) {
    const e = ex(), m = effMode(a), el = e.eligible;
    const ok = (c, t, n) => ({ c, o: el ? 'pass' : t, n });
    if (m === 'paper') return { to: 'manual_live', gated: false, checks: [] };
    if (m === 'manual_live') return { to: 'assisted', gated: true, checks: [
      { c: 'execution_enabled is ON', o: el || K.on('execution_enabled') ? 'pass' : 'fail', t: 'the master switch below is OFF' },
      { c: 'Trading role on and Dhan connection connected', o: 'pass', t: 'connected' },
      { c: 'P11-06 small-live verdict MET', o: el ? 'pass' : 'unavailable', t: 'verdict not evaluable: 0 of 8 live weeks', n: el ? '' : '0 of 8 weeks' },
      { c: 'Sandbox suite passed (record at most 30 days old)', o: el ? 'pass' : 'unavailable', t: 'no sandbox record on file' },
      { c: 'Static-IP attestation at most 30 days old', o: el ? 'pass' : 'fail', t: 'no attestation on file' }] };
    if (m === 'assisted') return { to: 'semi_auto', gated: true, checks: [{ c: '10 sessions in assisted, each with a matched sheet', o: el ? 'pass' : 'unavailable', t: '0 of 10 sessions', n: '0 of 10' }, { c: 'Reconciliation ran with 0 breaks', o: el ? 'pass' : 'unavailable', t: 'not run' }, { c: 'Sandbox record with broker-side stop, ≤ 30 days', o: el ? 'pass' : 'unavailable', t: 'none' }, { c: 'Kill-switch drill ≤ 30 days', o: el || e.drillAt ? 'pass' : 'fail', t: 'no drill on file' }, { c: 'Ready at 09:10 on at least 8 of the last 10 sessions', o: el ? 'pass' : 'unavailable', t: '0 of 10', n: '0 of 10' }] };
    if (m === 'semi_auto') return { to: 'auto_live', gated: true, checks: [{ c: 'P12-12 verdict MET (30 counted sessions, 0 breaks)', o: el ? 'pass' : 'unavailable', t: '0 of 30 sessions', n: '0 of 30' }, { c: 'auto_live_enabled is ON', o: el || K.on('auto_live_enabled') ? 'pass' : 'fail', t: 'switch is OFF' }, { c: 'Limits sheet complete (every cap set, allow-list non-empty)', o: el ? 'pass' : 'fail', t: 'sheet is empty' }, { c: 'Kill-switch drill ≤ 30 days', o: el || e.drillAt ? 'pass' : 'fail', t: 'none' }, { c: 'No demotion in the window', o: 'pass', t: 'none' }] };
    return { to: null, gated: false, checks: [] };
  }
  function exModes(body) {
    const e = ex();
    const accs = tradingAccs();
    const cw = { pass: ['checkCircle', 'Pass'], fail: ['xCircle', 'Fail'], unavailable: ['ban', 'Unavailable'] };
    const meta = { execution_enabled: { label: 'execution_enabled', on: 'Turning the master switch on allows promotion requests. Raising risk waits one closed session after it is saved and needs your password.' }, auto_live_enabled: { label: 'auto_live_enabled', on: 'Allows the auto_live mode to be entered later. It does nothing by itself.' } };
    body.innerHTML = `<div class="banner info">${I('lock')}<div><b>Moving up needs your password twice, at least two sessions apart; moving down is instant.</b> One step at a time; demotion may skip steps. Gates are constants in the code: loosening one is a code change with an ADR, never a setting.</div></div>
      ${accs.map((a) => modeCard(a, gateChecks(a), cw)).join('')}
      <div class="grid g2" style="margin-top:16px">
        ${K.card('Switches (risk direction shown)', `<div class="card-b flush">${K.switchRow({ id: 'execution_enabled', label: 'execution_enabled', desc: 'Master switch. Raising risk: waits one closed session and needs your password. Lowering is immediate.' })}${K.switchRow({ id: 'auto_live_enabled', label: 'auto_live_enabled', desc: 'Raising risk: waits one closed session and needs your password. Lowering is immediate.' })}</div>`)}
        ${K.card('Kill-switch drill', `<div class="card-b" style="display:grid;gap:8px"><div>Last drill: ${e.drillAt ? `${ago(e.drillAt)} (result: all pending orders cancelled in 1.4 s, stops stayed at the broker)` : K.st('empty', 'never run. The ladder needs a drill at most 30 days old.')}</div><div><button class="btn" id="ex-drill" ${K.on('execution_enabled') ? '' : 'disabled'}>Run drill (password)</button> ${K.on('execution_enabled') ? '' : '<span class="small muted">enabled after execution_enabled is on</span>'}</div></div>`)}
      </div>
      ${limitsHtml()}`;
    K.bindSwitches(body, meta);
    $$('[data-req]', body).forEach((b) => (b.onclick = () => requestPromotion(b.dataset.req)));
    $$('[data-cpromo]', body).forEach((b) => (b.onclick = () => confirmPromotion(b.dataset.cpromo)));
    $$('[data-cool]', body).forEach((b) => (b.onclick = () => { e.req[b.dataset.cool].eligibleFrom = Date.now() - 1; BT.save(); BT.rerender(); }));
    $$('[data-down]', body).forEach((b) => (b.onclick = async () => { const a = BT.S.accounts.find((x) => x.id === b.dataset.down); const m = effMode(a), to = LADDER[LADDER.indexOf(m) - 1]; if (await BT.confirmDlg('Move down one step', `${esc(a.name)} moves ${m.replace('_', ' ')} → ${to.replace('_', ' ')} from the next session. No password is needed to lower risk.`, 'Move down')) { e.modes[a.id] = to; BT.audit('owner', 'Mode demoted', `${a.name}: ${m} → ${to}`); BT.save(); BT.rerender(); } }));
    $$('[data-el]', body).forEach((c) => (c.onchange = () => { e.eligible = c.checked; BT.save(); BT.rerender(); }));
    const dr = $('#ex-drill', body); if (dr) dr.onclick = async () => { const r = await K.ownerAct({ title: 'Run the kill-switch drill', lead: 'Runs the stop path against the sandbox environment only and records how long it took. No live order is touched.', okLabel: 'Run drill' }); if (r) { e.drillAt = Date.now(); BT.save(); BT.rerender(); } };
    bindLimits(body);
  }
  function modeCard(a, g, cw) {
    const e = ex(), m = effMode(a), idx = LADDER.indexOf(m), req = e.req[a.id];
    const allPass = g.checks.length && g.checks.every((c) => c.o === 'pass');
    const elig = g.gated ? (allPass ? 'eligible' : 'not_eligible') : 'n/a';
    const coolLeft = req ? req.eligibleFrom - Date.now() : 0;
    return K.card(`${esc(a.name)} · ${m.replace('_', ' ')}`, `<div class="card-b" style="display:grid;gap:12px">
      <div class="ladder-modes" aria-label="Mode ladder">${LADDER.map((x, i) => `<div class="rung ${i === idx ? 'on' : ''} ${i > 1 ? 'future' : ''}"><b>${x.replace('_', ' ')}</b>${i === idx ? 'effective now' : i < idx ? 'below' : i === idx + 1 ? 'next step' : 'later'}</div>`).join('')}</div>
      ${g.to === null ? '<div class="small muted">Top of the ladder.</div>' : !g.gated ? `<div>${K.st('provisional', 'paper → manual_live is not gated. Evidence beside the control: paper phase check NOT EVALUABLE (n ' + counted().length + ' of 60).', true)}<div class="small muted" style="margin-top:6px">Change it in Accounts, Trading (password).</div></div>`
        : `<div><b>Next gate: ${m.replace('_', ' ')} → ${g.to.replace('_', ' ')}</b> · verdict: <b>${elig === 'eligible' ? 'eligible' : 'not eligible'}</b>${K.table(['Check', 'Outcome', 'Text'], g.checks.map((c) => [esc(c.c), `<span class="st">${I(cw[c.o][0])}<b>${cw[c.o][1]}</b></span>`, (c.o === 'pass' && e.eligible ? 'pretend: this fact passes' : esc(c.t) + (c.n ? ` <span class="small muted">(${c.n})</span>` : ''))]), { stack: true })}</div>
        ${req ? `<div class="banner info" style="margin:0">${I('clock')}<div>Promotion requested ${ago(req.at)}: ${coolLeft > 0 ? `cooling off until the close of the second session (<span role="timer" aria-live="off">${Math.ceil(coolLeft / 3600000)} h</span> left in this demo)` : 'cooling-off ended; confirm before the close of the fifth session'}. <button class="btn sm" data-cool="${a.id}">Prototype only: skip two sessions</button></div></div>` : ''}
        <div class="row"><button class="btn primary" data-req="${a.id}" ${elig === 'eligible' && !req ? '' : 'disabled'}>Request promotion</button><button class="btn good" data-cpromo="${a.id}" ${req && coolLeft <= 0 ? '' : 'disabled'}>Confirm promotion</button><button class="btn" data-down="${a.id}" ${idx > 0 ? '' : 'disabled'}>Move down one step</button>
          <label class="check small"><input type="checkbox" data-el ${e.eligible ? 'checked' : ''}> Prototype only: pretend every gate passes</label></div>`}
      ${m === 'assisted' ? assistedSheet() : ''}</div>`);
  }
  function assistedSheet() {
    return `<div class="card"><div class="card-h"><h3>Assisted sheet (you type the order at the broker)</h3></div><div class="card-b" style="display:grid;gap:8px"><div class="order-text" style="padding:10px;border-radius:10px;background:var(--surface-2)">BUY 120 TATAPOWER, LIMIT 412.35, stop-loss 405.10, target 425.80, intraday. Worst case at the stop Rs 870.00 (120 × 7.25). Notional Rs 49,482.00. Client order id XD2610240000A1E1. Valid until 15:00 IST.</div><div class="small muted">The system holds no order-capable call in this mode. After you place it, the sheet is matched to the broker's order: exact, differs (with the fields), ambiguous or unmatched.</div></div></div>`;
  }
  async function requestPromotion(id) {
    const a = BT.S.accounts.find((x) => x.id === id), to = LADDER[LADDER.indexOf(effMode(a)) + 1];
    const r = await K.ownerAct({ title: `Request promotion to ${to.replace('_', ' ')}`, lead: `Stores the evidence snapshot and its hash. It grants nothing: you confirm after the close of the second trading session and before the close of the fifth, and the gate is checked again then.` });
    if (!r) return;
    ex().req[id] = { to, at: Date.now(), eligibleFrom: Date.now() + 2 * 24 * 3600000 }; BT.save(); BT.rerender();
  }
  async function confirmPromotion(id) {
    const a = BT.S.accounts.find((x) => x.id === id), q = ex().req[id];
    const r = await K.ownerAct({ title: `Confirm promotion to ${q.to.replace('_', ' ')}`, lead: 'The gate is re-evaluated now and the password checked again. The mode applies from 09:00 IST on the next trading day and is written to the insert-only mode trail.' });
    if (!r) return;
    ex().modes[id] = q.to; delete ex().req[id]; BT.audit('owner', 'Mode promoted', `${a.name} → ${q.to} (design only; effective next trading day)`); BT.save(); BT.toast(`${a.name}: ${q.to.replace('_', ' ')} from 09:00 IST next trading day`, 'good'); BT.rerender();
  }
  function limitsHtml() {
    const L = ex().limits, empty = !K.on('execution_enabled');
    return K.card('Limits sheet (P12-09: auto_live only, ceilings in code)', `<div class="card-b" style="display:grid;gap:10px"><div class="small muted">Sheet in force: ${empty ? K.st('empty', 'Empty: nothing may be sent automatically', false) : `orders ${L.orders}, notional ${inr(L.notional)}, daily loss ${inr(L.loss)}`}. A pending change shows beside it with its effective-from time.</div>
      <div class="grid g3"><label class="field">Orders per day (ceiling 10)<input class="input" type="number" id="lm-o" min="0" max="10" value="${L.orders}"></label><label class="field">Notional per day, Rs (ceiling 300,000)<input class="input" type="number" id="lm-n" min="0" max="300000" step="1000" value="${L.notional}"></label><label class="field">Worst-case daily loss, Rs (ceiling 3,000)<input class="input" type="number" id="lm-l" min="0" max="3000" step="100" value="${L.loss}"></label></div>
      <div class="small muted" id="lm-hint">Raising any value raises risk: it waits one closed session and needs your password. Lowering is immediate and needs none. The form never offers a value above a ceiling.</div><div class="err" id="lm-err" role="alert"></div>
      <div><button class="btn primary" id="lm-save">Save limits</button></div></div>`);
  }
  function bindLimits(body) {
    const L = ex().limits;
    const inputs = ['lm-o', 'lm-n', 'lm-l'].map((i) => $('#' + i, body));
    if (!inputs[0]) return;
    const raise = (i, v) => v > L[['orders', 'notional', 'loss'][i]];
    inputs.forEach((x, i) => x.addEventListener('input', () => { x.style.borderColor = raise(i, +x.value) ? 'var(--warn)' : ''; $('#lm-hint', body).textContent = inputs.some((y, j) => raise(j, +y.value)) ? 'Marked fields raise risk: this change needs your password and takes effect after the next closed session.' : 'This change lowers or keeps risk: immediate, no password.'; }));
    $('#lm-save', body).onclick = async () => {
      const v = inputs.map((x) => +x.value), mx = [10, 300000, 3000];
      if (v.some((x, i) => !(x >= 0 && x <= mx[i]))) return ($('#lm-err', body).textContent = 'A value is above its ceiling or below 0.');
      const up = v.some((x, i) => raise(i, x));
      const r = await K.ownerAct({ title: 'Save limits sheet', lead: up ? 'At least one value raises risk: it takes effect after the next closed session and needs your password.' : 'This change lowers risk and applies at once.', password: up });
      if (!r) return; Object.assign(L, { orders: v[0], notional: v[1], loss: v[2] }); BT.save(); BT.toast(up ? 'Saved: effective after the next closed session' : 'Saved', 'good'); BT.rerender();
    };
  }

  /* =====================================================================================
     STOP CHIP in the app bar (P12-10 criterion 8) and BELL NOTICES (P8-15, P12-10, P7-20)
     ===================================================================================== */
  BT.afterShell = () => BT.onStatus();
  BT.onStatus = function () {
    const slot = $('#stop-slot'); if (!slot) return;
    const show = anyAutomated() || halted();
    if (!show) { slot.innerHTML = ''; return; }
    const h = halted();
    slot.innerHTML = `<button class="stop-chip ${h ? 'halted' : ''}" id="stop-chip" title="Stop trading: one deliberate action">${I('octagon')}<span>${h ? 'Trading stopped' : 'Stop<span class="lbl-long"> trading</span>'}</span></button>`;
    $('#stop-chip').onclick = h ? releaseDialog : stopDialog;
  };
  function stopDialog() {
    const e = ex();
    const bodyRow = (id, title, what, danger) => `<div class="stop-opt"><div><b>${title}</b><div class="small muted">${what}</div></div><button class="btn ${danger ? 'bad' : ''}" data-stop="${id}">${danger ? 'Review list…' : 'Do this'}</button></div>`;
    BT.modal({ title: 'Stop trading', body: `<div class="banner warn" style="margin:0">${I('alert')}<div>Working orders: <b>1</b> · open positions: <b>2</b> · stops stay at the broker in every option. Stopping never needs a password.</div></div>
        ${bodyRow('entries', 'Stop new orders', 'No new entry is created or sent. Working orders and open positions are left as they are.')}
        ${bodyRow('cancel', 'Stop new orders and cancel pending ones', 'Also cancels the 1 working order. Open positions and their stops stay.')}
        ${bodyRow('exit_all', 'Stop new orders, cancel pending ones and exit all positions at market', 'Sells or covers 2 positions at market. Shows the exact list first and needs your password and a second click.', true)}`,
      foot: '<button class="btn" data-close autofocus>Close</button>',
      onMount: (ov, close) => {
        $('[data-close]', ov).focus();
        $$('[data-stop]', ov).forEach((b) => (b.onclick = async () => {
          const lv = b.dataset.stop;
          if (lv === 'exit_all') { close(); const r = await K.ownerAct({ title: 'Exit all positions at market', lead: 'Exact list: <b>TATAPOWER</b> BUY 120 (sell 120 at market), <b>INFY</b> long 30 (sell 30 at market, foreign position: not ours). This is the only stop level that needs the password. In this prototype no order is sent.', okLabel: 'Stop and exit all', danger: true }); if (!r) return; setHalt(lv); return; }
          close(); setHalt(lv);
        }));
      } });
  }
  function setHalt(level) { const e = ex(); e.halt = { level, at: Date.now(), id: 'H-' + Math.random().toString(36).slice(2, 6) }; BT.audit('owner', 'Halt opened', `level ${level}; design only`); BT.save(); BT.toast('Trading stopped (' + level + ')', 'bad'); BT.renderTopStatus(); BT.rerender(); }
  async function releaseDialog() {
    const e = ex(); const h = e.halt || { level: 'cancel', id: 'H-demo', at: Date.now() };
    BT.modal({ title: 'Release the halt', body: `<p style="margin:0">Halt <b class="mono">${h.id}</b> · level <b>${h.level}</b> · ${fmtTime(h.at)}. Release needs your password and a reason; the server's refusal reasons are shown verbatim.</p>
        <label class="field">Reason (required)<textarea class="input" id="rl-r" rows="2" maxlength="500" style="font-family:var(--font)"></textarea></label><label class="field">Password<input class="input" type="password" id="rl-pw" value="demo-password"></label>
        <div class="err" id="rl-err" role="alert"></div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="rl-ok">Release</button>',
      onMount: (ov, close) => { $('#rl-ok', ov).onclick = () => {
        if (!$('#rl-r', ov).value.trim()) return ($('#rl-err', ov).textContent = 'Enter a reason.');
        if (D.hash($('#rl-pw', ov).value) !== BT.S.auth.pw) return ($('#rl-err', ov).textContent = 'Wrong password.');
        if (ex().sim === 'missing') return ($('#rl-err', ov).textContent = 'Refused: execution.release_refused: a position has no confirmed broker-side protection (TATAPOWER).');
        e.halt = null; if (e.sim === 'halted') e.sim = 'on'; BT.audit('owner', 'Halt released', 'password and reason on file'); BT.save(); close(); BT.toast('Halt released', 'good'); BT.renderTopStatus(); BT.rerender(); }; } });
  }

  // Notices: derived from state, never stored. Each id carries its cause; a notice leaves by itself when the cause is gone.
  const hm = (t) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
  BT.extraNotices = function () {
    const S = BT.S, out = [], now = Date.now();
    const plan = S.plan, prim = plan.items.filter((i) => !i.reserve);
    const und = prim.filter((i) => i.status === 'proposed').length;
    const d = new Date(), nowM = d.getHours() * 60 + d.getMinutes(), left = hm(S.settings.reviewCutoff) - nowM;
    if (und && left > 0) { const band = left > 60 ? 'open' : left > 15 ? 'soon' : 'final'; out.push({ id: `plan:review:${plan.date}:${band}`, severity: band === 'open' ? 'info' : 'warning', title: 'Plan waiting for review', text: `Plan for ${plan.date}: ${und} of ${prim.length} primary tickets undecided, ${left} minutes left until the ${S.settings.reviewCutoff} IST cutoff. Undecided tickets lapse at the cutoff.`, at: now, link: '#/review', src: 'Plan · P8-15' }); }
    const lapsed = prim.filter((i) => i.status === 'lapsed').length;
    if (lapsed) out.push({ id: 'plan:lapsed:' + plan.date, severity: 'info', title: 'Tickets lapsed', text: `${lapsed} undecided tickets lapsed at the cutoff for ${plan.date}. Nothing was approved, armed or ordered.`, at: now, link: '#/review', src: 'Plan · P8-15' });
    const armed = prim.filter((i) => i.status === 'armed' || i.status === 'invalidated');
    if (armed.length) out.push({ id: 'plan:preopen:' + plan.date, severity: armed.some((i) => i.status === 'invalidated') ? 'warning' : 'info', title: 'Pre-open check', text: `At pre-open ${armed.filter((i) => i.status === 'armed').length} tickets were armed and ${armed.filter((i) => i.status === 'invalidated').length} invalidated. Armed means watched, not ordered. No order exists.`, at: now, link: '#/review', src: 'Plan · P8-15' });
    if (K.on('ticket_alerts') && armed.length) { const t = armed.find((i) => i.status === 'armed'); if (t) out.push({ id: 'crossing:' + t.id, severity: 'warning', title: `${t.symbol}: indicative stop level crossed`, text: `${t.symbol} ${t.side}: indicative stop from the close crossed at ${num(t.stop)}. Alert only: no order is placed and none exists. A stop is only an alert.`, at: now - 600000, link: '#/review', src: 'Alerts · P7-20' }); out.push({ id: 'prompt:' + plan.date, severity: 'info', title: 'Did you take this trade?', text: `After the session: was ${armed[0].symbol} ${armed[0].side} taken? The question closes by itself when your fill is found.`, at: now - 3600000, link: '#/review', src: 'Prompts · P7-20' }); }
    const sim = ex().sim;
    if (sim === 'on' || sim === 'missing') { if (EX.ticket) out.push({ id: 'exec:ticket:114', severity: 'warning', title: 'Order waiting for your tap', text: 'BUY 120 TATAPOWER at LIMIT 412.35 expires within 90 seconds of issue.', at: EX.tickAt, link: '#/execution', src: 'Execution · P12-10' }); if (sim === 'missing') out.push({ id: 'exec:protection:TATAPOWER', severity: 'error', title: 'Protection missing', text: 'TATAPOWER BUY 120 has no stop confirmed at the broker.', at: now, link: '#/execution?tab=positions', src: 'Execution · P12-10' }); out.push({ id: 'exec:auto:1', severity: 'info', title: 'Automatic action', text: 'Stop-loss 405.10 confirmed at the broker for TATAPOWER (tightening is automatic in semi_auto).', at: now - 1800000, link: '#/execution?tab=activity', src: 'Execution · P12-10', resolved: true }); }
    if (sim === 'notready') out.push({ id: 'exec:notready:' + K.lastDay(), severity: 'error', title: 'Execution not ready', text: 'token_covers_session failed: the token expires at 15:00 today. Log in again; protection is at the broker and the system cannot see it now.', at: now, link: '#/execution', src: 'Execution · P12-11' });
    if (halted()) out.push({ id: 'exec:halted', severity: 'error', title: 'Trading stopped', text: 'A halt is open. No new order is created; stops stay at the broker.', at: (ex().halt || { at: now }).at, link: '#/execution', src: 'Execution · P12-10' });
    return out;
  };
  BT.noticeSeen = (n) => { const p = K.P(); return !!(p.seen && p.seen[n.id]); };
  BT.markNoticesSeen = (list) => { const p = K.P(); p.seen = p.seen || {}; list.forEach((n) => (p.seen[n.id] = 1)); };
})();
