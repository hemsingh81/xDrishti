/* xDrishti prototype — Learning page: journey, lessons (problem → fix → result), execution feedback,
   cell evolution, experiments (tweak & re-run) and autonomy. Demo data is deterministic (seeded). */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const { $, $$, esc, num, pct, sgn, fmtDate, fmtDT, ago } = BT;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);
  const r2 = (x) => Math.round(x * 100) / 100;
  const R = (x, d = 2) => (x > 0 ? '+' : x < 0 ? '−' : '') + Math.abs(x).toFixed(d) + 'R';
  const WEEK = 7 * 86400000;

  /* ---------------- learning configuration (what the experiments tweak) ---------------- */
  const KNOBS = [
    { k: 'lookbackMonths', l: 'History window', u: 'months', min: 6, max: 36, step: 1, def: 18, hint: 'How much history each cell learns from' },
    { k: 'halfLifeDays', l: 'Recency half-life', u: 'days', min: 30, max: 365, step: 5, def: 120, hint: 'Older trades count less — shorter adapts faster but is noisier' },
    { k: 'minTrades', l: 'Min trades to activate a cell', u: 'trades', min: 10, max: 80, step: 1, def: 30, hint: 'Evidence needed before a setup can trade' },
    { k: 'priorStrength', l: 'Shrinkage prior', u: 'trades', min: 5, max: 60, step: 1, def: 35, hint: 'Pulls small samples toward the strategy average' },
    { k: 'vixGate', l: 'Breakout VIX gate', u: 'VIX', min: 12, max: 32, step: 0.5, def: 18, off: true, hint: 'Skip breakout cells above this INDIA VIX (off = never skip)' },
    { k: 'stopAtr', l: 'Intraday stop', u: '× ATR', min: 0.6, max: 2.0, step: 0.05, def: 1.2, hint: 'Initial stop distance for 5m/15m setups' },
    { k: 'minPWin', l: 'Selection threshold', u: 'P(win)', min: 0.45, max: 0.65, step: 0.01, def: 0.52, hint: 'Minimum calibrated probability to enter the plan' },
    { k: 'slippageBps', l: 'Slippage model', u: 'bps', min: 0, max: 30, step: 1, def: 8, hint: 'Cost assumed in backtests — too low flatters results' },
  ];
  const defaults = () => Object.fromEntries(KNOBS.map((x) => [x.k, x.def]));
  function cfg() { const S = BT.S; S.learningConfig = Object.assign(defaults(), S.learningConfig || {}); return S.learningConfig; }

  /* ---------------- simulated replay (walk-forward, 12 folds, common random numbers) ---------------- */
  function simulate(p) {
    const q = (x, c, w) => ((x - c) / w) ** 2;
    let exp = 0.24 - 0.10 * q(p.stopAtr, 1.25, 0.6) - 0.06 * q(p.minTrades, 32, 30) - 0.05 * q(p.priorStrength, 35, 30) - 0.04 * q(p.halfLifeDays, 120, 200) - 0.12 * q(p.lookbackMonths, 24, 12);
    exp += p.vixGate == null ? -0.05 : -0.04 * q(p.vixGate, 18, 8);
    exp += 0.5 * (p.minPWin - 0.52);
    exp -= 0.004 * (p.slippageBps - 8);
    const liveGap = Math.max(0, 0.0045 * (9 - p.slippageBps));
    let trades = 360 * Math.exp(-(p.minPWin - 0.52) * 12) * (1 - (p.minTrades - 30) / 160) * (p.vixGate == null ? 1.08 : 1 - Math.max(0, 18 - p.vixGate) * 0.03);
    trades = Math.max(40, Math.round(trades));
    const win = Math.min(0.72, Math.max(0.25, 0.47 + exp * 0.3 + (p.stopAtr - 1.2) * 0.09));
    const avgL = -1.03, avgW = (exp - (1 - win) * avgL) / win;
    const ece = 0.025 + 0.045 * Math.abs(p.priorStrength - 35) / 30 + 0.02 * Math.abs(p.halfLifeDays - 120) / 245 + (p.minTrades < 20 ? 0.02 : 0);
    // trade-by-trade path with common random numbers so candidate and baseline differ only by the parameters
    const r = D.rng('replay-crn');
    const path = []; let eq = 0, peak = 0, dd = 0;
    for (let i = 0; i < trades; i++) {
      const u = r(), v = r();
      const x = u < win ? avgW * (0.35 + 1.3 * v) : avgL * (0.85 + 0.3 * v);
      eq += x; peak = Math.max(peak, eq); dd = Math.min(dd, eq - peak); path.push(r2(eq));
    }
    // fold results share the market noise (same periods) plus a small parameter-specific part
    const rc = D.rng('folds-common'), rf = D.rng('folds:' + JSON.stringify(p));
    const folds = Array.from({ length: 12 }, () => r2(exp + (rc() - 0.5) * 0.16 + (rf() - 0.5) * 0.05));
    const gw = win * avgW, gl = -(1 - win) * avgL;
    return { exp: r2(exp), trades, win, pf: gl ? gw / gl : null, maxDD: r2(dd), ece, liveGap: r2(liveGap), path, folds, total: r2(eq) };
  }

  /* ---------------- deterministic learning history (52 weeks) ---------------- */
  const CAT = { Regime: ['radio', '#fb7185', '#e11d48'], Exits: ['target', '#34d399', '#059669'], Execution: ['gauge', '#fbbf24', '#d97706'], Calibration: ['percent', '#2dd4bf', '#0d9488'],
    Decisions: ['checkCircle', '#818cf8', '#4f46e5'], Drift: ['trendDown', '#c084fc', '#7c3aed'], Events: ['calendar', '#38bdf8', '#0284c7'] };
  const LESSONS = [
    { id: 'L1', cat: 'Regime', d: 5, f: 8, up: 0.05, title: 'Breakouts failed when volatility was high', status: 'resolved', how: 'auto',
      problem: 'ORB 15m win rate fell from 47% to 29% over 38 trades while INDIA VIX was above 18 — a −9.4R drawdown.',
      cause: 'False breakouts in volatile opens: 64% of losers hit the stop within 15 minutes (MAE analysis).',
      fix: 'Regime gate: breakout cells pause when VIX > 18 or regime is RANGE_HIGH_VOL. Applied automatically (within guardrails) after 40-trade evidence.',
      result: 'ORB expectancy −0.21R → +0.17R over the next 60 trades; the drawdown stopped.', metric: ['ORB expectancy (R)', -0.21, 0.17], knob: { vixGate: 18 } },
    { id: 'L2', cat: 'Exits', d: 11, f: 13, up: 0.045, title: 'Stops were too tight on 5-minute setups', status: 'resolved', how: 'auto',
      problem: '41% of stopped VWAP-pullback 5m trades later reached their target (MFE after stop).',
      cause: 'Stop at 0.8 ATR sat inside normal noise for the first 30 minutes.',
      fix: 'Hypothesis test, one variable: stop 0.8 → 1.2 ATR. Better in 7 of 8 walk-forward folds → promoted.',
      result: 'Win rate 39% → 47%, expectancy +0.04R → +0.13R.', metric: ['VWAP 5m expectancy (R)', 0.04, 0.13], knob: { stopAtr: 1.2 } },
    { id: 'L3', cat: 'Execution', d: 17, f: 19, up: 0.04, title: 'Entry slippage on thinly traded mid-caps', status: 'resolved', how: 'you',
      problem: 'Actual fills were 0.11R worse than plan prices on instruments with daily turnover below ₹50 cr.',
      cause: 'Market orders at the trigger crossed wide spreads; the backtest assumed 3 bps.',
      fix: 'Liquidity filter (turnover ≥ ₹50 cr) + limit-with-buffer entries; slippage model raised to 8 bps per liquidity bucket. Approved by you.',
      result: 'Entry slippage 0.11R → 0.04R; plan-vs-actual gap more than halved.', metric: ['Entry slippage (R)', 0.11, 0.04], knob: { slippageBps: 8 } },
    { id: 'L4', cat: 'Calibration', d: 22, f: 23, up: 0.03, title: 'Probabilities were over-confident', status: 'resolved', how: 'auto',
      problem: 'Top bucket predicted 64% wins but delivered 52% (reliability curve below the diagonal).',
      cause: 'Small cells with lucky streaks; prior too weak (20 trades).',
      fix: 'Monthly PAV recalibration + shrinkage prior 20 → 35 trades.',
      result: 'Calibration error 8.7% → 2.8%; top-N selection picks better tickets.', metric: ['Calibration error (%)', 8.7, 2.8], knob: { priorStrength: 35 } },
    { id: 'L5', cat: 'Decisions', d: 27, f: 30, up: 0.03, title: 'Manual rejections were costing returns', status: 'resolved', how: 'you',
      problem: 'You rejected 31% of SELL tickets; the rejected ones would have returned +0.22R each (−6.8R missed).',
      cause: 'Discomfort with shorts while holding the same stock long-term (now shown by the pre-trade checklist).',
      fix: 'Autonomy “supervised”: tickets with P(win) ≥ 58% and all checks passing are auto-approved at the cutoff. Approved by you.',
      result: 'Decision drag −0.05R → −0.01R per trade; your review time ~12 → ~3 minutes a day.', metric: ['Decision drag (R)', -0.05, -0.01], knob: null },
    { id: 'L6', cat: 'Drift', d: 33, f: 35, up: 0.035, title: 'PDH/PDL break (1h) stopped working', status: 'resolved', how: 'auto',
      problem: 'Drift monitor (CUSUM) alarmed: 90-trade expectancy −0.04R, below its lower confidence band for 3 weeks.',
      cause: 'Edge decayed — breakouts of the prior day’s range became crowded after mid-year.',
      fix: 'Cell retired. Challenger “PDH break + volume ≥ 1.5×” ran in shadow for 50 trades (+0.11R) and replaced it.',
      result: 'Slot reused by the challenger: +0.11R live so far.', metric: ['Cell expectancy (R)', -0.04, 0.11], knob: { minTrades: 30 } },
    { id: 'L7', cat: 'Events', d: 40, f: 42, up: 0.025, title: 'Results-week gaps blew through stops', status: 'resolved', how: 'auto',
      problem: '3 swing trades lost more than 2R each on results-day gaps.',
      cause: 'Entries 1–2 days before quarterly results; stops cannot protect overnight gaps.',
      fix: 'Event filter: no new swing entries within 3 days of results; gap-aware sizing for holds.',
      result: 'Losses beyond −1.5R: 7 → 1 in the following quarter.', metric: ['Tail losses / quarter', 7, 1], knob: null },
    { id: 'L8', cat: 'Exits', d: 47, f: null, up: 0, title: 'Pyramiding may add risk in choppy 1h trends', status: 'testing', how: 'shadow',
      problem: 'Second add-on in RANGE regimes reversed in 58% of cases (23 trades).',
      cause: 'Hypothesis: add-ons should require ADX > 20 — being tested.',
      fix: 'Shadow A/B: pyramid only when ADX > 20. 23 of 50 shadow trades done.',
      result: 'So far +0.06R better than current rule — not yet significant.', metric: ['Pyramid add-on R', -0.08, -0.02], knob: null, progress: 23 / 50 },
    { id: 'L9', cat: 'Exits', d: 48, f: 49, up: 0, title: 'Wider target on EMA pullback 30m did not hold up live', status: 'rolledback', how: 'auto',
      problem: 'After the change (target 1:2 → 1:3, 8 of 12 folds better in replay), live expectancy over the 60-trade monitoring window was −0.04R vs +0.09R before.',
      cause: 'Backtest edge came from two strong trending months; live market was range-bound.',
      fix: 'Automatic rollback to 1:2 after the 60-trade monitoring window; the hypothesis is parked for 90 days.',
      result: 'Expectancy back to +0.08R; no manual action was needed.', metric: ['EMA 30m expectancy (R)', -0.04, 0.08], knob: null },
  ];
  function history() {
    if (BT.mem.learnHist) return BT.mem.learnHist;
    const r = D.rng('learn-hist-v1');
    const n = 52, start = Date.now() - (n - 1) * WEEK;
    const weeks = [], frozen = [], live = [], theo = [], cumL = [], cumF = [], ece = [], gap = [];
    let cl = 0, cf = 0;
    for (let w = 0; w < n; w++) {
      const fz = 0.13 - 0.0028 * w + (r() - 0.5) * 0.05;
      let up = 0, dip = 0;
      for (const L of LESSONS) {
        if (L.f != null && w >= L.f) up += L.up;
        if (w >= L.d - 1 && w < (L.f ?? L.d + 3)) dip += Math.min(0.08, 0.035 + (w - L.d + 1) * 0.012);
      }
      const lv = fz + up - dip + (r() - 0.5) * 0.03;
      const g = w < 19 ? 0.12 + r() * 0.03 : 0.05 + r() * 0.025;
      weeks.push(start + w * WEEK); frozen.push(r2(fz)); live.push(r2(lv)); theo.push(r2(lv + g)); gap.push(r2(g));
      ece.push(r2((w < 23 ? 8.2 + (r() - 0.5) * 1.6 : 2.9 + (r() - 0.5) * 0.8)));
      cl += lv * 7; cf += fz * 7; cumL.push(r2(cl)); cumF.push(r2(cf));
    }
    BT.mem.learnHist = { weeks, frozen, live, theo, cumL, cumF, ece, gap };
    return BT.mem.learnHist;
  }
  // change log of automatic and approved learning changes
  function changes() {
    const S = BT.S;
    if (!S.learningChanges) {
      const h = history();
      S.learningChanges = LESSONS.filter((l) => l.f != null && l.status !== 'rolledback').map((l, i) => ({ id: 'C' + (i + 1), at: h.weeks[l.f], lesson: l.id, text: l.fix.split('.')[0], source: l.how === 'you' ? 'approved' : 'auto', status: 'active' }))
        .concat([
          { id: 'C8', at: Date.now() - 2 * 86400000, lesson: null, text: 'Exit re-selection: TREND_PULLBACK 1h → 50% at 1R, trail ATR×2 (was fixed 1:2)', source: 'auto', status: 'active' },
          { id: 'C9', at: Date.now() - 1 * 86400000, lesson: null, text: 'Paused EMA_PULLBACK 30m SELL in UP_LOW_VOL (expectancy −0.06R over 41 trades)', source: 'auto', status: 'active' },
          { id: 'C10', at: Date.now() - 6 * 3600000, lesson: null, text: 'Raise risk per trade 0.5% → 0.6% for BASE_BREAKOUT 1D (calibrated edge +0.31R, 140 trades)', source: 'needs-approval', status: 'pending' },
          { id: 'C11', at: Date.now() - 5 * 3600000, lesson: null, text: 'Add new strategy candidate “Inside-bar breakout 1h” to shadow trading (Strategy Lab result +0.14R OOS)', source: 'needs-approval', status: 'pending' },
        ]).sort((a, b) => b.at - a.at);
    }
    const L = S.learningChanges;
    if (!L.some((c) => c.id === 'C12')) {
      L.push({ id: 'C12', at: Date.now() - 3 * 86400000, lesson: 'L9', text: 'Auto-rolled back: EMA_PULLBACK 30m target 1:3 → 1:2 (live −0.04R vs +0.09R before, 60 trades)', source: 'auto', status: 'rolledback' });
      L.forEach((c) => { if (c.id === 'C8') c.monitor = { done: 34, of: 60, delta: 0.04 }; if (c.id === 'C9') c.monitor = { done: 12, of: 60, delta: 0.01 }; });
      L.sort((a, b) => b.at - a.at);
    }
    return L;
  }
  BT.learningPending = () => changes().filter((c) => c.status === 'pending').length;

  /* ---------------- loss tags & weekly digest ---------------- */
  const TAG_ACTION = { 'Against regime': ['L1', 'Regime gate for breakout cells'], 'Gap through stop': ['L7', 'Event filter + gap-aware sizing'], 'High slippage': ['L3', 'Liquidity filter, limit-with-buffer entries'],
    'Late entry': ['smart', 'Learned entry windows'], 'Gave back open profit': ['L2', 'Partial exit at 1R + trail (exit re-selection)'], 'News / results': ['L7', 'Event filter'], 'Normal loss (variance)': [null, 'No action — normal edge variance'] };
  function lossTags(days = 90) {
    const since = D.DAYS[Math.max(0, D.DAYS.length - 1 - Math.round(days * 5 / 7))];
    const losses = BT.tradesData().filter((t) => t.taken && t.R <= 0 && t.date >= since);
    const by = {};
    losses.forEach((t) => t.tags.forEach((g) => { (by[g] = by[g] || []).push(t.R); }));
    return Object.entries(by).map(([tag, a]) => ({ tag, n: a.length, avg: r2(sum(a) / a.length), total: r2(sum(a)) })).sort((a, b) => b.n - a.n);
  }
  BT.learnSummary = function () {
    const S = BT.S, h = history();
    const avg = (a, k = 8) => sum(a.slice(-k)) / k;
    const week = changes().filter((c) => Date.now() - c.at < 7 * 86400000);
    const tags = lossTags().filter((t) => t.tag !== 'Normal loss (variance)');
    const recent = BT.tradesData().filter((t) => t.taken).slice(-50);
    return { uplift: R(avg(h.live) - avg(h.frozen)), live: R(avg(h.live)), autoWeek: week.filter((c) => c.source === 'auto').length, week, pending: BT.learningPending(),
      gate: S.plan.gate ? S.plan.gate.status : 'normal', riskMult: S.plan.throttle ? S.plan.throttle.riskMult : 1, topTag: tags[0] ? [tags[0].tag, tags[0].n] : ['—', 0],
      execGap: R(-sum(recent, (t) => t.slipR) / (recent.length || 1)), tags };
  };
  BT.openDigest = function () {
    const S = BT.S, L = BT.learnSummary();
    const watching = changes().filter((c) => c.monitor && c.status === 'active');
    const testing = LESSONS.filter((l) => l.status === 'testing');
    BT.modal({
      title: 'Weekly learning digest', wide: true,
      body: `<div class="mini-stats">
          <div><span class="muted small">Better than the frozen model by</span><b class="pos">${L.uplift}</b><span class="small muted">per trade (8 weeks)</span></div>
          <div><span class="muted small">Live expectancy (fills)</span><b>${L.live}</b><span class="small muted">execution gap ${L.execGap}</span></div>
          <div><span class="muted small">Changed automatically</span><b>${L.autoWeek}</b><span class="small muted">${L.pending} need you</span></div>
          <div><span class="muted small">Tomorrow</span><b>${esc(L.gate)}</b><span class="small muted">risk ×${L.riskMult}</span></div></div>
        <div class="grid g2">
          <div><h4 class="dg-h">${BT.icon('bolt')} Changed this week</h4><ul class="dg-list">${L.week.map((c) => `<li>${esc(c.text)} <span class="small muted">· ${c.source === 'auto' ? 'automatic' : c.status === 'pending' ? 'needs you' : 'approved'}</span></li>`).join('') || '<li class="muted">Nothing changed.</li>'}</ul>
            <h4 class="dg-h">${BT.icon('eye')} Being watched</h4><ul class="dg-list">${watching.map((c) => `<li>${esc(c.text)} — ${c.monitor.done}/${c.monitor.of} trades, ${R(c.monitor.delta)} vs before</li>`).join('')}${testing.map((l) => `<li>${l.id} shadow test: ${esc(l.title)} — ${Math.round(l.progress * 50)}/50 trades</li>`).join('')}</ul></div>
          <div><h4 class="dg-h">${BT.icon('alert')} Why trades lost (last quarter, automatic tags)</h4><ul class="dg-list">${L.tags.slice(0, 4).map((t) => `<li><b>${esc(t.tag)}</b>: ${t.n} losses, ${R(t.total, 1)} — ${esc(TAG_ACTION[t.tag] ? TAG_ACTION[t.tag][1] : '')}</li>`).join('')}</ul>
            <h4 class="dg-h">${BT.icon('target')} Focus for next week</h4><ul class="dg-list"><li>${L.pending ? `Decide the ${L.pending} pending change(s) — ${esc(changes().find((c) => c.status === 'pending').text)}` : 'Nothing waits for you.'}</li><li>Shadow test L8 needs ~${Math.round(50 - 23)} more trades before a decision.</li><li>${L.tags[0] ? `Most frequent loss cause is “${esc(L.tags[0].tag)}” — the fix is already in place; watch whether it falls.` : ''}</li></ul></div>
        </div>`,
      foot: `<a class="btn" href="#/learning?tab=autonomy" data-close>Review changes</a><a class="btn primary" href="#/learning" data-close>Open Learning</a>`,
    });
    BT.audit('owner', 'Viewed weekly digest', `${L.autoWeek} automatic change(s), ${L.pending} pending`);
  };

  /* ---------------- page ---------------- */
  const lr = { tab: 'journey', hashSeen: '' };
  const TABS = [['journey', 'Journey'], ['lessons', 'Lessons'], ['execution', 'Plan vs actual'], ['cells', 'Setups (cells)'], ['smart', 'Smart rules'], ['experiments', 'Tweak & re-run'], ['autonomy', 'Autonomy']];
  const card = (title, body, extra = '', cls = '') => `<div class="card ${cls}"><div class="card-h"><h3>${title}</h3>${extra ? `<div class="row">${extra}</div>` : ''}</div>${body}</div>`;

  BT.pages.learning = function (el) {
    const S = BT.S;
    const qs = new URLSearchParams(location.hash.split('?')[1] || '');
    let openDigest = false;
    if ((qs.get('tab') || qs.get('digest')) && lr.hashSeen !== location.hash) { if (qs.get('tab')) lr.tab = qs.get('tab'); openDigest = !!qs.get('digest'); lr.hashSeen = location.hash; }
    const h = history();
    const pending = BT.learningPending();
    const resolved = LESSONS.filter((l) => l.status === 'resolved').length;
    const autoN = changes().filter((c) => c.source === 'auto').length;
    const last = h.live.length - 1;
    const avg = (a, k = 8) => sum(a.slice(-k)) / k;
    el.innerHTML = `
      <div class="page-head"><div><h1>Learning</h1><p>What the system learned from past data and your actual executions, what went wrong, how it was fixed — and the knobs to tune and re-run it.</p></div>
        <div class="row"><button class="btn" id="lr-digest">${BT.icon('sparkles')} Weekly digest</button><button class="btn" id="lr-retrain">${BT.icon('refresh')} Run weekly retrain now</button><button class="btn primary" id="lr-exp">${BT.icon('git')} New experiment</button></div></div>
      ${pending ? `<div class="banner auto">${BT.icon('bolt')}<div><b>${pending} learning change(s) need your approval</b> — everything else is applied automatically within your guardrails. <a href="#" data-tab="autonomy" style="margin-left:auto">Review →</a></div></div>` : ''}
      <div class="tiles">
        <div class="tile"><div class="l">Live expectancy (8 wks)</div><div class="v pos">${R(avg(h.live))}</div><div class="s">plan prices ${R(avg(h.theo))}</div></div>
        <div class="tile"><div class="l">Learning uplift vs frozen model</div><div class="v pos">${R(avg(h.live) - avg(h.frozen))}</div><div class="s">${R(h.cumL[last] - h.cumF[last], 0)} cumulative this year</div></div>
        <div class="tile"><div class="l">Calibration error</div><div class="v">${num(avg(h.ece, 4), 1)}%</div><div class="s">was ${num(h.ece[0], 1)}% a year ago</div></div>
        <div class="tile"><div class="l">Execution gap</div><div class="v">${R(avg(h.gap, 4))}</div><div class="s">plan vs actual fills · was ${R(h.gap[0])}</div></div>
        <div class="tile"><div class="l">Lessons learned</div><div class="v">${resolved}</div><div class="s">${LESSONS.length - resolved} being tested in shadow</div></div>
        <div class="tile"><div class="l">Auto-applied changes</div><div class="v">${autoN}</div><div class="s">${pending} waiting for you · ${esc(S.settings.autonomy.level)}</div></div>
      </div>
      <div class="card" data-nocollapse="1" style="margin-bottom:16px"><div class="tabs" id="lr-tabs">${TABS.map(([k, l]) => `<button data-tab="${k}" class="${lr.tab === k ? 'on' : ''}">${l}${k === 'autonomy' && pending ? ` <span class="badge">${pending}</span>` : ''}</button>`).join('')}</div></div>
      <div id="lr-body"></div>`;
    $$('[data-tab]', el).forEach((b) => (b.onclick = (e) => { e.preventDefault(); lr.tab = b.dataset.tab; BT.rerender(); }));
    $('#lr-retrain').onclick = () => { BT.runService('retrain'); BT.toast('Weekly retrain started on the service'); };
    $('#lr-exp').onclick = () => { lr.tab = 'experiments'; BT.rerender(); };
    $('#lr-digest').onclick = () => BT.openDigest();
    if (openDigest) setTimeout(() => BT.openDigest(), 300);
    const body = $('#lr-body');
    body.classList.add('fade-swap');
    ({ journey, lessons, execution, cells, smart, experiments, autonomy }[lr.tab] || journey)(body);
  };

  /* ---------------- Journey ---------------- */
  function journey(body) {
    const h = history();
    const health = [['Data quality', 94], ['Calibration', 88], ['Execution', 81], ['Stability', 76], ['Evidence', 72]];
    const score = Math.round(sum(health, (x) => x[1]) / health.length);
    const learned = [
      'Skip breakouts when INDIA VIX > 18 — they fail twice as often (L1).',
      '5m and 15m stops at 1.2 ATR keep winners alive; 0.8 ATR was inside the noise (L2).',
      'Only trade mid-caps with ≥ ₹50 cr turnover, enter with limit + buffer (L3).',
      'Don’t open swing trades within 3 days of results (L7).',
      'Your manual SELL rejections cost more than they saved — auto-approve within limits (L5).',
    ];
    body.innerHTML = `<div class="grid lr-a">
        ${card('Learning health', `<div class="card-b" style="display:grid;gap:14px;justify-items:center"><div class="score-ring" style="--p:${score}"><div><b>${score}</b><span>of 100</span></div></div>
          <div style="width:100%;display:grid;gap:8px">${health.map(([l, v]) => `<div><div class="row between small"><span>${l}</span><b>${v}</b></div><div class="meter"><i class="grow" style="width:${v}%"></i></div></div>`).join('')}</div>
          <div class="small muted">Evidence is lowest: 4 cells still have fewer than 60 trades.</div></div>`)}
        ${card('Learning journey — weekly expectancy (R per trade)', '<div class="card-b"><div class="chart lg" id="lj-main"></div><div class="small muted">Red bands: problem detected → fix applied. Pins: lessons (click a pin to open it). Grey: the same strategies if the model had been frozen a year ago.</div></div>')}
      </div>
      <div class="grid g2" style="margin-top:16px">
        ${card('Cumulative R — with learning vs frozen model', '<div class="card-b"><div class="chart" id="lj-cum"></div></div>')}
        ${card('What the system has learned (plain language)', `<div class="card-b"><ol class="insights">${learned.map((t) => `<li>${esc(t)}</li>`).join('')}</ol>
          <div class="row" style="margin-top:12px"><button class="btn sm" data-tab="lessons">All lessons →</button><button class="btn sm" data-tab="experiments">Try a different setting →</button></div></div>`)}
      </div>
      <div style="margin-top:16px">${card('Model versions', `<div class="card-b flush"><div class="tbl-wrap" style="max-height:340px"><table class="tbl"><thead><tr><th>Version</th><th>Date</th><th>Change</th><th>How</th><th class="r">Expectancy after 4 wks</th></tr></thead><tbody>
        ${LESSONS.filter((l) => l.f != null).map((l, i) => `<tr><td><b>v1.${i + 1}</b></td><td>${fmtDate(h.weeks[l.f])}</td><td>${esc(l.fix.split('.')[0])}</td><td>${howChip(l.how)}</td><td class="r num ${sgn(h.live[Math.min(51, l.f + 4)])}">${R(h.live[Math.min(51, l.f + 4)])}</td></tr>`).reverse().join('')}
        <tr><td><b>v1.0</b></td><td>${fmtDate(h.weeks[0])}</td><td>Initial model from walk-forward on 3 years of history</td><td><span class="chip">baseline</span></td><td class="r num">${R(h.live[4])}</td></tr></tbody></table></div></div>`)}</div>`;
    $$('[data-tab]', body).forEach((b) => (b.onclick = () => { lr.tab = b.dataset.tab; BT.rerender(); }));
    const x = h.weeks.map((t) => fmtDate(t).slice(0, 6));
    const fixed = LESSONS.filter((l) => l.f != null || l.status === 'testing');
    const main = BT.echart($('#lj-main'), {
      tooltip: { trigger: 'axis', valueFormatter: (v) => R(v) }, legend: { top: 0, type: 'scroll' }, grid: { left: 48, right: 16, top: 36, bottom: 30 },
      xAxis: { type: 'category', data: x, boundaryGap: false }, yAxis: { type: 'value', axisLabel: { formatter: (v) => v.toFixed(2) + 'R' } },
      series: [
        { name: 'With learning (actual fills)', type: 'line', smooth: true, symbol: 'none', data: h.live, lineStyle: { width: 3 }, areaStyle: { opacity: 0.12 },
          markArea: { itemStyle: { color: 'rgba(244,63,94,.10)' }, data: LESSONS.map((l) => [{ xAxis: x[Math.max(0, l.d - 1)] }, { xAxis: x[Math.min(51, l.f ?? 51)] }]) },
          markPoint: { symbol: 'pin', symbolSize: 42, label: { formatter: (p) => p.name, fontSize: 10, fontWeight: 700 }, data: fixed.map((l) => ({ name: l.id, coord: [x[l.f ?? l.d], h.live[l.f ?? l.d]], itemStyle: { color: CAT[l.cat][2] } })) } },
        { name: 'Plan prices (before execution costs)', type: 'line', smooth: true, symbol: 'none', data: h.theo, lineStyle: { type: 'dashed', width: 1.5 } },
        { name: 'Frozen model (no learning)', type: 'line', smooth: true, symbol: 'none', data: h.frozen, lineStyle: { width: 2 }, color: '#94a3b8' },
      ],
    });
    if (main) main.chart.on('click', (p) => { if (p.componentType === 'markPoint') lessonDrawer(p.name); });
    BT.echart($('#lj-cum'), {
      tooltip: { trigger: 'axis', valueFormatter: (v) => R(v, 1) }, legend: { top: 0, type: 'scroll' }, grid: { left: 48, right: 16, top: 36, bottom: 30 },
      xAxis: { type: 'category', data: x, boundaryGap: false }, yAxis: { type: 'value', axisLabel: { formatter: '{value}R' } },
      series: [{ name: 'With learning', type: 'line', smooth: true, symbol: 'none', data: h.cumL, areaStyle: { opacity: 0.18 }, lineStyle: { width: 3 } },
        { name: 'Frozen model', type: 'line', smooth: true, symbol: 'none', data: h.cumF, color: '#94a3b8', lineStyle: { width: 2 } }],
    });
  }
  const howChip = (how) => ({ auto: `<span class="chip pink">${BT.icon('bolt')} automatic</span>`, you: `<span class="chip blue">${BT.icon('user')} approved by you</span>`, shadow: `<span class="chip purple">${BT.icon('eye')} shadow test</span>` }[how] || '');
  const statusChip = (s) => ({ resolved: '<span class="chip green">resolved</span>', testing: '<span class="chip amber">testing</span>', reverted: '<span class="chip red">reverted</span>', rolledback: '<span class="chip red">auto-rolled back</span>' }[s] || '');

  /* ---------------- Lessons ---------------- */
  function lessons(body) {
    const f = lr.lcat || 'all';
    const list = LESSONS.filter((l) => f === 'all' || l.cat === f).slice().reverse();
    const tags = lossTags();
    body.innerHTML = `<div class="grid lr-c" style="margin-bottom:16px">${card('Why trades lost — automatic loss tags (last quarter)', '<div class="card-b"><div class="chart sm" id="lt-ch"></div></div>')}
      ${card('Tag → what the system does about it', `<div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Tag</th><th class="r">Losses</th><th class="r">Total</th><th>Response</th></tr></thead><tbody>${tags.map((t) => { const a = TAG_ACTION[t.tag] || [null, '']; return `<tr><td><b>${esc(t.tag)}</b></td><td class="r num">${t.n}</td><td class="r num neg">${R(t.total, 1)}</td><td class="small">${a[0] && a[0][0] === 'L' ? `<a href="#" data-lesson-link="${a[0]}">${a[0]}</a> · ` : a[0] === 'smart' ? '<a href="#" data-tab-link="smart">Smart rules</a> · ' : ''}${esc(a[1])}</td></tr>`; }).join('')}</tbody></table></div>
        <div class="small muted" style="padding:10px 14px">Every losing trade is tagged automatically from its MAE/MFE, entry time, regime, slippage and the event calendar. A tag that keeps growing opens a lesson.</div></div>`)}</div>` + card(`Lessons — problem → fix → result <span class="small muted">&nbsp;${LESSONS.length} this year</span>`,
      `<div class="lesson-list">${list.map((l) => `<div class="lesson" data-lesson="${l.id}">
          <div class="l-ico" style="--c1:${CAT[l.cat][1]};--c2:${CAT[l.cat][2]}">${BT.icon(CAT[l.cat][0])}</div>
          <div><div class="row" style="gap:6px"><b>${l.id} · ${esc(l.title)}</b><span class="chip">${l.cat}</span>${statusChip(l.status)}${howChip(l.how)}<span class="small muted">detected ${fmtDate(history().weeks[l.d])}</span></div>
            <div class="l-flow"><div class="l-step p"><b>What went wrong</b>${esc(l.problem)}</div><div class="l-step f"><b>How it was fixed</b>${esc(l.fix)}</div><div class="l-step r"><b>Result</b>${esc(l.result)}</div></div>
            ${l.progress ? `<div class="row small" style="margin-top:8px"><span class="muted">Shadow test</span><div class="meter" style="width:180px"><i class="grow" style="width:${Math.round(l.progress * 100)}%"></i></div><span>${Math.round(l.progress * 50)} / 50 trades</span></div>` : ''}</div>
          <div class="spark" id="sp-${l.id}"></div></div>`).join('')}</div>`,
      `<div class="seg" id="lc-f">${['all', ...Object.keys(CAT)].map((c) => `<button data-c="${c}" class="${f === c ? 'on' : ''}">${c === 'all' ? 'All' : c}</button>`).join('')}</div>`);
    $$('#lc-f button').forEach((b) => (b.onclick = (e) => { e.stopPropagation(); lr.lcat = b.dataset.c; BT.rerender(); }));
    $$('[data-lesson]').forEach((x) => (x.onclick = () => lessonDrawer(x.dataset.lesson)));
    $$('[data-lesson-link]').forEach((x) => (x.onclick = (e) => { e.preventDefault(); lessonDrawer(x.dataset.lessonLink); }));
    $$('[data-tab-link]').forEach((x) => (x.onclick = (e) => { e.preventDefault(); lr.tab = x.dataset.tabLink; BT.rerender(); }));
    BT.echart($('#lt-ch'), { grid: { left: 150, right: 30, top: 8, bottom: 20 }, tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } }, xAxis: { type: 'value', name: 'losses' },
      yAxis: { type: 'category', inverse: true, data: tags.map((t) => t.tag) }, series: [{ type: 'bar', data: tags.map((t) => ({ value: t.n, itemStyle: { color: t.tag.startsWith('Normal') ? '#94a3b8' : '#f97316', borderRadius: 4 } })), label: { show: true, position: 'right' } }] });
    for (const l of list) BT.echart($('#sp-' + l.id), sparkOpt(l));
  }
  function sparkSeries(l) {
    const r = D.rng('spark:' + l.id); const [, a, b] = l.metric;
    return Array.from({ length: 14 }, (_, i) => { const k = i < 6 ? 0 : i > 9 ? 1 : (i - 6) / 4; return r2(a + (b - a) * k + (r() - 0.5) * Math.abs(b - a) * 0.25); });
  }
  function sparkOpt(l) {
    const good = l.metric[2] > l.metric[1] === !/slippage|error|tail|drag/i.test(l.metric[0]) || /drag/i.test(l.metric[0]);
    return { grid: { left: 2, right: 2, top: 4, bottom: 4 }, xAxis: { type: 'category', show: false, data: Array.from({ length: 14 }, (_, i) => i) }, yAxis: { type: 'value', show: false, scale: true },
      series: [{ type: 'line', data: sparkSeries(l), smooth: true, symbol: 'none', lineStyle: { width: 2.5, color: good ? '#10b981' : '#f43f5e' }, areaStyle: { opacity: 0.15, color: good ? '#10b981' : '#f43f5e' },
        markLine: { silent: true, symbol: 'none', label: { show: false }, lineStyle: { color: '#94a3b8', type: 'dashed' }, data: [{ xAxis: 6 }] } }] };
  }
  function lessonDrawer(id) {
    const l = LESSONS.find((x) => x.id === id); if (!l) return;
    const h = history();
    const worse = parseInt(id.slice(1), 10) % 8; // one fold usually disagrees — real evidence is rarely unanimous
    const folds = Array.from({ length: 8 }, (_, i) => { const r = D.rng('lf:' + id + i)(); return r2((l.metric[2] - l.metric[1]) * (i === worse ? -0.15 - r * 0.2 : 0.4 + r)); });
    BT.drawer({
      title: `${l.id} · ${esc(l.title)}`,
      body: `<div class="row">${statusChip(l.status)}${howChip(l.how)}<span class="chip">${l.cat}</span><span class="small muted">detected ${fmtDate(h.weeks[l.d])}${l.f != null ? ' · fixed ' + fmtDate(h.weeks[l.f]) : ''}</span></div>
        <div class="kv"><dt>What went wrong</dt><dd>${esc(l.problem)}</dd><dt>Diagnosis</dt><dd>${esc(l.cause)}</dd><dt>Fix</dt><dd>${esc(l.fix)}</dd><dt>Result</dt><dd>${esc(l.result)}</dd></div>
        <div class="card"><div class="card-h"><h3>${esc(l.metric[0])} — before and after the fix</h3></div><div class="card-b"><div class="chart sm" id="ld-ch"></div></div></div>
        <div class="card"><div class="card-h"><h3>Walk-forward evidence (improvement per fold)</h3><span class="small muted">${folds.filter((x) => x > 0).length} of 8 folds better</span></div><div class="card-b"><div class="chart sm" id="ld-f"></div></div></div>
        <div class="banner info">${BT.icon('lightbulb')}<div>Guardrails: one variable changed at a time, out-of-sample folds only, and the improvement must beat a threshold that rises with the number of trials (multiple-testing guard). The change is monitored for 60 trades and reverted automatically if live results fall below the old version.</div></div>
        <div class="row">${l.knob ? `<button class="btn primary" id="ld-exp">${BT.icon('git')} Re-run with a different value</button>` : ''}${l.status === 'resolved' ? `<button class="btn bad" id="ld-rev">Revert this change…</button>` : ''}</div>`,
      onMount: (ov, close, charts) => {
        BT.echart($('#ld-ch', ov), Object.assign(sparkOpt(l), { grid: { left: 40, right: 12, top: 12, bottom: 24 }, xAxis: { type: 'category', data: Array.from({ length: 14 }, (_, i) => 'wk ' + (i - 6)) }, yAxis: { type: 'value', scale: true }, tooltip: { trigger: 'axis' } }), charts);
        BT.echart($('#ld-f', ov), { grid: { left: 40, right: 12, top: 12, bottom: 24 }, tooltip: { trigger: 'axis' }, xAxis: { type: 'category', data: folds.map((_, i) => 'F' + (i + 1)) }, yAxis: { type: 'value' },
          series: [{ type: 'bar', data: folds.map((v) => ({ value: v, itemStyle: { color: (v > 0) === (l.metric[2] > l.metric[1]) ? '#10b981' : '#f43f5e', borderRadius: 4 } })) }] }, charts);
        const ex = $('#ld-exp', ov); if (ex) ex.onclick = () => { close(); lr.tab = 'experiments'; lr.draft = Object.assign({}, cfg(), l.knob); BT.rerender(); BT.toast('Experiment pre-filled from ' + l.id + ' — change the value and re-run'); };
        const rv = $('#ld-rev', ov); if (rv) rv.onclick = async () => { if (await BT.confirmDlg('Revert ' + l.id, 'The previous rule is restored from the next nightly run. The lesson stays in the history and the revert is audited.', 'Revert', true)) { l.status = 'reverted'; BT.audit('owner', 'Learning change reverted', l.id + ' ' + l.title); BT.save(); close(); BT.rerender(); BT.toast(l.id + ' reverted', 'good'); } };
      },
    });
  }

  /* ---------------- Plan vs actual (execution feedback) ---------------- */
  function execution(body) {
    const steps = [['Plan prices', 0.31], ['Entry slippage', -0.035], ['Exit slippage', -0.012], ['Missed / late triggers', -0.008], ['Your overrides', -0.01], ['Charges & taxes', -0.005]];
    const months = Array.from({ length: 12 }, (_, i) => { const d = new Date(); d.setMonth(d.getMonth() - 11 + i); return d.toLocaleDateString('en-IN', { month: 'short' }); });
    const r = D.rng('exec-m');
    const slip = months.map((_, i) => r2((i < 4 ? 0.1 : 0.04) + r() * 0.03));
    const missed = months.map((_, i) => Math.round((i < 6 ? 9 : 4) + r() * 4));
    const strat = ['ORB 15m', 'VWAP_PULLBACK 5m', 'EMA_PULLBACK 30m', 'PDH_VOL_BREAK 1h', 'BASE_BREAKOUT 1D', 'TREND_PULLBACK 1h', 'RSI2_REVERSION 1D'].map((s) => {
      const q = D.rng('es:' + s); const plan = r2(0.12 + q() * 0.3); const sl = r2(0.02 + q() * 0.06);
      return { s, n: 30 + Math.floor(q() * 70), plan, actual: r2(plan - sl - q() * 0.02), sl, fill: 0.86 + q() * 0.13, late: Math.round(q() * 9), ov: Math.round(q() * 6) };
    });
    let acc = 0;
    const wf = steps.map(([l, v], i) => { const base = i === 0 ? 0 : acc + Math.min(0, v); acc += v; return { l, v, base: i === 0 ? 0 : base }; });
    body.innerHTML = `<div class="grid g2">
        ${card('Where the edge goes — plan prices to actual result (R per trade)', '<div class="card-b"><div class="chart" id="ex-wf"></div></div>')}
        ${card('Execution quality by month', '<div class="card-b"><div class="chart" id="ex-m"></div></div>')}
      </div>
      <div style="margin-top:16px">${card('By strategy — plan vs actual fills', `<div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Setup</th><th class="r">Trades</th><th class="r">Plan R</th><th class="r">Actual R</th><th class="r">Slippage</th><th class="r">Fill rate</th><th class="r">Late exits</th><th class="r">Overrides</th><th class="r">Capacity</th></tr></thead><tbody>
        ${strat.map((x) => `<tr><td><b>${x.s}</b></td><td class="r num">${x.n}</td><td class="r num">${R(x.plan)}</td><td class="r num ${sgn(x.actual)}">${R(x.actual)}</td><td class="r num ${x.sl > 0.05 ? 'neg' : ''}">${R(-x.sl)}</td><td class="r num">${pct(x.fill, 0).replace('+', '')}</td><td class="r num">${x.late}</td><td class="r num">${x.ov}</td><td class="r"><span class="chip ${x.fill > 0.93 ? 'green' : x.fill > 0.88 ? 'amber' : 'red'}">${Math.round(x.fill * 100 - x.late)}</span></td></tr>`).join('')}</tbody></table></div></div>`)}</div>
      <div style="margin-top:16px">${card('Graded on fills — recent trades (plan prices vs what you actually got)', `<div class="card-b flush"><div class="tbl-wrap" style="max-height:360px"><table class="tbl"><thead><tr><th>Date</th><th>Instrument</th><th>Setup</th><th class="r">R at plan prices</th><th class="r">R at fills (graded)</th><th class="r">Slippage</th><th>Loss tags</th></tr></thead><tbody>
        ${BT.tradesData().filter((t) => t.taken).slice(-40).reverse().map((t) => `<tr><td>${t.date}</td><td><b>${t.symbol}</b></td><td>${t.strategy} · ${t.tf}</td><td class="r num">${R(t.planR)}</td><td class="r num ${sgn(t.R)}"><b>${R(t.R)}</b></td><td class="r num ${t.slipR > 0.065 ? 'neg' : ''}">${R(-t.slipR)}</td><td class="small">${t.tags.map((g) => `<span class="chip ${g.startsWith('Normal') ? '' : 'amber'}">${esc(g)}</span>`).join(' ')}</td></tr>`).join('')}</tbody></table></div></div>`)}</div>
      <div class="grid g2" style="margin-top:16px">
        ${card('How actual execution feeds learning', `<div class="card-b"><ol class="insights">
          <li><b>Labels use real fills</b>, not plan prices — every graded trade carries the price you actually got, so the model learns what is achievable.</li>
          <li><b>Slippage model per liquidity bucket</b> is re-estimated weekly from your fills and used in every backtest and experiment.</li>
          <li><b>Missed triggers</b> (price touched, no fill) adjust the trigger buffer and order type per instrument.</li>
          <li><b>Your overrides</b> are graded against the system's proposal (Reports → Decisions) and feed the autonomy recommendation.</li>
          <li><b>Broker rejections and partial fills</b> lower a cell's capacity score, so the selection prefers tickets that can actually be filled.</li></ol></div>`)}
        ${card('Execution alerts this week', `<div class="card-b flush">${[['amber', 'gauge', 'TATAPOWER: 3 entries filled 0.09R worse than plan — spread widened at 09:30', 'Moved to limit-with-buffer'], ['blue', 'clock', '2 triggers missed while the live feed reconnected (11:02–11:04)', 'Standby data account took over'], ['green', 'checkCircle', 'Slippage this week 0.03R — best since the liquidity filter', '']].map(([c, ic, t, a]) => `<div class="act-item sev-${c === 'amber' ? 'medium' : 'info'}"><div class="act-ico">${BT.icon(ic)}</div><div><div>${esc(t)}</div>${a ? `<div class="small muted">${esc(a)}</div>` : ''}</div><span></span></div>`).join('')}</div>`)}
      </div>`;
    BT.echart($('#ex-wf'), {
      tooltip: { trigger: 'axis', formatter: (p) => { const s = p.find((x) => x.seriesName === 'v'); return `${s.name}: ${R(wf[s.dataIndex].v, 3)}`; } }, grid: { left: 52, right: 16, top: 16, bottom: 56 },
      xAxis: { type: 'category', data: wf.map((x) => x.l).concat(['Actual result']), axisLabel: { interval: 0, rotate: 20 } }, yAxis: { type: 'value', axisLabel: { formatter: (v) => v.toFixed(2) + 'R' } },
      series: [{ name: 'base', type: 'bar', stack: 't', itemStyle: { color: 'transparent' }, data: wf.map((x) => x.base).concat([0]) },
        { name: 'v', type: 'bar', stack: 't', data: wf.map((x, i) => ({ value: Math.abs(x.v), itemStyle: { color: i === 0 ? '#6366f1' : '#f43f5e', borderRadius: 4 } })).concat([{ value: r2(acc), itemStyle: { color: '#10b981', borderRadius: 4 } }]),
          label: { show: true, position: 'top', formatter: (p) => (p.dataIndex === 0 || p.dataIndex === wf.length ? R(p.value) : '−' + p.value.toFixed(3)) } }],
    });
    BT.echart($('#ex-m'), {
      tooltip: { trigger: 'axis' }, legend: { top: 0, type: 'scroll' }, grid: { left: 48, right: 48, top: 36, bottom: 28 }, xAxis: { type: 'category', data: months },
      yAxis: [{ type: 'value', name: 'R', axisLabel: { formatter: (v) => v.toFixed(2) } }, { type: 'value', name: 'missed %', splitLine: { show: false } }],
      series: [{ name: 'Entry slippage (R)', type: 'bar', data: slip, itemStyle: { borderRadius: 4, color: '#f59e0b' } }, { name: 'Missed triggers (%)', type: 'line', yAxisIndex: 1, smooth: true, data: missed, color: '#ec4899' }],
    });
  }

  /* ---------------- Setups (cells) ---------------- */
  function cells(body) {
    const strategies = ['ORB 15m', 'VWAP_PULLBACK 5m', 'EMA_PULLBACK 30m', 'PDH_PDL_BREAK 1h', 'PDH_VOL_BREAK 1h', 'BASE_BREAKOUT 1D', 'TREND_PULLBACK 1h', 'RSI2_REVERSION 1D'];
    const months = Array.from({ length: 12 }, (_, i) => { const d = new Date(); d.setMonth(d.getMonth() - 11 + i); return d.toLocaleDateString('en-IN', { month: 'short', year: '2-digit' }); });
    const data = [];
    strategies.forEach((s, y) => months.forEach((m, x) => {
      const q = D.rng('cell:' + s + x)();
      let v = { 'ORB 15m': x < 1 || x > 2 ? 0.15 : -0.2, 'VWAP_PULLBACK 5m': x < 3 ? 0.03 : 0.13, 'EMA_PULLBACK 30m': 0.1, 'PDH_PDL_BREAK 1h': 0.08 - x * 0.03, 'PDH_VOL_BREAK 1h': 0.11,
        'BASE_BREAKOUT 1D': 0.28, 'TREND_PULLBACK 1h': 0.19, 'RSI2_REVERSION 1D': 0.09 }[s] + (q - 0.5) * 0.12;
      if (s === 'PDH_PDL_BREAK 1h' && x > 7) v = null;
      if (s === 'PDH_VOL_BREAK 1h' && x < 7) v = null;
      data.push([x, y, v == null ? '-' : r2(v)]);
    }));
    const tiers = [['Candidates', 46, '#94a3b8', '#64748b'], ['Shadow / paper', 18, '#c084fc', '#7c3aed'], ['Probation', 11, '#fbbf24', '#d97706'], ['Active', 14, '#34d399', '#059669'], ['Paused', 5, '#fb7185', '#e11d48'], ['Retired', 9, '#a1a1aa', '#52525b']];
    const rows = [['BASE_BREAKOUT 1D BUY · UP_LOW_VOL', 'active', 142, 0.31, 0.56, 'Strongest cell; risk increase proposed (needs you)'], ['TREND_PULLBACK 1h BUY · UP_*', 'active', 118, 0.21, 0.52, 'Exit changed to partial + trail last week'],
      ['ORB 15m BUY · UP_LOW_VOL', 'active', 96, 0.17, 0.5, 'Paused automatically when VIX > 18 (L1)'], ['ORB 15m SELL · RANGE_HIGH_VOL', 'paused', 38, -0.21, 0.31, 'Regime gate (L1)'],
      ['VWAP_PULLBACK 5m BUY · any', 'active', 84, 0.13, 0.47, 'Stop 1.2 ATR (L2)'], ['PDH_VOL_BREAK 1h BUY · UP_*', 'probation', 50, 0.11, 0.51, 'Promoted from shadow (L6) — half size until 60 trades'],
      ['EMA_PULLBACK 30m SELL · UP_LOW_VOL', 'paused', 41, -0.06, 0.4, 'Paused yesterday — negative after costs'], ['Inside-bar breakout 1h', 'shadow', 12, 0.14, 0.49, 'Candidate from Strategy Lab — awaiting your approval'],
      ['PDH_PDL_BREAK 1h', 'retired', 212, -0.04, 0.42, 'Drift alarm (L6)']];
    const st = { active: 'green', paused: 'red', probation: 'amber', shadow: 'purple', retired: '' };
    body.innerHTML = `<div class="grid lr-b">
        ${card('Setup expectancy by month (R per trade)', '<div class="card-b"><div class="chart lg" id="ce-heat"></div><div class="small muted">Blank = the setup did not exist or was retired. PDH_PDL_BREAK decays and is replaced by PDH_VOL_BREAK from shadow testing.</div></div>')}
        ${card('Cell lifecycle', `<div class="card-b"><div class="funnel">${tiers.map(([l, n, a, b], i) => `<div class="fs"><span>${l}</span><div class="fb" style="width:${Math.max(8, n / 46 * 100)}%;--c1:${a};--c2:${b};animation-delay:${i * 0.08}s"></div><b class="num">${n}</b></div>`).join('')}</div>
          <div class="small muted" style="margin-top:12px">Candidates → shadow (paper) → probation (half size) → active. Drift or negative live results move a cell to paused; persistent decay retires it. Moves inside these rules are automatic.</div></div>`)}
      </div>
      <div style="margin-top:16px">${card('Cells that changed recently', `<div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Cell</th><th>Status</th><th class="r">Trades</th><th class="r">Expectancy</th><th class="r">P(win) calibrated</th><th>Why</th></tr></thead><tbody>
        ${rows.map(([c, s, n, e, p, why]) => `<tr><td><b>${c}</b></td><td><span class="chip ${st[s]}">${s}</span></td><td class="r num">${n}</td><td class="r num ${sgn(e)}">${R(e)}</td><td class="r num">${Math.round(p * 100)}%</td><td class="small">${esc(why)}</td></tr>`).join('')}</tbody></table></div></div>`)}</div>`;
    BT.echart($('#ce-heat'), {
      tooltip: { formatter: (p) => `${strategies[p.value[1]]} · ${months[p.value[0]]}: ${p.value[2] === '-' ? 'not trading' : R(p.value[2])}` }, grid: { left: 140, right: 16, top: 10, bottom: 84 },
      xAxis: { type: 'category', data: months, splitArea: { show: true } }, yAxis: { type: 'category', data: strategies, splitArea: { show: true } },
      visualMap: { min: -0.25, max: 0.35, calculable: true, orient: 'horizontal', left: 'center', bottom: 0, inRange: { color: ['#e11d48', '#fda4af', '#f1f5f9', '#86efac', '#059669'] } },
      series: [{ type: 'heatmap', data, label: { show: true, fontSize: 10, formatter: (p) => (p.value[2] === '-' ? '' : p.value[2].toFixed(2)) }, itemStyle: { borderRadius: 4, borderColor: 'transparent', borderWidth: 2 } }],
    });
  }

  /* ---------------- Smart rules: market gate, equity throttle, meta-labelling, entry windows, correlation ---------------- */
  function smart(body) {
    const S = BT.S;
    const sm = D.smartCfg(S);
    const d = lr.smartDraft = lr.smartDraft || JSON.parse(JSON.stringify(sm));
    const trades = BT.tradesData();
    const gate = D.marketGate(Object.assign({}, S, { smart: d }), S.plan.date);
    const thr = D.equityThrottle(trades, d.throttle);
    const wins = D.learnedWindows(trades, d.windows.minTrades);
    const taken = trades.filter((t) => t.taken);
    const avgR = (l) => (l.length ? sum(l, (t) => t.R) / l.length : 0);
    const tk = taken.filter((t) => t.meta >= d.meta.minTake), sk = taken.filter((t) => t.meta < d.meta.minTake);
    const planSyms = S.plan.items.filter((i) => !i.reserve || i.filtered).map((i) => i.symbol);
    const openSyms = [...new Set(BT.positions().filter((p) => p.status === 'open' && p.bucket === 'Trading').map((p) => p.symbol))];
    const corrSyms = [...new Set(planSyms.concat(openSyms))].slice(0, 12);
    const lvl = { normal: 'green', reduced: 'amber', 'no-trade': 'red' };
    const tog = (k) => `<label class="switch" title="Rule on / off"><input type="checkbox" data-en="${k}" ${d[k].enabled ? 'checked' : ''}><span></span>${d[k].enabled ? 'On' : 'Off'}</label>`;
    const slider = (k, f, l, min, max, step, fmt) => `<div class="knob"><b class="small">${l}</b><div class="row"><input type="range" data-sk="${k}.${f}" data-fmt="${fmt}" min="${min}" max="${max}" step="${step}" value="${d[k][f]}"><output>${FMT[fmt](d[k][f])}</output></div></div>`;
    body.innerHTML = `<div class="banner info">${BT.icon('lightbulb')}<div>These rules sit between the learned signals and tomorrow's plan. Their thresholds are <b>learned from your history</b> and they run automatically every night. Changes apply from the next nightly run; switching a rule off asks for your password.</div></div>
      <div class="grid g2">
        ${card('Market gate — learn when not to trade', `<div class="card-b"><div class="row between" style="margin-bottom:10px"><div class="row"><span class="gate-pill ${lvl[gate.status]}">${gate.status === 'no-trade' ? 'NO-TRADE DAY' : gate.status.toUpperCase()}</span><span class="small muted">for ${fmtDate(new Date(S.plan.date))}</span></div>${tog('gate')}</div>
          <div class="tbl-wrap"><table class="tbl"><thead><tr><th>Factor · learned evidence</th><th>Now</th><th>Level</th></tr></thead><tbody>${gate.factors.map((f) => `<tr><td style="white-space:normal"><b>${f.label}</b><div class="small muted">${esc(f.evidence)}</div></td><td class="small" style="white-space:normal">${esc(f.text)}</td><td><span class="chip ${lvl[f.level]}">${f.level}</span></td></tr>`).join('')}</tbody></table></div>
          <div class="small muted" style="margin:8px 0">Upcoming: ${gate.events.map((e) => `${esc(e.name)} (${fmtDate(new Date(e.date))})`).join(' · ')}</div>
          <div class="grid g2">${slider('gate', 'vixReduce', 'Reduce above VIX', 14, 26, 0.5, 'n')}${slider('gate', 'vixNoTrade', 'No-trade above VIX', 18, 32, 0.5, 'n')}</div>
          <div class="row" style="margin-top:10px"><button class="btn sm" id="sm-sim">${d.gate.simulate ? 'Back to live market data' : 'Simulate a no-trade day'}</button><span class="small muted">Reduced day: half the tickets, risk ×0.75 · No-trade day: no tickets</span></div></div>`)}
        ${card('Equity-curve throttle — cut risk when the system is cold', `<div class="card-b"><div class="row between" style="margin-bottom:6px"><span class="small">Now: <b class="${thr.mult < 1 ? 'neg' : 'pos'}">risk ×${thr.mult}</b> ${thr.mult < 1 ? '(equity below its average)' : '(equity above its average)'}</span>${tog('throttle')}</div>
          <div class="chart sm" id="sm-thr"></div>
          <div class="mini-stats" style="margin-top:8px"><div><span class="muted small">Total R</span><b>${R(thr.totalW, 0)}</b><span class="small muted">without throttle ${R(thr.total, 0)}</span></div><div><span class="muted small">Max drawdown</span><b class="pos">${R(thr.ddW, 1)}</b><span class="small muted">without ${R(thr.dd, 1)}</span></div><div><span class="muted small">Return ÷ drawdown</span><b>${(thr.totalW / -thr.ddW).toFixed(1)}</b><span class="small muted">without ${(thr.total / -thr.dd).toFixed(1)}</span></div><div><span class="muted small">Time throttled</span><b>${Math.round(thr.throttledPct * 100)}%</b><span class="small muted">of trades</span></div></div>
          <div class="grid g2" style="margin-top:8px">${slider('throttle', 'window', 'Average over (trades)', 10, 40, 1, 'n')}${slider('throttle', 'cut', 'Risk while cold', 0.25, 1, 0.05, 'x')}</div></div>`)}
        ${card('Meta-labelling — take this signal, and how big?', `<div class="card-b"><div class="row between" style="margin-bottom:6px"><span class="small">A second model trained on your graded trades scores every signal: below the threshold → reserve; above → size ×0.5–1.5.</span>${tog('meta')}</div>
          <div class="chart sm" id="sm-meta"></div>
          <div class="mini-stats" style="margin-top:8px"><div><span class="muted small">Taken (score ≥ ${Math.round(d.meta.minTake * 100)}%)</span><b class="pos">${R(avgR(tk))}</b><span class="small muted">${tk.length} trades</span></div><div><span class="muted small">Would be skipped</span><b class="neg">${R(avgR(sk))}</b><span class="small muted">${sk.length} trades</span></div><div><span class="muted small">Total R kept</span><b>${R(sum(tk, (t) => t.R), 0)}</b><span class="small muted">of ${R(sum(taken, (t) => t.R), 0)}</span></div></div>
          ${slider('meta', 'minTake', 'Take threshold', 0.3, 0.7, 0.01, 'p')}
          <div class="small muted">Tomorrow: filtered ${S.plan.items.filter((i) => i.filtered && i.filtered.startsWith('Meta')).map((i) => esc(i.symbol)).join(', ') || 'none'} · sizes ${S.plan.items.filter((i) => !i.reserve).map((i) => esc(i.symbol) + ' ×' + (i.sizeMult ?? 1)).join(', ') || '—'}</div></div>`)}
        ${card('Entry windows — when each setup works', `<div class="card-b"><div class="row between" style="margin-bottom:6px"><span class="small">Expectancy by entry hour from the walk-forward backtest; ★ = learned window, used as the ticket's validity window.</span>${tog('windows')}</div><div class="chart" id="sm-win"></div>
          <div class="small muted">${Object.entries(wins).map(([k, w]) => `${k}: <b>${w.label || '—'}</b>`).join(' · ')}</div></div>`)}
      </div>
      <div style="margin-top:16px">${card('Correlation — avoid the same bet twice (tickets + open trades, 60-day returns)', `<div class="card-b"><div class="row between" style="margin-bottom:6px"><span class="small">A ticket correlated above the limit with a better ticket or an open trade (●) moves to the reserve list; the pre-trade checklist warns too.</span><div class="row" style="flex-wrap:nowrap">${slider('corr', 'max', 'Limit', 0.4, 0.9, 0.05, 'n')}${tog('corr')}</div></div><div class="chart lg" id="sm-corr"></div></div>`)}</div>
      <div class="row" style="margin-top:16px"><button class="btn" id="sm-reset">Discard changes</button><span class="spacer"></span><button class="btn" id="sm-save">Save rules</button><button class="btn primary" id="sm-run">${BT.icon('play')} Save & re-run nightly plan</button></div>`;
    // charts
    const areas = [];
    thr.series.forEach((x, i) => { if (x.mult < 1) { if (i === 0 || thr.series[i - 1].mult === 1) areas.push([{ xAxis: x.date }, { xAxis: x.date }]); areas[areas.length - 1][1] = { xAxis: x.date }; } });
    BT.echart($('#sm-thr'), { tooltip: { trigger: 'axis' }, legend: { top: 0, type: 'scroll' }, grid: { left: 40, right: 10, top: 30, bottom: 22 }, xAxis: { type: 'category', data: thr.series.map((x) => x.date), axisLabel: { show: false } }, yAxis: { type: 'value', axisLabel: { formatter: '{value}R' } },
      series: [{ name: 'Without throttle', type: 'line', symbol: 'none', data: thr.series.map((x) => x.eq), color: '#94a3b8' }, { name: `${d.throttle.window}-trade average`, type: 'line', symbol: 'none', data: thr.series.map((x) => x.ma), lineStyle: { type: 'dashed', width: 1 }, color: '#f59e0b' },
        { name: 'With throttle', type: 'line', symbol: 'none', data: thr.series.map((x) => x.eqW), color: '#6366f1', lineStyle: { width: 2.5 }, markArea: { itemStyle: { color: 'rgba(245,158,11,.12)' }, data: areas } }] });
    const buckets = [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8];
    BT.echart($('#sm-meta'), { tooltip: { trigger: 'axis', valueFormatter: (v) => R(v) }, grid: { left: 44, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'category', data: buckets.map((b) => `${Math.round(b * 100)}–${Math.round(b * 100) + 10}%`) }, yAxis: { type: 'value', axisLabel: { formatter: (v) => v.toFixed(1) + 'R' } },
      series: [{ type: 'bar', data: buckets.map((b) => { const l = taken.filter((t) => t.meta >= b && t.meta < b + 0.1); const v = r2(avgR(l)); return { value: v, itemStyle: { color: b + 0.1 <= d.meta.minTake ? '#cbd5e1' : v >= 0 ? '#4ade80' : '#f87171', borderRadius: 4 } }; }) }] });
    const strats = Object.keys(wins), hours = Object.keys(D.HOUR_LABEL);
    BT.echart($('#sm-win'), { tooltip: { formatter: (p) => `${strats[p.value[1]]} · ${D.HOUR_LABEL[hours[p.value[0]]]}: ${R(p.value[2])} (backtest n=${wins[strats[p.value[1]]].cells[p.value[0]].n})` }, grid: { left: 120, right: 10, top: 6, bottom: 28 },
      xAxis: { type: 'category', data: hours.map((h) => D.HOUR_LABEL[h].slice(0, 5)) }, yAxis: { type: 'category', data: strats },
      visualMap: { min: -0.15, max: 0.3, show: false, inRange: { color: ['#fecaca', '#f8fafc', '#bbf7d0', '#4ade80'] } },
      series: [{ type: 'heatmap', data: strats.flatMap((st, y) => wins[st].cells.map((c, x) => [x, y, c.exp])), label: { show: true, fontSize: 10, color: '#334155', formatter: (p) => (wins[strats[p.value[1]]].best && wins[strats[p.value[1]]].best.h === +hours[p.value[0]] ? '★ ' : '') + p.value[2].toFixed(2) }, itemStyle: { borderColor: 'transparent', borderWidth: 2, borderRadius: 4 } }] });
    BT.echart($('#sm-corr'), { tooltip: { formatter: (p) => `${corrSyms[p.value[0]]} × ${corrSyms[p.value[1]]}: ${p.value[2]}` }, grid: { left: 96, right: 10, top: 6, bottom: 74 },
      xAxis: { type: 'category', data: corrSyms, axisLabel: { rotate: 40, formatter: (v) => (openSyms.includes(v) ? '● ' : '') + v } }, yAxis: { type: 'category', data: corrSyms, axisLabel: { formatter: (v) => (openSyms.includes(v) ? '● ' : '') + v } },
      visualMap: { min: -0.2, max: 1, show: false, inRange: { color: ['#e0f2fe', '#f8fafc', '#fde68a', '#fb923c'] } },
      series: [{ type: 'heatmap', data: corrSyms.flatMap((a, i) => corrSyms.map((b, j) => [i, j, i === j ? 1 : D.corr(a, b, d.corr.lookback)])), label: { show: true, fontSize: 10, color: '#334155', formatter: (p) => (p.value[0] === p.value[1] ? '' : p.value[2].toFixed(2)) }, itemStyle: { borderColor: 'transparent', borderWidth: 2, borderRadius: 3 } }] });
    // inputs
    $$('[data-sk]', body).forEach((inp) => (inp.oninput = () => { const [k, f] = inp.dataset.sk.split('.'); d[k][f] = parseFloat(inp.value); inp.parentElement.querySelector('output').textContent = FMT[inp.dataset.fmt](d[k][f]); clearTimeout(lr.smT); lr.smT = setTimeout(() => BT.rerender(), 400); }));
    $$('[data-en]', body).forEach((c) => (c.onchange = () => { d[c.dataset.en].enabled = c.checked; BT.rerender(); }));
    $('#sm-sim').onclick = () => { d.gate.simulate = !d.gate.simulate; BT.rerender(); };
    $('#sm-reset').onclick = () => { lr.smartDraft = null; BT.rerender(); };
    const save = async (run) => {
      const loosened = Object.keys(d).some((k) => sm[k] && sm[k].enabled && d[k].enabled === false);
      if (loosened && !(await BT.stepUp('Switching off a risk rule'))) return;
      S.smart = JSON.parse(JSON.stringify(d)); lr.smartDraft = null;
      BT.audit('owner', 'Smart rules saved', ['gate', 'throttle', 'meta', 'windows', 'corr'].map((k) => `${k} ${d[k].enabled ? 'on' : 'off'}`).join(', ') + (d.gate.simulate ? ' · simulating a no-trade day' : ''));
      BT.save();
      if (run) { BT.runService('nightly'); BT.toast('Rules saved — nightly pipeline re-running with them', 'good'); } else BT.toast('Rules saved — used from the next nightly run', 'good');
      BT.rerender();
    };
    $('#sm-save').onclick = () => save(false);
    $('#sm-run').onclick = () => save(true);
  }
  const FMT = { n: (v) => String(v), x: (v) => '×' + v, p: (v) => Math.round(v * 100) + '%' };

  /* ---------------- Experiments: tweak & re-run ---------------- */
  function experiments(body) {
    const S = BT.S;
    const base = cfg();
    const draft = lr.draft = Object.assign({}, base, lr.draft || {});
    const trials = S.experiments.filter((e) => Date.now() - e.at < 30 * 86400000).length;
    const need = r2(0.02 + 0.006 * Math.sqrt(trials));
    const fmtV = (k, v) => { const kn = KNOBS.find((x) => x.k === k); return v == null ? 'off' : (kn.step < 1 ? (kn.k === 'minPWin' ? Math.round(v * 100) + '%' : num(v, 2)) : v) + (kn.k === 'minPWin' ? '' : ' ' + kn.u); };
    body.innerHTML = `<div class="grid lr-c">
      ${card('Learning knobs', `<div class="card-b" style="display:grid;gap:14px">${KNOBS.map((kn) => `<div class="knob ${draft[kn.k] !== base[kn.k] ? 'changed' : ''}" data-knob="${kn.k}">
          <div class="row between"><b class="small">${kn.l}</b>${kn.off ? `<label class="check small"><input type="checkbox" data-off="${kn.k}" ${draft[kn.k] == null ? 'checked' : ''}> off</label>` : ''}</div>
          <div class="row"><input type="range" min="${kn.min}" max="${kn.max}" step="${kn.step}" value="${draft[kn.k] ?? kn.def}" data-k="${kn.k}" ${draft[kn.k] == null ? 'disabled' : ''}><output>${fmtV(kn.k, draft[kn.k])}</output></div>
          <div class="hint">${kn.hint} · current ${fmtV(kn.k, base[kn.k])}</div></div>`).join('')}
        <div class="row"><button class="btn" id="xp-reset">Reset to current</button><span class="spacer"></span><button class="btn primary" id="xp-run">${BT.icon('play')} Re-run replay</button></div></div>`)}
      <div style="display:grid;gap:16px;align-content:start">
        ${card('Replay result', `<div class="card-b" id="xp-out"><div class="empty">${BT.icon('git')}<br>Change one or more knobs and press <b>Re-run replay</b>.<br>The system replays the last 12 months walk-forward (12 folds) with your settings, using the same trade sequence as the current model so only your change differs.</div></div>`)}
        ${card('Experiment history', `<div class="card-b flush" id="xp-hist">${historyTable()}</div>`, `<span class="small muted">${trials} trial(s) in 30 days → required improvement ${R(need)}</span>`)}
      </div></div>`;
    $$('[data-k]', body).forEach((inp) => (inp.oninput = () => { const k = inp.dataset.k; draft[k] = parseFloat(inp.value); const box = inp.closest('.knob'); box.querySelector('output').textContent = fmtV(k, draft[k]); box.classList.toggle('changed', draft[k] !== base[k]); }));
    $$('[data-off]', body).forEach((c) => (c.onchange = () => { const k = c.dataset.off; draft[k] = c.checked ? null : KNOBS.find((x) => x.k === k).def; BT.rerender(); }));
    $('#xp-reset').onclick = () => { lr.draft = Object.assign({}, base); BT.rerender(); };
    $('#xp-run').onclick = () => runReplay(draft, base, need);
  }
  function historyTable() {
    return `<div class="tbl-wrap" style="max-height:320px"><table class="tbl"><thead><tr><th>When</th><th>Change</th><th class="r">Δ Expectancy</th><th class="r">Folds better</th><th>Verdict</th><th>Status</th></tr></thead><tbody>
      ${BT.S.experiments.map((e) => `<tr><td>${ago(e.at)}</td><td class="small">${esc(e.diff)}</td><td class="r num ${sgn(e.dExp)}">${R(e.dExp)}</td><td class="r num">${e.better}/12</td><td>${verdictChip(e.verdict)}</td><td><span class="chip ${e.status === 'applied' ? 'green' : e.status === 'shadow' ? 'purple' : ''}">${e.status}</span></td></tr>`).join('') || '<tr><td colspan="6" class="empty">No experiments yet.</td></tr>'}</tbody></table></div>`;
  }
  const verdictChip = (v) => ({ better: '<span class="chip green">better</span>', worse: '<span class="chip red">worse</span>', inconclusive: '<span class="chip amber">inconclusive</span>' }[v]);
  function runReplay(draft, base, need) {
    const S = BT.S;
    const diff = KNOBS.filter((k) => draft[k.k] !== base[k.k]).map((k) => `${k.l}: ${base[k.k] ?? 'off'} → ${draft[k.k] ?? 'off'}`).join('; ');
    if (!diff) return BT.toast('Change at least one knob first', 'bad');
    const out = $('#xp-out');
    const a = simulate(draft), b = simulate(base);
    out.innerHTML = `<div style="display:grid;gap:10px"><div class="row between small"><b>Replaying 12 walk-forward folds…</b><span id="xp-pct">0%</span></div><div class="runbar"><i id="xp-bar"></i></div><div class="fold-grid" id="xp-folds">${Array.from({ length: 12 }, () => '<span></span>').join('')}</div><div class="small muted mono" id="xp-log">Loading 12 months of 1-minute history and graded trades…</div></div>`;
    $('#xp-run').disabled = true;
    let i = 0;
    const logs = ['Rebuilding cells with your settings', 'Recalibrating probabilities (PAV)', 'Selecting top-N per day with your threshold', 'Applying slippage model to fills', 'Grading exits on 1-minute bars'];
    const t = setInterval(() => {
      if (!document.body.contains(out)) return clearInterval(t);
      const sq = $$('#xp-folds span')[i]; if (sq) sq.classList.add(a.folds[i] > b.folds[i] ? 'done' : 'bad');
      i++;
      $('#xp-bar').style.width = Math.round(i / 12 * 100) + '%'; $('#xp-pct').textContent = Math.round(i / 12 * 100) + '%';
      $('#xp-log').textContent = `Fold ${i}/12 · ${logs[i % logs.length]}`;
      if (i >= 12) { clearInterval(t); setTimeout(() => showResult(a, b, diff, need, draft), 250); }
    }, 220);
  }
  function showResult(a, b, diff, need, draft) {
    const S = BT.S;
    const better = a.folds.filter((x, i) => x > b.folds[i]).length;
    const tN = S.experiments.filter((e) => Date.now() - e.at < 30 * 86400000).length;
    const dExp = r2((a.exp - a.liveGap) - (b.exp - b.liveGap)); // judged after the expected live gap, so a flattering cost model cannot win
    const totalA = a.exp * a.trades, totalB = b.exp * b.trades;
    const fewer = totalA < totalB * 0.9; // higher expectancy on far fewer trades can still earn less in total
    const verdict = dExp >= need && better >= 8 && !fewer ? 'better' : (dExp <= -0.01 && better <= 4) || (dExp <= 0 && fewer) ? 'worse' : 'inconclusive';
    const exp = { id: BT.uid('X'), at: Date.now(), diff, params: Object.assign({}, draft), dExp, better, verdict, status: 'discarded' };
    S.experiments.unshift(exp); if (S.experiments.length > 50) S.experiments.length = 50;
    BT.audit('owner', 'Learning experiment', `${diff} → ${verdict} (Δ ${dExp}R, ${better}/12 folds)`);
    BT.save();
    if ($('#xp-hist')) $('#xp-hist').innerHTML = historyTable();
    const row = (l, x, y, f, goodUp = true) => { const d = x - y; const cls = Math.abs(d) < 1e-9 ? 'flat' : (d > 0) === goodUp ? 'up' : 'down'; return `<tr><td>${l}</td><td class="r num">${f(y)}</td><td class="r num"><b>${f(x)}</b></td><td class="r"><span class="delta ${cls}">${d > 0 ? '▲' : d < 0 ? '▼' : '■'} ${f(Math.abs(d)).replace(/^[+−]/, '')}</span></td></tr>`; };
    const out = $('#xp-out'); if (!out) return;
    out.innerHTML = `<div class="banner ${verdict === 'better' ? 'good' : verdict === 'worse' ? 'warn' : 'info'}">${BT.icon(verdict === 'better' ? 'award' : verdict === 'worse' ? 'alert' : 'lightbulb')}<div><b>${verdict === 'better' ? 'Better than the current model' : verdict === 'worse' ? 'Worse than the current model' : 'Inconclusive'}</b> — ${better} of 12 folds better, Δ expectancy ${R(dExp)} (needs ≥ ${R(need)} because ${tN} other trial(s) ran this month).
        ${fewer && dExp > 0 ? 'Expectancy per trade is higher, but total R per year is lower because it trades much less. ' : ''}${verdict === 'better' ? 'Recommended next step: run it in shadow for 30 trades before it goes live.' : verdict === 'worse' ? 'Keep the current setting.' : 'Not enough evidence — try a smaller change or wait for more data.'}</div></div>
      <div class="grid"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Metric</th><th class="r">Current</th><th class="r">Candidate</th><th class="r">Change</th></tr></thead><tbody>
        ${row('Expectancy / trade', a.exp, b.exp, (v) => R(v))}${row('Win rate', a.win, b.win, (v) => pct(v).replace('+', ''))}${row('Profit factor', a.pf || 0, b.pf || 0, (v) => num(v, 2))}
        ${row('Trades / year', a.trades, b.trades, (v) => String(Math.round(v)))}${row('Total R / year', totalA, totalB, (v) => R(v, 0))}${row('Max drawdown', a.maxDD, b.maxDD, (v) => R(v, 1))}${row('Calibration error', a.ece * 100, b.ece * 100, (v) => num(v, 1) + '%', false)}
        ${row('Expected live gap', a.liveGap, b.liveGap, (v) => R(v), false)}</tbody></table></div><div class="chart" id="xp-ch"></div></div>
      <div class="row" style="margin-top:12px"><button class="btn" id="xp-discard">Discard</button><span class="spacer"></span><button class="btn" id="xp-shadow" ${verdict === 'worse' ? 'disabled' : ''}>${BT.icon('eye')} Run in shadow (30 trades)</button><button class="btn primary" id="xp-apply" ${verdict !== 'better' ? 'disabled' : ''}>${BT.icon('rocket')} Apply now…</button></div>`;
    BT.echart($('#xp-ch'), { tooltip: { trigger: 'axis', valueFormatter: (v) => R(v, 1) }, legend: { top: 0, type: 'scroll' }, grid: { left: 44, right: 10, top: 32, bottom: 24 },
      xAxis: { type: 'category', data: a.path.length > b.path.length ? a.path.map((_, i) => i + 1) : b.path.map((_, i) => i + 1), name: 'trade' }, yAxis: { type: 'value', axisLabel: { formatter: '{value}R' } },
      series: [{ name: 'Candidate', type: 'line', symbol: 'none', smooth: true, data: a.path, lineStyle: { width: 2.5 }, areaStyle: { opacity: 0.12 } }, { name: 'Current', type: 'line', symbol: 'none', smooth: true, data: b.path, color: '#94a3b8' }] });
    $('#xp-discard').onclick = () => { BT.toast('Experiment discarded (kept in history)'); BT.rerender(); };
    $('#xp-shadow').onclick = () => { exp.status = 'shadow'; BT.audit('owner', 'Experiment → shadow', diff); BT.save(); BT.toast('Running in shadow: paper trades alongside the live model for 30 trades', 'good'); BT.rerender(); };
    $('#xp-apply').onclick = async () => {
      if (!(await BT.stepUp('Applying a learning change'))) return;
      Object.assign(BT.S.learningConfig, exp.params); exp.status = 'applied'; lr.draft = null;
      changes().unshift({ id: BT.uid('C'), at: Date.now(), lesson: null, text: 'Experiment applied: ' + diff, source: 'approved', status: 'active' });
      BT.audit('owner', 'Learning config changed', diff); BT.save(); BT.toast('Applied — used from the next nightly run; monitored for 60 trades with automatic rollback', 'good'); BT.rerender();
    };
  }

  /* ---------------- Autonomy ---------------- */
  function autonomy(body) {
    const S = BT.S, au = S.settings.autonomy;
    const LV = [
      ['advisory', 'Advisory', 'clipboard', 'You approve every ticket and every learning change.', ['Every ticket needs your approval', 'All learning changes wait for you', 'Most effort, slowest adaptation']],
      ['supervised', 'Supervised (recommended)', 'shield', 'Routine work is automatic; you decide what matters.', ['Tickets above the bar auto-approve at the cutoff', 'Pausing cells, exits and recalibration apply automatically', 'You approve risk increases and new strategies']],
      ['autonomous', 'Autonomous within limits', 'rocket', 'The system runs the plan; you get a digest and can veto.', ['All tickets inside risk limits are armed', 'Shadow-tested challengers are promoted automatically', 'Risk increases, new strategies and live execution still need you']],
    ];
    const ch = changes();
    body.innerHTML = `${card('How much should the system decide on its own?', `<div class="card-b"><div class="ladder">${LV.map(([k, l, ic, d, pts], i) => `<div class="lv ${au.level === k ? 'on' : ''}" data-lv="${k}">${BT.navIcon(['review', 'quality', 'learning'][i])}<h4>${l}</h4><div class="small muted">${d}</div><ul class="small" style="margin:8px 0 0;padding-left:18px">${pts.map((p) => `<li>${p}</li>`).join('')}</ul></div>`).join('')}</div>
        <div class="grid g3" style="margin-top:16px">
          <div class="knob"><div class="row between"><b class="small">Auto-approve tickets with P(win) at least</b></div><div class="row"><input type="range" id="au-p" min="0.5" max="0.7" step="0.01" value="${au.autoApproveMinP}" ${au.level === 'advisory' ? 'disabled' : ''}><output id="au-pv">${Math.round(au.autoApproveMinP * 100)}%</output></div><div class="hint">…and every pre-trade check passing. ${S.plan.items.filter((x) => BT.autoEligible && BT.autoEligible(x)).length} ticket(s) in today's plan qualify.</div></div>
          <div class="knob"><div class="row between"><b class="small">Max automatic learning changes per week</b></div><div class="row"><input type="range" id="au-m" min="0" max="10" step="1" value="${au.maxChangesPerWeek}"><output id="au-mv">${au.maxChangesPerWeek}</output></div><div class="hint">More than this waits for you — limits how fast the system can drift.</div></div>
          <div class="knob"><b class="small">Always ask me before</b><div class="small" style="display:grid;gap:4px;margin-top:4px">${['Increasing risk or position size', 'Adding a new strategy to live trading', 'Enabling live order execution', 'Changing accounts or credentials'].map((t) => `<label class="check">${BT.icon('lock')} ${t}</label>`).join('')}</div></div>
        </div>
        <div class="row" style="margin-top:14px"><label class="check"><input type="checkbox" id="au-learn" ${au.autoApplyLearning ? 'checked' : ''}> Apply learning changes automatically within guardrails (one variable, out-of-sample proof, 60-trade monitoring, auto-rollback)</label><span class="spacer"></span><button class="btn primary" id="au-save">Save autonomy settings</button></div></div>`)}
      <div style="margin-top:16px">${card(`Learning change log <span class="small muted">&nbsp;automatic and approved</span>`, `<div class="card-b flush">${ch.map((c) => `<div class="act-item sev-${c.status === 'pending' ? 'medium' : 'info'}"><div class="act-ico">${BT.icon(c.status === 'rolledback' ? 'refresh' : c.source === 'auto' ? 'bolt' : c.status === 'pending' ? 'alert' : 'user')}</div>
          <div><div>${esc(c.text)}</div><div class="small muted">${fmtDT(c.at)} · ${c.source === 'auto' ? 'automatic' : c.source === 'approved' ? 'approved by you' : 'needs your approval'}${c.lesson ? ' · from ' + c.lesson : ''}${c.status === 'reverted' ? ' · reverted' : c.status === 'rejected' ? ' · rejected' : c.status === 'rolledback' ? ' · rolled back automatically' : ''}</div>
          ${c.monitor && c.status === 'active' ? `<div class="row small" style="margin-top:4px"><span class="muted">Monitoring</span><div class="meter" style="width:140px"><i style="width:${Math.round(c.monitor.done / c.monitor.of * 100)}%"></i></div><span>${c.monitor.done}/${c.monitor.of} trades · ${R(c.monitor.delta)} vs before · auto-rollback if worse</span></div>` : ''}</div>
          <div class="row" style="flex-wrap:nowrap">${c.status === 'pending' ? `<button class="btn sm good" data-ok="${c.id}">Approve</button><button class="btn sm bad" data-no="${c.id}">Reject</button>` : c.status === 'active' && c.source === 'auto' ? `<button class="btn sm" data-undo="${c.id}">Revert</button>` : ''}</div></div>`).join('')}</div>`)}</div>`;
    $$('[data-lv]', body).forEach((x) => (x.onclick = () => { lr.level = x.dataset.lv; $$('[data-lv]', body).forEach((y) => y.classList.toggle('on', y === x)); $('#au-p').disabled = x.dataset.lv === 'advisory'; }));
    $('#au-p').oninput = (e) => { $('#au-pv').textContent = Math.round(e.target.value * 100) + '%'; };
    $('#au-m').oninput = (e) => { $('#au-mv').textContent = e.target.value; };
    $('#au-save').onclick = async () => {
      const lvl = lr.level || au.level, order = ['advisory', 'supervised', 'autonomous'];
      if (order.indexOf(lvl) > order.indexOf(au.level) && !(await BT.stepUp('Giving the system more autonomy'))) return;
      Object.assign(au, { level: lvl, autoApproveMinP: parseFloat($('#au-p').value), maxChangesPerWeek: parseInt($('#au-m').value, 10), autoApplyLearning: $('#au-learn').checked });
      lr.level = null; BT.audit('owner', 'Autonomy settings', `${au.level} · auto-approve P ≥ ${au.autoApproveMinP} · ${au.maxChangesPerWeek} changes/week · auto-learning ${au.autoApplyLearning}`); BT.save(); BT.toast('Autonomy settings saved', 'good'); BT.rerender();
    };
    const find = (id) => ch.find((c) => c.id === id);
    $$('[data-ok]', body).forEach((b) => (b.onclick = async () => { const c = find(b.dataset.ok); if (/risk/i.test(c.text) && !(await BT.stepUp('Increasing risk'))) return; c.status = 'active'; c.source = 'approved'; BT.audit('owner', 'Learning change approved', c.text); BT.save(); BT.toast('Approved — used from the next nightly run', 'good'); BT.rerender(); }));
    $$('[data-no]', body).forEach((b) => (b.onclick = () => { const c = find(b.dataset.no); c.status = 'rejected'; BT.audit('owner', 'Learning change rejected', c.text); BT.save(); BT.toast('Rejected — the system will not propose it again for 30 days'); BT.rerender(); }));
    $$('[data-undo]', body).forEach((b) => (b.onclick = async () => { const c = find(b.dataset.undo); if (await BT.confirmDlg('Revert change', esc(c.text) + '<br><br>The previous rule is restored from the next nightly run.', 'Revert', true)) { c.status = 'reverted'; BT.audit('owner', 'Learning change reverted', c.text); BT.save(); BT.rerender(); } }));
  }
})();
