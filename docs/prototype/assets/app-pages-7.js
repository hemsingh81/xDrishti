/* xDrishti prototype — additions to existing pages from the 2026-10-24 specs (wrappers: the original pages are kept).
   Suggestions proposals (P9-08), plan briefing/flags/alerts (P9-06, P8-14, P7-20), Lab draft panel (P9-07),
   Learning journey/smart rules/tweak/owner acts (P8-12/13/18/19), Reports evidence/exports/replay (P8-08..11, P8-20),
   Accounts modes + readiness (P12-07, P12-11), Settings (P8-19/20/21, P7-20), Audit (P8-22), Today strip. All demo data. */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData, K = BT.kit;
  const { $, $$, esc, num, inr, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const I = BT.icon, R = K.R;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);
  const DAY = 86400000;

  const wrap = (id, fn) => { const orig = BT.pages[id]; BT.pages[id] = function (el) { orig(el); try { fn(el); } catch (e) { console.error('prototype patch ' + id, e); } }; };
  const tagHead = (el, ids) => { const d = $('.page-head > div', el); if (d) d.insertAdjacentHTML('beforeend', `<div class="spec-row">${K.specs(ids)}</div>`); };
  const afterHead = (el, html) => { const h = $('.page-head', el); h ? h.insertAdjacentHTML('afterend', html) : el.insertAdjacentHTML('afterbegin', html); };
  const assistantOn = () => K.on('assistant');

  /* =========================== SUGGESTIONS — proposals (P9-08) =========================== */
  const PROP_KIND = { hypothesis: ['Hypothesis', 'git'], track_instrument: ['Track instrument', 'list'], basket_add: ['Basket add', 'basket'], basket_remove: ['Basket remove', 'basket'], next_day_focus: ['Next-day focus', 'target'] };
  function proposals() {
    const P = K.P();
    if (!P.props) {
      const now = Date.now();
      const ev = (n, figs, extra = []) => ({ basis: 'gross, stored simulation runs; re-read by the engine, not taken from the model', population: 'cell ORB 15m BUY in stored runs', range: `${D.DAYS[D.DAYS.length - 250]} to ${K.lastDay()}`, n, figs, limits: ['The model supplied no figure: every number was re-derived by the engine.', 'Many looks at the same data; an improvement can be chance.'].concat(extra) });
      P.props = [
        { id: 'AP-7', kind: 'hypothesis', subject: 'ORB 15m BUY: rr 2 → 2.5', reason: 'The cell has 41 new trades since the last proposal and its winners reach 2.5R more often than the 2R target captures. One knob changed; everything else stays.', state: 'pending', at: now - 6 * 3600000, exp: now + 7 * DAY,
          evidence: ev(41, [['cell expectancy', { state: 'value', v: 0.18, n: 41 }], ['credible lower bound', { state: 'insufficient', reason: 'n 41 of at least 60' }], ['share reaching 2.5R', { state: 'value', v: 0.31, n: 41 }]]),
          checks: [['baseline_run_exists', true, 'run 7f3a… found; knob names a parameter at its stored value'], ['knob_within_50_percent', true, '+25% of the baseline'], ['cell_n_at_least_30', true, 'n 41'], ['not_repeat_blocked', true, 'not rejected in the last 30 days'], ['evidence_resolved', true, 'every reference re-read']],
          effect: 'Writes the hypothesis "ORB 15m BUY: rr 2 to 2.5" as proposed. It will not run or change anything until you start the experiment on Learning, and applying a result is your own act.' },
        { id: 'AP-8', kind: 'track_instrument', subject: 'PERSISTENT', reason: 'In the master list, a share, active, not tracked. Eight of your best-stock peers hold it and the sector had the strongest relative strength of 11.', state: 'pending', at: now - 6 * 3600000, exp: now + 3 * DAY,
          evidence: ev(null, [['peers holding it', { state: 'value', v: 8, n: null }], ['own trades', { state: 'empty', reason: 'none: not tracked yet' }]], ['An instrument with no history has no cell: nothing is claimed about its edge.']),
          checks: [['in_master_active_share', true, 'NSE equity, active'], ['not_tracked', true, 'not in the tracked list'], ['not_pending', true, 'no identical pending item']],
          effect: 'Adds PERSISTENT to the tracked list and starts a history backfill on the data service.' },
        { id: 'AP-6', kind: 'next_day_focus', subject: 'Prefer swing on 26 Oct', reason: 'Both styles have at least 15 trades in the stored runs and swing is ahead.', state: 'accepted', at: now - 2 * DAY, exp: now - DAY, decided: { actor: 'owner', at: now - 2 * DAY + 3600000 },
          evidence: ev(52, [['intraday expectancy', { state: 'value', v: 0.08, n: 52 }], ['swing expectancy', { state: 'value', v: 0.21, n: 38 }]]), checks: [['style_insight_exists', true, 'both groups n ≥ 15'], ['plan_date_next_session', true, '']], effect: 'Text only: shown in the plan review. It has no effect on any plan.' },
        { id: 'AP-4', kind: 'basket_add', subject: 'SBIN → Best stocks', reason: 'Tracked, not a member.', state: 'expired', at: now - 5 * DAY, exp: now - 2 * DAY, evidence: ev(null, []), checks: [['basket_is_manual', true, ''], ['instrument_tracked', true, '']], effect: 'Adds SBIN to Best stocks.' },
      ];
    }
    return P.props;
  }
  const ck = (c) => `<div class="chk-line">${I(c[1] ? 'checkCircle' : 'xCircle')}<div><b class="mono">${c[0]}</b> · ${c[1] ? 'ok' : 'failed'} <span class="muted">${esc(c[2] || '')}</span></div></div>`;
  function propCard(p) {
    const [kl, ki] = PROP_KIND[p.kind];
    const open = p.state === 'pending';
    return `<div class="card" style="margin-bottom:12px"><div class="card-h"><h3>${I(ki)} ${esc(p.subject)} <span class="chip">${kl}</span> <span class="chip ${open ? 'amber' : ''}">${p.state}</span></h3><div class="small muted">${open ? 'expires ' + fmtDate(p.exp) : p.state === 'accepted' ? 'accepted ' + ago(p.decided.at) + ' by owner' : p.state}</div></div>
      <div class="card-b" style="display:grid;gap:10px"><div>${esc(p.reason)}</div>
        ${K.evidence({ basis: p.evidence.basis, population: p.evidence.population, range: p.evidence.range, n: p.evidence.n, limits: p.evidence.limits, more: p.evidence.figs.length ? [['Figures', p.evidence.figs.map(([l, m]) => `${esc(l)}: ${K.metric(m, (v) => (v < 1 && v > -1 && l.includes('expectancy') ? R(v) : l.includes('share') ? Math.round(v * 100) + '%' : num(v, 0)))}`).join('<br>')]] : [] })}
        <div><b>Engine checks</b>${p.checks.map(ck).join('')}</div>
        ${open ? `<div class="row"><button class="btn good" data-pa="${p.id}">Accept…</button><button class="btn bad" data-pr="${p.id}">Reject</button><span class="small muted">Accept needs your password; nothing changes before that.</span></div>` : ''}</div></div>`;
  }
  wrap('suggestions', (el) => {
    tagHead(el, ['P9-08']);
    const list = proposals();
    const pend = list.filter((p) => p.state === 'pending'), done = list.filter((p) => p.state !== 'pending');
    const meta = { auto_accept_hypothesis: { label: 'Auto-accept hypotheses', on: 'A hypothesis proposal that passes every engine check is written automatically, recorded as automatic (never as your approval) with the settings snapshot.' }, auto_accept_track_instrument: { label: 'Auto-accept track instrument', on: 'Tracks the instrument and starts a backfill by itself.' }, auto_accept_basket_add: { label: 'Auto-accept basket add' }, auto_accept_basket_remove: { label: 'Auto-accept basket remove' } };
    const html = `<div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Assistant proposals ${K.spec('P9-08')}</h3><span class="small muted">at most 3 per run, 10 pending in all</span></div>
        <div class="card-b" style="display:grid;gap:8px"><div class="banner info" style="margin:0">${I('lock')}<div>The model only <b>proposes</b>. The engine re-derives every figure and runs every rule check; only proposals that pass land here. Accept does exactly what you could do by hand, after your password. ${assistantOn() ? '' : '<b>The assistant is off</b>, so no new proposals are made; stored ones stay listed.'}</div></div>
        ${pend.map(propCard).join('') || K.st('empty', 'No pending proposal.', true)}
        ${done.length ? `<details><summary class="small">Decided and expired (30 days) · ${done.length}</summary><div style="margin-top:10px">${done.map(propCard).join('')}</div></details>` : ''}</div></div>
      <div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Auto option for proposals</h3>${K.safety()}</div><div class="card-b flush">${['auto_accept_hypothesis', 'auto_accept_track_instrument', 'auto_accept_basket_add', 'auto_accept_basket_remove'].map((id) => K.switchRow({ id, label: meta[id].label, desc: 'Default OFF = manual approval (the standing rule). ON never allows an order, and an unavailable input never counts as a pass.' })).join('')}</div></div>
      <h3 style="margin:6px 0 10px">Instrument and basket suggestions (existing inbox)</h3>`;
    $('.page-head', el).insertAdjacentHTML('afterend', html);
    K.bindSwitches(el, meta);
    $$('[data-pa]', el).forEach((b) => (b.onclick = async () => { const p = list.find((x) => x.id === b.dataset.pa); const r = await K.ownerAct({ title: 'Accept proposal ' + p.id, lead: `<b>${esc(p.subject)}.</b> ${esc(p.effect)}`, reason: false, okLabel: 'Accept' }); if (!r) return; p.state = 'accepted'; p.decided = { actor: 'owner', at: Date.now() }; BT.audit('owner', 'Proposal accepted', p.subject); BT.save(); BT.toast('Accepted: ' + p.subject, 'good'); BT.rerender(); }));
    $$('[data-pr]', el).forEach((b) => (b.onclick = () => { const p = list.find((x) => x.id === b.dataset.pr); p.state = 'rejected'; p.decided = { actor: 'owner', at: Date.now() }; BT.audit('owner', 'Proposal rejected', p.subject); BT.save(); BT.toast('Rejected. It will not be proposed again for 30 days.'); BT.rerender(); }));
  });

  /* =========================== PLAN REVIEW — banners, flags, briefing, alerts (P8-14, P9-06, P7-20) =========================== */
  function briefingText() {
    const S = BT.S, p = S.plan, prim = p.items.filter((i) => !i.reserve), res = p.items.filter((i) => i.reserve);
    const gate = p.gate ? p.gate.status : 'normal';
    const line = (i) => `- ${i.symbol} ${i.side}, ${i.strategy} ${i.tf}: stop ${num(i.stop)}, target ${num(i.target)}, P(win) ${Math.round(i.pWin * 100)}%.`;
    return `Plan for ${fmtDate(new Date(p.date))}, ${p.status || 'proposed'}. Market gate: ${gate}.\n\n` + (prim.length ? `Primary tickets (${prim.length}):\n${prim.slice(0, 5).map(line).join('\n')}` : 'No primary ticket. The market gate or the data checks produced none.') + (res.length ? `\n\nReserve (${res.length}): ${res.slice(0, 4).map((i) => i.symbol).join(', ')}.` : '') + '\n\nRisks: the cost sheet is not confirmed as yours; the open book is assumed from your settings.';
  }
  wrap('review', (el) => {
    tagHead(el, ['P8-14', 'P9-06', 'P7-20']);
    const P = K.P(); P.brief = P.brief || 'model';
    const S = BT.S, g = S.plan.gate, th = S.plan.throttle;
    const text = briefingText();
    const words = text.split(/\s+/).length;
    const modelOk = assistantOn() && P.brief === 'model';
    const rej = assistantOn() && P.brief === 'rejected';
    const label = modelOk ? 'Drafted by a local model from stored records; every number was checked against them.' : `Fixed-format text; the model was not used (${assistantOn() ? 'its text failed the checks twice' : 'the assistant is switched off'}).`;
    const flags = [['Market gate', g ? g.status : 'unavailable', g ? (g.status === 'no-trade' ? 'no-trade day, tickets moved to reserve' : g.status) : 'VIX and breadth not known'], ['Throttle', th && th.mult < 1 ? 'cut' : 'clear', th && th.mult < 1 ? 'cut' : 'clear'], ['Correlation', 'applied', 'applied'], ['Open book', 'assumed empty', 'assumed empty by owner setting'], ['Risk multiplier', '', String(th ? th.riskMult : 1)], ['Cost basis', 'costed', 'costed, sheet not confirmed as yours']];
    const failed = P.runFail;
    const html = (failed ? `<div class="banner warn" role="alert">${I('xCircle')}<div><b>No plan was published for ${fmtDate(new Date(S.plan.date))}:</b> the data-quality gate refused (attempt 2 at 18:42): <span class="mono">gate dq.freshness</span>, 1-minute bars of 3 instruments are older than the session. Started by the schedule. <span class="small muted">(Prototype: simulated; the plan below is the last published one.)</span></div></div>` : '')
      + K.card('Plan flags (stored with the plan) ' + K.spec('P8-14'), `<div class="card-b" style="display:grid;gap:8px"><div class="row" role="list">${flags.map(([a, , t]) => `<span class="chip" role="listitem">${I(a === 'Market gate' && g && g.status !== 'normal' ? 'alert' : 'shield')} ${a}: ${esc(t)}</span>`).join('')}</div>
        <div class="small muted">Drawn from the plan's stored fields (schema v1), not guessed from sentences. A plan that predates them would say "read from the basis text; this plan predates structured flags". <label class="check small" style="margin-left:8px"><input type="checkbox" id="pf-fail" ${failed ? 'checked' : ''}> Prototype only: pretend the nightly run failed closed</label></div></div>`)
      + K.card('Briefing ' + K.spec('P9-06'), `<div class="card-b" style="display:grid;gap:8px"><div class="row between"><span class="small muted">${esc(label)}</span><span class="small muted">${words} words of at most 300 · ${modelOk ? 'source: model' : 'source: deterministic'}</span></div>
        <div class="bubble" style="padding:10px 13px;border:1px solid var(--border);border-radius:12px;background:var(--surface-2)">${BT.assistantA.plain(text)}<p class="small muted">Nothing was applied: every item above is a proposal; approving is your own act.</p></div>
        ${rej ? `<details open><summary><b>Why the model text was not used</b></summary><ul class="insights"><li><span class="mono">number_not_in_facts</span>: "2.5" is not in the facts (character 212)</li><li><span class="mono">ticket_not_covered</span>: reserve ticket DIXON is not mentioned</li></ul></details>` : ''}
        <details><summary class="small">Facts used (ids)</summary><div class="small muted">plan ${esc(S.plan.date)} · digest W42 · run keys 7f3a…, c91b… · facts hash <span class="mono">b2e4…91</span></div></details>
        <div class="row small"><span class="muted">Prototype only:</span><div class="seg" id="br-sim">${[['model', 'model text passed'], ['rejected', 'model text rejected']].map(([k, l]) => `<button data-b="${k}" class="${P.brief === k ? 'on' : ''}">${l}</button>`).join('')}</div></div></div>`)
      + `<div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Alerts on armed tickets and post-trade prompts ${K.spec('P7-20')}</h3></div><div class="card-b flush">${K.switchRow({ id: 'ticket_alerts', label: 'Price-crossing alerts and post-trade prompts', desc: 'Default OFF. When ON, the bell says once when an armed ticket\'s price crosses its indicative stop or target level, and after the session asks once whether you took the trade. Alert only: no order is placed and none exists. A stop is only an alert.' })}</div>
        ${K.on('ticket_alerts') ? `<div class="card-b">${K.table(['Ticket', 'Account', 'Prompt', 'Status'], S.plan.items.filter((i) => !i.reserve).slice(0, 3).map((i, k) => [`<b>${esc(i.symbol)}</b> ${i.side}`, 'Dhan · Main (paper)', 'Did you take this trade?', [K.st('empty', 'open: window 3 trading days'), '<span class="chip">matched via trade on record</span>', K.st('unknown', 'ambiguous: two tickets fit one trade; nothing guessed')][k % 3]]), { stack: true })}</div>` : ''}</div>`;
    afterHead(el, html);
    const f = $('#pf-fail', el); if (f) f.onchange = () => { K.P().runFail = f.checked; BT.save(); BT.rerender(); };
    $$('#br-sim button', el).forEach((b) => (b.onclick = () => { K.P().brief = b.dataset.b; BT.save(); BT.rerender(); }));
    K.bindSwitches(el, { ticket_alerts: { label: 'Price-crossing alerts and post-trade prompts', on: 'Starts watching armed tickets and asking after the session. It places no order and changes nothing.' } });
  });

  /* =========================== TODAY — execution strip (P12-10) =========================== */
  wrap('today', (el) => {
    tagHead(el, ['P12-10']);
    const sim = BT.exec.sim(), off = sim === 'off';
    afterHead(el, `<div class="banner ${off ? 'info' : 'warn'}">${I('send')}<div><b>Execution: ${off ? 'off' : 'on in this demo (' + sim + ')'}.</b> ${off ? 'Phase P12, off by default. No order exists in this system; armed means watched.' : 'Design preview of Execution: see the page. No order is sent by this prototype.'} <a href="#/execution" style="margin-left:auto">${off ? 'What it will look like' : 'Open Execution'} →</a></div></div>`);
  });

  /* =========================== STRATEGY LAB — draft with the assistant (P9-07) =========================== */
  const DR = { idea: '', res: null, busy: false };
  const SAMPLE_YAML = `id: vwap_rsi_trend_15m
timeframe: 15m
entry:
  after: "09:45"
  when:
    - close > vwap
    - rsi(14) > 55
    - ema(20, 1h) > ema(50, 1h)
stop: below_swing_low
exits:
  - kind: rr
    rr: 2
  - kind: time
    at: "15:15"
sell:            # the automatic BUY to SELL mirror is P3-02e and not built:
  when:          # a SELL needs its own explicit block
    - close < vwap
    - rsi(14) < 45
    - ema(20, 1h) < ema(50, 1h)`;
  function draftFor(idea) {
    if (/result|earning|news|sentiment|option|iv\b|tick|depth|fundamental|pe ratio/i.test(idea)) return { status: 'declined', unsupported: [/result|earning|news|sentiment/i.test(idea) ? 'events and news data (phase P15)' : '', /option|iv\b/i.test(idea) ? 'options (phase P14)' : '', /tick|depth/i.test(idea) ? 'tick or depth data (phase P13)' : '', /fundamental|pe ratio/i.test(idea) ? 'fundamentals (phase P15)' : ''].filter(Boolean) };
    return { status: 'valid', yaml: SAMPLE_YAML, understood: ['Intraday on the 15-minute chart, after 09:45.', 'Buy when a 15-minute candle closes above VWAP with RSI(14) above 55 and the 1-hour EMA20 above EMA50.', 'Stop below the last swing low; target 2R; exit by 15:15.', 'Mirror for shorts, written out explicitly.'], assumptions: [['exits[0].rr', '2', 'You said "1:2".'], ['exits[1].at', '15:15', 'You said "exit by 15:15".'], ['sell.when[1]', 'rsi(14) < 45', 'Chosen by the model as the mirror of 55; you gave no value. Check it.']], notExpressed: [['swing-low definition', 'the language has "below_swing_low" with a fixed 5-bar look-back; your exact swing rule cannot be set']], requestedTest: '2022 to today on MyChoice and Best stocks (shown as text; you choose the window and basket at Confirm)', checks: [['C1 valid', true, 'passes validate'], ['C2 families known', true, ''], ['C3 assumptions are true', true, '3 of 3 found in the text'], ['C4 only a draft', true, 'no version; id free'], ['C5 numbers from the idea', true, ''], ['C6 nothing dropped silently', true, '9:45, 15, 14, 55, 20, 50, 2, 15:15 all present']] };
  }
  wrap('lab', (el) => {
    tagHead(el, ['P9-07']);
    const on = assistantOn(), r = DR.res;
    const meta = { auto_lab_quick_test: { label: 'Auto quick test after a valid draft', on: 'A draft that is valid with no finding starts a quick test by itself. Recorded as automatic, never as your approval. A quick test is exploration, not a verdict.' } };
    const html = `<div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Draft with the assistant ${K.spec('P9-07')}</h3><span class="small muted">nothing is stored or run until you press Confirm</span></div><div class="card-b" style="display:grid;gap:10px">
      <label class="field" for="dr-idea">Describe a trading idea in plain English (10 to 2,000 characters)<textarea class="input" id="dr-idea" rows="3" maxlength="2000" style="font-family:var(--font)" placeholder="Intraday: after 9:45 buy when a 15-min candle closes above VWAP with RSI(14) above 55 and 1-hour EMA20 above EMA50. Stop below last swing low. Target 1:2. Exit by 15:15. Mirror for shorts.">${esc(DR.idea)}</textarea></label>
      <div class="row"><button class="btn primary" id="dr-go" ${on ? '' : 'disabled'}>${I('sparkles')} Draft strategy</button>${on ? '<span class="small muted">The local model drafts; the validator decides. At most 20 drafts a day.</span>' : K.st('unavailable', 'the assistant is switched off: turn it on in Assistant settings (owner act)')}${r ? '<button class="btn" id="dr-discard">Discard</button>' : ''}</div>
      ${DR.busy ? `<div class="working" role="status">${I('refresh')} Drafting and validating…</div>` : ''}
      ${r && r.status === 'declined' ? `<div class="st block">${I('ban')}<b>Declined</b><span>This idea cannot be written in the language today, because: ${r.unsupported.map(esc).join('; ')}. No strategy text was made; Confirm is not offered.</span></div>` : ''}
      ${r && r.status === 'valid' ? `<div class="grid g2"><div style="display:grid;gap:8px"><div><span class="chip green">${I('checkCircle')} valid</span> <span class="small muted">the YAML passes the validator; edit it and press Validate to re-check</span></div>
          <textarea class="input mono" id="dr-yaml" rows="17" aria-label="Strategy text">${esc(r.yaml)}</textarea><div class="err" id="dr-err" role="alert"></div>
          <div class="row"><button class="btn" id="dr-val">Validate</button><button class="btn good" id="dr-conf" ${r.invalid ? 'disabled' : ''}>Confirm…</button></div></div>
        <div style="display:grid;gap:10px"><div><b>What the model understood</b><ul class="insights">${r.understood.map((t) => `<li>${esc(t)}</li>`).join('')}</ul></div>
          <div><b>Values the model chose on its own</b>${K.table(['Field', 'Value', 'Why'], r.assumptions.map(([f, v, w]) => [`<span class="mono">${esc(f)}</span>`, esc(v), esc(w)]), { stack: true })}</div>
          <div><b>Not expressed</b>${r.notExpressed.map(([w, y]) => `<div class="chk-line">${I('alert')}<div>${esc(w)}: <span class="muted">${esc(y)}</span></div></div>`).join('')}</div>
          <div class="small muted">Requested test: ${esc(r.requestedTest)}</div>
          <div><b>Checks</b>${r.checks.map(ck).join('')}</div></div></div>` : ''}
      ${r && r.confirmed ? `<div class="banner good" style="margin:0">${I('checkCircle')}<div>Stored as library version <b>${esc(r.confirmed.id)}</b> (note: "Assistant draft D-31; confirmed by owner") and a quick test started on ${esc(r.confirmed.basket)}. <b>Run summary (deterministic, no model text):</b> state succeeded · instruments run 12, skipped 2 · trades 118, open at end 3 · labels: gross cost basis, exploration sample. No win rate, expectancy or verdict: a quick test is not a verdict.</div></div>` : ''}
      <div class="card-b flush" style="border-top:1px solid var(--border)">${K.switchRow({ id: 'auto_lab_quick_test', label: 'Auto quick test after a valid draft', desc: 'Default OFF = you press Confirm. The model can never start a run by its own act.' })}</div>
      ${K.evidence({ basis: 'the validator and the draft checks, not the model\'s say-so', population: 'your idea text only', limits: ['The model drafts new families only; editing an existing strategy is yours.', 'A draft is not stored: a crash loses it.'] })}</div></div>`;
    afterHead(el, html);
    K.bindSwitches(el, meta);
    const idea = $('#dr-idea', el); if (idea) idea.oninput = () => { DR.idea = idea.value; };
    const go = $('#dr-go', el); if (go) go.onclick = () => { if (DR.idea.trim().length < 10) return BT.toast('Write the idea in at least 10 characters'); DR.busy = true; DR.res = null; BT.rerender(); setTimeout(() => { DR.busy = false; DR.res = draftFor(DR.idea); BT.rerender(); }, 1400); };
    const dis = $('#dr-discard', el); if (dis) dis.onclick = () => { DR.res = null; BT.rerender(); };
    const val = $('#dr-val', el); if (val) val.onclick = () => { const t = $('#dr-yaml').value; if (!/^id: /m.test(t) || !/^entry:/m.test(t)) { DR.res.invalid = true; $('#dr-err').textContent = 'Not valid: entry (line 1) is missing. Not confirmable until it validates.'; $('#dr-conf').disabled = true; return; } DR.res.invalid = false; DR.res.yaml = t; $('#dr-err').textContent = ''; $('#dr-conf').disabled = false; BT.toast('Valid', 'good'); };
    const cf = $('#dr-conf', el); if (cf) cf.onclick = async () => {
      const r = await K.ownerAct({ title: 'Confirm the strategy draft', lead: 'Stores the text you see (re-validated now) as a normal library version and starts a quick test. Pick the universe; the window is the quick-test default.', okLabel: 'Store and run quick test', fields: `<label class="field">Basket (required)<select class="input" id="dc-b"><option value="">Choose…</option>${BT.S.baskets.filter((b) => b.purpose === 'trading').map((b) => `<option>${esc(b.name)}</option>`).join('')}</select></label><label class="check"><input type="checkbox" id="dc-run" checked> Start a quick test (exploration, not a verdict)</label>`, validate: (ov) => ($('#dc-b', ov).value ? '' : 'Choose a basket.'), collect: (ov) => ({ basket: $('#dc-b', ov).value }) });
      if (!r) return; DR.res.confirmed = { id: DR.res.yaml.match(/id: (\S+)/)[1] + ' v1', basket: r.basket }; BT.audit('owner', 'Strategy draft confirmed', DR.res.confirmed.id + ' (design only)'); BT.save(); BT.rerender();
    };
  });

  /* =========================== LEARNING — journey, smart rules, tweak, owner acts (P8-12, P8-13, P8-18, P8-19) =========================== */
  wrap('learning', (el) => {
    tagHead(el, ['P8-02', 'P8-12', 'P8-13', 'P8-18', 'P8-19']);
    const NE = [[/live expectancy/i, 'no live trades exist until paper and live trading (P10/P11)'], [/learning uplift/i, 'needs 3 judged counterfactual windows (2 stored)'], [/execution gap/i, 'no fills yet: arrives with P10/P11']];
    $$('.tiles .tile', el).forEach((t) => { const l = (($('.l', t) || {}).textContent || ''); const hit = NE.find(([re]) => re.test(l)); if (hit) { $('.v', t).innerHTML = K.st('not_evaluable', ''); $('.s', t).textContent = hit[1]; } });
    const tiles = $('.tiles', el); if (tiles) tiles.insertAdjacentHTML('afterend', '<div class="small muted" style="margin:-8px 0 14px">Tiles show the honest state today; the charts and tables below use demo values to show the screen with data.</div>');
    const tab = (($('#lr-tabs .on', el) || {}).dataset || {}).tab, body = $('#lr-body', el);
    if (!body) return;
    if (tab === 'journey') body.insertAdjacentHTML('afterbegin', K.card('Learning versus a frozen model: the stored counterfactual ' + K.spec('P8-12'), `<div class="card-b" style="display:grid;gap:10px"><div class="st block" style="font-size:14px">${I('help')}<b>Inconclusive</b><span>2 judged windows, 3 needed. No gain is called an improvement.</span></div>
      ${K.table(['Window', '>Frozen take-R', '>Retrained take-R', '>Rows', 'Uplift'], [['Q3 2026', '0.10', '0.16', 40, `${R(2.4, 1)} <span class="small muted">n 40</span>`], ['Q2 2026', '0.12', '0.09', 50, `${R(-1.5, 1)} <span class="small muted">n 50</span>`], ['Q1 2026', '—', '—', '—', K.st('not_evaluable', 'same model on both sides, not judged')]], { stack: true })}
      <div>Cumulative uplift over judged windows: <b class="num">${R(0.9, 1)}</b> <span class="small muted">weak: 2 windows; not shown as a gain</span></div>
      <div class="small muted">Lessons are drawn as bands from when they were noticed to when closed; a model registration is a pin with the first 8 hex of its hash. With no counterfactual stored the tab would say "empty" with the command to run: <span class="mono">scripts/xd.sh learn counterfactual --run RUNKEY:STYLE:EXIT --store</span>.</div>
      ${K.evidence({ basis: 'gross, costed on a stored sheet not confirmed as yours', population: 'run keys behind the report (stored)', n: 90, range: 'last 3 quarters', limits: ['Gross and costed runs are never mixed in one store.', 'Every figure is a server field; the browser computes no statistic.'] })}</div>`));
    if (tab === 'smart') {
      const all = BT.tradesData().filter((t) => t.taken && t.R <= 0);
      const by = {}; all.forEach((t) => t.tags.forEach((g) => { (by[g] = by[g] || []).push(t.R); }));
      const tags = Object.entries(by).map(([tag, a]) => ({ tag, n: a.length, lost: sum(a) })).sort((a, b) => a.lost - b.lost);
      const rules = [['Market gate', 'VIX and breadth buckets need market context (P3-01d/e)'], ['Equity-curve throttle', 'needs closed paper trades of the epoch'], ['Meta-labelling', 'needs graded plan outcomes'], ['Entry windows', 'study stored; out-of-sample lift below'], ['Correlation', 'needs paired daily closes (3 pairs partial)']];
      body.insertAdjacentHTML('beforeend', K.card('Evidence per rule ' + K.spec('P8-13'), K.table(['Rule', '>Plans read', '>Unavailable', '>Moved to reserve', 'Outcome'], rules.map(([r, why], i) => [r, 1, i === 4 ? 1 : 0, i === 0 ? 0 : '—', K.st('not_evaluable', why)]), { stack: true }) + '<div class="card-b small muted">With no plan stored every row would read empty: "no plan stored yet (the nightly service is paused until the owner starts it)", and no digit.</div>', '', 'flush')
        + K.card('Why trades lost: R lost per tag', `<div class="card-b">${K.table(['Tag', '>Losses', '>R lost', 'Growing?'], tags.map((t) => [esc(t.tag), t.n, R(t.lost, 1), K.st('not_evaluable', 'needs 60 losses (two windows of 30)')]), { stack: true })}<div class="small muted" style="margin-top:6px">Tags overlap, so per-tag sums exceed the total loss. A cost-only loss is untagged.</div></div>`)
        + K.card('Learned entry windows (stored study)', `<div class="card-b">${K.table(['Cell', 'Window', '>Window trades', '>Lift', 'Folds held', 'Status'], [['ORB 15m BUY', '09:30 to 10:45', K.metric({ state: 'value', v: 62, n: null }, (v) => v), K.metric({ state: 'insufficient', reason: 'n 62 of 80 for a lift' }), '4 of 5 needed', 'proposed'], ['VWAP 5m BUY', '10:00 to 11:30', 18, K.st('insufficient', 'n 18 of at least 30'), '—', 'not enough evidence'], ['EMA 30m SELL', '13:00 to 14:30', 54, K.st('not_evaluable', 'no out-of-sample lift'), '1 of 5', 'rejected']], { stack: true })}</div>`)
        + K.evidence({ basis: 'stored plans and runs; a rule without evidence says so', population: 'plans in the last 30, trades of the stored runs', limits: ['Pooled out-of-sample lift is shown only when every fold is evaluable.', 'No rule is scored or ranked here.'] }));
    }
    if (tab === 'experiments') {
      const on = K.on('experiments_ui');
      const prev = K.card('Before you run: the replay preview ' + K.spec('P8-18'), `<div class="card-b" style="display:grid;gap:8px"><dl class="kv"><dt>Next trial number</dt><dd>7</dd><dt>Required improvement</dt><dd>0.04 R per trade in at least 5 of the usable folds</dd><dt>Usable folds</dt><dd>3 <span class="small muted">(6 needed)</span></dd><dt>Best possible outcome</dt><dd>${K.st('insufficient', 'Inconclusive: fewer than 6 folds exist')}</dd></dl>
        <label class="check"><input type="checkbox" id="tw-ack"> I understand a run counts as a look and can at best be Inconclusive</label>
        <div class="small muted">Only entry-neutral knobs; a cheaper slippage than the baseline is refused ("a cheaper cost assumption cannot win"). Replay time is measured and printed. No control named apply exists anywhere.</div></div>`);
      if (!on) body.innerHTML = K.card('Tweak and re-run from the UI ' + K.spec('P8-18'), `<div class="card-b flush">${K.switchRow({ id: 'experiments_ui', label: 'experiments_ui capability', desc: 'Default OFF. While OFF this tab shows the state read-only and no start control is drawn. Starting a replay needs this switch, your password and a stored baseline run.' })}</div><div class="card-b"><div class="st block">${I('lock')}<b>Read only</b><span>The capability is OFF. See Settings → Owner acts for all switches.</span></div></div>`) + prev.replace(/<input type="checkbox" id="tw-ack">/, '<input type="checkbox" id="tw-ack" disabled>');
      else body.insertAdjacentHTML('afterbegin', prev + K.card('Capability', `<div class="card-b flush">${K.switchRow({ id: 'experiments_ui', label: 'experiments_ui capability', desc: 'ON: the form below can start one replay at a time (password per start).' })}</div>`));
      K.bindSwitches(body, { experiments_ui: { label: 'Tweak and re-run from the UI', on: 'Lets you start one replay at a time from this screen. It changes nothing in the trading configuration, plans, strategies, cells, models or autonomy.' } });
    }
    if (tab === 'lessons' || tab === 'autonomy') {
      const on = K.on('owner_acts');
      body.insertAdjacentHTML('afterbegin', `<div class="banner ${on ? 'auto' : 'info'}">${I('lock')}<div><b>Owner acts ${K.spec('P8-19')}:</b> ${on ? 'ON. Moves below need a reason and your password; each leaves an insert-only audit row.' : 'OFF (default). Close, dismiss or link a lesson, move a hypothesis, revert the latest cell transition and change autonomy are read-only here.'} <a href="#/settings" style="margin-left:auto">Settings → Owner acts</a></div></div>`);
    }
  });

  /* =========================== REPORTS — evidence, exports, trade replay (P8-08..11, P8-20) =========================== */
  function replay(t) {
    const prev = D.DAYS[Math.max(0, D.DAY_INDEX[t.date] - 1)];
    const m1 = [prev, t.date].flatMap((d) => D.intraday1m(t.symbol, d));
    const bars = D.aggregate(m1, '15m');
    const entryBar = bars.find((b) => new Date(b.t).toISOString().slice(0, 10) === t.date && new Date(b.t).getHours() >= (t.hour || 10)) || bars[Math.floor(bars.length * 0.6)];
    const dir = t.side === 'BUY' ? 1 : -1, entry = entryBar.open, risk = Math.max(entry * 0.008, 0.5), stop = entry - dir * risk, target = entry + dir * 2 * risk, exit = entry + dir * t.R * risk;
    const ei = bars.indexOf(entryBar), xi = Math.min(bars.length - 1, ei + (t.R > 0 ? 8 : 3)), xb = bars[xi];
    BT.drawer({ title: `Trade replay · ${esc(t.symbol)} ${t.side} · ${t.date} ${K.spec('P8-11')}`, body: `<div class="banner good" style="margin:0">${I('checkCircle')}<div><b>coherence ok:</b> the first bar at or after the entry minute opens within one tick of the stored entry price. (Demo bars.)</div></div>
      <div id="rpl-ch" class="chart" role="img" aria-label="Candles with entry, stop, target and exit"></div>
      <div class="row"><button class="btn sm" id="rpl-prev">Previous event</button><button class="btn sm" id="rpl-next">Next event</button><span class="small muted" id="rpl-live" role="status" aria-live="polite"></span></div>
      ${K.table(['Event', 'Time', '>Price', 'Share'], [['Entry', fmtTime(entryBar.t).slice(0, 5), num(entry), '100%'], ['Stop (stored)', '—', num(stop), '—'], ['Target (stored)', '—', num(target), '—'], ['Exit', fmtTime(xb.t).slice(0, 5), num(exit), '100%']])}
      <dl class="kv"><dt>R exact / filled</dt><dd>${R(t.planR)} / ${R(t.R)} <span class="small muted">slippage ${R(-t.slipR)}</span></dd><dt>MAE / MFE</dt><dd>${R(t.mae)} / ${R(t.mfe)}</dd><dt>Net R, charges</dt><dd>${K.st('provisional', 'costed on sheet v3, not confirmed as yours; gross shown above')}</dd></dl>
      <details open><summary><b>Decision-time features</b></summary>${K.table(['Group', 'Feature', 'Value'], [['bars', 'ATR(14) / close', '0.9%'], ['bars', 'relative volume', '1.8×'], ['plan', 'P(win) at decision', Math.round(t.pWin * 100) + '%'], ['context', 'India VIX bucket', K.st('unavailable', 'market context not built (P3-01d/e)')], ['context', 'breadth', K.st('unavailable', 'market context not built (P3-01d/e)')]])}</details>
      <details><summary class="small">Bars (${bars.length}), first 12</summary>${K.table(['Time', '>Open', '>High', '>Low', '>Close'], bars.slice(0, 12).map((b) => [fmtDT(b.t), num(b.open), num(b.high), num(b.low), num(b.close)]))}</details>
      <div class="small muted">Charts: <a href="https://www.tradingview.com/" rel="noopener" target="_blank">TradingView lightweight-charts attribution</a>. The chart is never the only carrier: the event table has the same numbers.</div>`,
      onMount: (ov, close, charts) => {
        const mk = [{ time: entryBar.t, position: dir > 0 ? 'belowBar' : 'aboveBar', color: '#6366f1', shape: dir > 0 ? 'arrowUp' : 'arrowDown', text: 'Entry' }, { time: xb.t, position: 'aboveBar', color: t.R > 0 ? '#10b981' : '#f43f5e', shape: 'circle', text: 'Exit' }];
        BT.candles($('#rpl-ch', ov), bars, { markers: mk, lines: [{ price: stop, color: '#f43f5e', title: 'stop' }, { price: target, color: '#10b981', title: 'target' }, { price: entry, color: '#6366f1', title: 'entry' }] }, charts);
        const ev = ['Entry ' + num(entry), 'Exit ' + num(exit)]; let k = 0;
        const say = (d) => { k = (k + d + 2) % 2; $('#rpl-live', ov).textContent = ev[k]; };
        $('#rpl-prev', ov).onclick = () => say(-1); $('#rpl-next', ov).onclick = () => say(1);
      } });
  }
  function exportRows(el) {
    const t = $('#rp-body table', el); if (!t) return null;
    const heads = $$('thead th', t).map((x) => x.textContent.trim());
    const rows = $$('tbody tr', t).slice(0, 5000).map((r) => $$('td', r).map((c) => c.textContent.trim()));
    return { heads, rows };
  }
  const csvCell = (v) => { let s = String(v).replace(/[\u0000-\u0008​-‏‪-‮]/g, ''); if (/^[\s]*[=+\-@\t\r\n＝＋－＠]/.test(s) && !/^[+-]?\d+([.,]\d+)?%?R?$/.test(s)) s = "'" + s; return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s; };
  wrap('reports', (el) => {
    tagHead(el, ['P8-08', 'P8-09', 'P8-10', 'P8-11', 'P8-20']);
    const host = $('#rp-body', el); if (!host) return;
    const n = (($('.tiles .tile .v', el) || {}).textContent || '').trim();
    const recent = BT.tradesData().filter((t) => t.taken).slice(-6).reverse();
    const on = K.on('exports');
    host.insertAdjacentHTML('afterend', K.evidence({ basis: 'gross trades of the stored simulation runs; costs only from a stored sheet, not confirmed as yours', population: 'run keys behind this view (stored), filtered as set above', range: `${$('#rp-from', el) ? $('#rp-from', el).value : ''} to ${$('#rp-to', el) ? $('#rp-to', el).value : ''}`, n: n || null, limits: ['A cell below the minimum count is hatched and shows insufficient, never a colour by value.', 'Many looks at the same data: a high figure can be chance. No composite score exists.', 'Hatched = insufficient data; regime and account dimensions are unavailable: market context is not built (P3-01d/e).'] })
      + K.card('Drill down: trade replay ' + K.spec('P8-11'), `<div class="card-b">${K.table(['Date', 'Instrument', 'Setup', '>R', ''], recent.map((t, i) => [t.date, `<b>${esc(t.symbol)}</b> ${t.side}`, `${esc(t.strategy)} ${t.tf}`, R(t.R), `<button class="btn sm" data-replay="${i}">Replay</button>`]), { stack: true })}<div class="small muted" style="margin-top:6px">A heat-map cell or basket row opens its trade list; one trade opens here, with its own bars, entry, stop, target and exit.</div></div>`)
      + K.card('Export the table you are looking at ' + K.spec('P8-20'), `<div class="card-b flush">${K.switchRow({ id: 'exports', label: 'exports capability', desc: 'Default OFF. When ON, a CSV or static HTML page with the evidence and limits inside the file. Nothing about accounts, holdings or balances is ever in it; the app records only the kind and the row count.' })}</div><div class="card-b row"><button class="btn" id="ex-csv" ${on ? '' : 'disabled'}>${I('download')} CSV</button><button class="btn" id="ex-html" ${on ? '' : 'disabled'}>${I('download')} HTML page</button>${on ? '<span class="small muted">6 a minute, 1 at a time; CSV above 100,000 rows is refused, never truncated</span>' : K.st('unavailable', 'exports are off (403 capabilities.off)')}</div>`));
    $$('[data-replay]', el).forEach((b) => (b.onclick = () => replay(recent[+b.dataset.replay])));
    K.bindSwitches(el, { exports: { label: 'Exports', on: 'Lets you download the current research table as CSV or HTML. The file holds the evidence and limits and nothing about your accounts.' } });
    const dl = (name, mime, text) => { const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: mime })); a.download = name; document.body.appendChild(a); a.click(); a.remove(); };
    const make = (html) => {
      const tb = exportRows(el); if (!tb) return BT.toast('No table in this view to export');
      const pre = ['# basis: gross trades of stored simulation runs; costs not confirmed as yours', '# population: run keys behind this view', `# n: ${n}`, `# table-rows: ${tb.rows.length}`, '# limits: many looks at the same data; cells below the minimum are insufficient'];
      const stamp = D.hash(tb.heads.join() + tb.rows.length).toString(16).slice(0, 8);
      if (!html) dl(`xd-report-${K.lastDay().replace(/-/g, '')}-${stamp}.csv`, 'text/csv;charset=utf-8', pre.join('\n') + '\n' + [tb.heads, ...tb.rows].map((r) => r.map(csvCell).join(',')).join('\n') + `\n# end of export, rows ${tb.rows.length}\n`);
      else dl(`xd-report-${K.lastDay().replace(/-/g, '')}-${stamp}.html`, 'text/html;charset=utf-8', `<!doctype html><meta charset=utf-8><title>xDrishti export</title><pre>${esc(pre.join('\n'))}</pre><table border=1><tr>${tb.heads.map((h) => `<th>${esc(h)}</th>`).join('')}</tr>${tb.rows.map((r) => `<tr>${r.map((c) => `<td>${esc(c)}</td>`).join('')}</tr>`).join('')}</table>`);
      BT.audit('owner', 'Report exported', `kind=report-groups format=${html ? 'html' : 'csv'} rows=${tb.rows.length}`); BT.save();
    };
    const c = $('#ex-csv', el), h = $('#ex-html', el); if (c) c.onclick = () => make(false); if (h) h.onclick = () => make(true);
  });

  /* =========================== ACCOUNTS — execution mode + readiness (P12-07, P12-11, P11-04) =========================== */
  wrap('accounts', (el) => {
    tagHead(el, ['P12-07', 'P12-11', 'P11-04']);
    const tab = (($('#ac-tabs .on', el) || {}).dataset || {}).tab, sel = $('.acc-item.sel', el), box = $('.split-detail .card-b', el);
    if (!sel || !box) return;
    const a = BT.S.accounts.find((x) => x.id === sel.dataset.acc);
    if (tab === 'trading' && a.roles.trading.enabled) {
      const o = $('#tr-mode option[disabled]', el); if (o) o.textContent = 'assisted / semi_auto / auto_live: changed on the Execution screen';
      const m = a.roles.trading.mode;
      box.insertAdjacentHTML('beforeend', `<div class="card" style="grid-column:1/-1"><div class="card-h"><h3>Execution mode ${K.spec('P12-07')}</h3><a class="btn sm" href="#/execution?tab=modes">Open Modes</a></div><div class="card-b" style="display:grid;gap:8px">
        <div class="ladder-modes">${['paper', 'manual_live', 'assisted', 'semi_auto', 'auto_live'].map((x, i) => `<div class="rung ${x === m ? 'on' : ''} ${i > 1 ? 'future' : ''}"><b>${x.replace('_', ' ')}</b>${x === m ? 'effective' : i > 1 ? 'Phase P12' : ''}</div>`).join('')}</div>
        <div class="small muted">Automated modes are changed on the Execution screen, with evidence, a cooling-off and your password twice. Saving here with mode assisted, semi_auto or auto_live is refused (accounts.invalid). A role switched off, an archived account or a changed client id demotes the account to paper.</div>
        ${m === 'manual_live' ? `<div>Risk step: <b>S1</b> (25% of intended risk) · loss halt: ${K.st('empty', 'none open')} · <a href="#/paper?tab=live">Ramp and halts</a></div>` : m === 'paper' ? `<div>Paper phase check: ${K.st('not_evaluable', 'n of 60 counted trades')} · <a href="#/paper?tab=phase">open</a></div>` : ''}</div></div>`);
    }
    if (tab === 'connection') {
      box.insertAdjacentHTML('beforeend', `<div class="card" style="grid-column:1/-1"><div class="card-h"><h3>Execution readiness ${K.spec('P12-11')}</h3><a class="btn sm" href="#/execution">Open Today</a></div><div class="card-b" style="display:grid;gap:6px"><div>${K.st('unavailable', 'execution is off: the token cover, the broker\'s profile and the static IP are not probed', true)}</div><div class="small muted">When execution is on, one record per day says at 09:10:30 IST whether the token covers the session (until 16:00), the broker answered, the static IP is configured and attested, and the feed is connected. Fail closed: a missing or short token stops new orders and says why. Runbook: login, 09:10 failure, invalid IP (DH-905), IP change (seven-day rule), quarterly re-check.</div></div></div>`);
    }
  });

  /* =========================== SETTINGS — owner acts, phone / install (P8-19, P8-20, P8-21, P7-20) =========================== */
  wrap('settings', (el) => {
    tagHead(el, ['P8-19', 'P8-20', 'P8-21']);
    const meta = { owner_acts: { label: 'Owner acts', on: 'Lets you record lesson, hypothesis and cell-state moves and change autonomy from the app. Each needs a reason and your password; nothing can apply a change or place an order.' }, experiments_ui: { label: 'Tweak and re-run' }, exports: { label: 'Exports' }, pwa: { label: 'Installable phone app', on: 'Registers the offline shell after sign-in. The phone keeps no trading data.' }, phone_access: { label: 'Phone access over a private tunnel', on: 'An owner security act: the Mac still listens only on 127.0.0.1 and the tunnel is yours to set up.' } };
    const html = `<div class="card" style="margin:16px 0"><div class="card-h"><h3>Owner acts and capabilities</h3><span class="spec-row" style="margin:0">${K.specs(['P8-19', 'P8-18', 'P8-20'])}</span></div><div class="card-b flush">
        ${K.switchRow({ id: 'owner_acts', label: 'Owner acts: lessons, hypotheses, cell-state revert, autonomy', desc: 'Six writes with a reason and a fresh password each. Close takes no number: the result is read from the stored experiment.' })}
        ${K.switchRow({ id: 'experiments_ui', label: 'Tweak and re-run from the UI', desc: 'One replay at a time; judged by the ADR 0072 ledger; can at best be Inconclusive today.' })}
        ${K.switchRow({ id: 'exports', label: 'Exports (CSV, HTML)', desc: 'Evidence and limits inside the file; nothing about accounts.' })}
        ${K.switchRow({ id: 'ticket_alerts', label: 'Price-crossing alerts and post-trade prompts', desc: 'Alert only; no order is placed and none exists.' })}</div></div>
      ${K.owner_acts_forms ? '' : ''}${K.on('owner_acts') ? `<div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Record a move</h3></div><div class="card-b row"><button class="btn" id="oa-hyp">Move a hypothesis…</button><button class="btn" id="oa-les">Close or dismiss a lesson…</button><button class="btn" id="oa-rev">Revert the latest cell transition…</button></div></div>` : ''}
      <div class="card" style="margin-bottom:16px"><div class="card-h"><h3>Phone and install ${K.spec('P8-21')}</h3></div><div class="card-b flush">
        ${K.switchRow({ id: 'phone_access', label: 'Phone access over a private network', desc: 'Default OFF. The Mac listens on 127.0.0.1 only; a second local port (8081) behind your own encrypted tunnel carries the same app. Setting up the tunnel is your security act.' })}
        ${K.switchRow({ id: 'pwa', label: 'Installable app (add to home screen)', desc: 'Default OFF. A worker caches only an offline page; it never touches data, the API or the live stream.' })}</div>
        <div class="card-b" style="display:grid;gap:6px"><div class="st block">${I('smartphone')}<b>Offline page</b><span>"You are offline. xDrishti shows no data without a connection and keeps none on this device." A "new version is ready" snackbar offers Reload; nothing reloads by itself.</span></div><div class="small muted">Review a plan before the cutoff from the phone; the phone holds no trading data.</div></div></div>`;
    el.insertAdjacentHTML('beforeend', html);
    K.bindSwitches(el, meta);
    const forms = { 'oa-hyp': ['Move a hypothesis', 'Legal moves: proposed → approved | rejected | expired; approved → rejected | expired; running → expired; judged → accepted | rejected | expired. Others are refused (hypotheses.move_refused). Accept needs a stored experiment with outcome Better.', '<label class="field">Move to<select class="input" id="oa-to"><option>approved</option><option>rejected</option><option>expired</option><option>accepted</option></select></label>'], 'oa-les': ['Close or dismiss a lesson', 'Close takes no number: the result is read from the fix\'s stored experiment (improved only when it was Better, otherwise no_measurable_change).', '<label class="field">Action<select class="input"><option>close</option><option>dismiss</option><option>link to hypothesis</option></select></label>'], 'oa-rev': ['Revert the latest cell transition', 'Only the latest row of a cell can be reverted, only a transition. A revert of a revert, a restore to Active and un-retiring are refused (changelog.not_revertible).', ''] };
    Object.entries(forms).forEach(([id, [t, lead, f]]) => { const b = $('#' + id, el); if (b) b.onclick = async () => { const r = await K.ownerAct({ title: t, lead: esc(lead), fields: f }); if (r) { BT.audit('owner', t, 'insert-only row written; names and states only'); BT.save(); BT.toast(t + ': recorded', 'good'); } }; });
  });

  /* =========================== AUDIT LOG — six sources, filters, partial reads, redaction (P8-22) =========================== */
  const AU = { src: new Set(), cat: '', out: '', actor: '', q: '', from: 30, limit: 50, failSrc: false, init: false };
  const SRCS = [['auth', 'Sign-ins'], ['account', 'Accounts and services'], ['approval', 'Plan decisions'], ['plan', 'Plans'], ['autonomy', 'Autonomy'], ['learning', 'Learning']];
  function srcOf(a) { const t = a.action + ' ' + a.detail; if (/login|logout|lock|re-auth|password|lockout/i.test(t)) return 'auth'; if (/approv|reject|suggest|proposal|ticket/i.test(t)) return 'approval'; if (/autonomy|auto-/i.test(t)) return 'autonomy'; if (/lesson|hypothes|experiment|learn|retrain|model|digest/i.test(t)) return 'learning'; if (/plan|nightly|pre-?open/i.test(t)) return 'plan'; return 'account'; }
  const catOf = (a, s) => ({ auth: 'security', account: /service/i.test(a.action) ? 'service' : 'configuration', approval: 'decision', plan: 'decision', autonomy: 'configuration', learning: 'learning' }[s]);
  const outOf = (a) => (/fail|wrong|refus|lockout|denied/i.test(a.action + a.detail) ? 'refused' : 'ok');
  const actorOf = (a) => (a.actor === 'owner' ? 'owner' : a.actor === 'system' ? 'system' : /^rule/.test(a.actor) ? 'rule' : /^cli/.test(a.actor) ? 'cli' : 'other');
  const redact = (s) => { let hit = false; const o = String(s).replace(/(password|token|secret|client_id|bearer)\s*[=:]?\s*[A-Za-z0-9._\-]{4,}/gi, () => { hit = true; return '[redacted]'; }); return [o, hit]; };
  BT.pages.audit = function (el) {
    const S = BT.S;
    if (!AU.init) { AU.init = true; const qs = new URLSearchParams(location.hash.split('?')[1] || ''); if (qs.get('q')) AU.q = qs.get('q'); if (qs.get('actor')) AU.actor = qs.get('actor'); }
    const all = S.audit.map((a) => { const s = srcOf(a); return Object.assign({ s, c: catOf(a, s), o: outOf(a), ac: actorOf(a) }, a); });
    const cut = Date.now() - AU.from * DAY;
    const fail = AU.failSrc ? 'learning' : null;
    const list = all.filter((a) => a.at >= cut && a.s !== fail && (!AU.src.size || AU.src.has(a.s)) && (!AU.cat || a.c === AU.cat) && (!AU.out || a.o === AU.out) && (!AU.actor || a.ac === AU.actor) && (AU.q.length < 3 || (a.action + ' ' + a.detail + ' ' + a.actor).toLowerCase().includes(AU.q.toLowerCase())));
    const shown = list.slice(0, AU.limit);
    const unread = fail ? 1 : 0;
    const url = () => { const p = new URLSearchParams(); if (AU.q.length >= 3) p.set('q', AU.q); if (AU.actor) p.set('actor', AU.actor); history.replaceState(null, '', '#/audit' + (p.toString() ? '?' + p : '')); };
    const rowsHtml = shown.map((a, i) => {
      const [d, h] = redact(a.detail);
      const srcName = (SRCS.find((x) => x[0] === a.s) || [])[1] || a.s;
      const outcome = '<span class="st">' + I(a.o === 'ok' ? 'checkCircle' : 'xCircle') + '<b>' + (a.o === 'ok' ? 'OK' : 'Refused') + '</b></span>';
      const actorChip = '<span class="chip ' + (a.ac === 'system' ? 'purple' : a.ac === 'owner' ? 'blue' : '') + '">' + esc(a.ac) + '</span>';
      const summary = '<b>' + esc(a.action) + '</b> <span class="muted">' + esc(d) + '</span>' + (h ? ' <span class="chip amber">redacted</span>' : '');
      return [fmtDT(a.at), esc(srcName), a.c, outcome, actorChip, summary, '<button class="btn sm ghost" data-i="' + i + '">Details</button>'];
    });
    const more = list.length > AU.limit ? '<div class="grid-foot"><button class="btn sm" id="au-more">Load more</button><span class="small muted">' + AU.limit + ' of ' + list.length + '</span></div>' : '';
    const entriesHtml = K.card('Entries', shown.length ? K.table(['When (IST)', 'Source', 'Category', 'Outcome', 'Actor', 'Summary', ''], rowsHtml, { stack: true }) + more : '<div class="card-b">' + K.st('empty', 'No entries in this range', true) + '</div>', '', 'flush');
    el.innerHTML = K.head('Audit log', 'What happened, when, and who or what did it: sign-ins, security changes, configuration, service actions, plan decisions, autonomy and learning changes. Read only; no secret appears in any row.', ['P8-22'])
      + (unread ? `<div class="banner warn" role="alert">${I('alert')}<div><b>${unread} of 6 records could not be read</b> (learning: <span class="mono">read_failed</span>). This page is not complete.</div></div>` : '')
      + `<div class="card" style="margin-bottom:12px"><div class="card-b" style="display:grid;gap:10px"><div class="row"><div class="seg" id="au-r" role="group" aria-label="Range">${[[1, '24 h'], [7, '7 d'], [30, '30 d'], [90, '90 d']].map(([d, l]) => `<button data-d="${d}" class="${AU.from === d ? 'on' : ''}">${l}</button>`).join('')}</div>
        <div class="row" role="group" aria-label="Source">${SRCS.map(([k, l]) => { const bad = k === fail; return `<button class="chip ${AU.src.has(k) ? 'blue' : ''}" data-s="${k}" aria-pressed="${AU.src.has(k)}" style="border:0;cursor:pointer">${bad ? I('ban') : ''} ${l}${bad ? ' · unavailable' : ''}</button>`; }).join('')}</div></div>
        <div class="row"><select class="input" id="au-c" style="width:auto" aria-label="Category"><option value="">All categories</option>${['security', 'configuration', 'service', 'decision', 'learning'].map((c) => `<option ${AU.cat === c ? 'selected' : ''}>${c}</option>`).join('')}</select>
          <select class="input" id="au-o" style="width:auto" aria-label="Outcome"><option value="">Any outcome</option>${['ok', 'refused'].map((c) => `<option ${AU.out === c ? 'selected' : ''}>${c}</option>`).join('')}</select>
          <select class="input" id="au-a2" style="width:auto" aria-label="Actor"><option value="">Anyone</option>${['owner', 'system', 'rule', 'cli', 'other'].map((c) => `<option ${AU.actor === c ? 'selected' : ''}>${c}</option>`).join('')}</select>
          <input class="input" id="au-q" placeholder="Search (3 or more characters)" style="width:230px" value="${esc(AU.q)}" aria-label="Search text"><span class="small muted">Showing ${shown.length} of ${list.length}${list.length > AU.limit ? '' : ''} · sources read ${6 - unread} of 6</span><span class="spacer"></span>
          <label class="check small"><input type="checkbox" id="au-fail" ${AU.failSrc ? 'checked' : ''}> Prototype only: pretend one record cannot be read</label></div></div></div>
      ${entriesHtml}`
      + '<div class="small muted" style="margin-top:8px">No export or copy-all control exists on purpose. Rows are insert-only elsewhere; this screen writes nothing, not even an audit row for being read.</div>';
    $$('#au-r button', el).forEach((b) => (b.onclick = () => { AU.from = +b.dataset.d; BT.rerender(); }));
    $$('[data-s]', el).forEach((b) => (b.onclick = () => { AU.src.has(b.dataset.s) ? AU.src.delete(b.dataset.s) : AU.src.add(b.dataset.s); BT.rerender(); }));
    $('#au-c', el).onchange = (e) => { AU.cat = e.target.value; BT.rerender(); }; $('#au-o', el).onchange = (e) => { AU.out = e.target.value; BT.rerender(); }; $('#au-a2', el).onchange = (e) => { AU.actor = e.target.value; url(); BT.rerender(); };
    $('#au-q', el).onchange = (e) => { AU.q = e.target.value.trim(); url(); BT.rerender(); }; $('#au-fail', el).onchange = (e) => { AU.failSrc = e.target.checked; BT.rerender(); };
    const m = $('#au-more', el); if (m) m.onclick = () => { AU.limit += 50; BT.rerender(); };
    $$('[data-i]', el).forEach((b) => (b.onclick = () => { const a = shown[+b.dataset.i]; const [d] = redact(a.detail); BT.drawer({ title: esc(a.action), body: `<dl class="kv"><dt>When</dt><dd>${fmtDT(a.at)} IST</dd><dt>Source</dt><dd>${esc(a.s)}</dd><dt>Category</dt><dd>${a.c}</dd><dt>Outcome</dt><dd>${a.o}</dd><dt>Actor</dt><dd>${esc(a.actor)}</dd><dt>Detail</dt><dd>${esc(d)}</dd></dl><div class="small muted">Server-built labelled fields. Any token, password or client id in a stored row is shown as [redacted].</div>` }); }));
  };

  /* =========================== LEARNING DIGEST — weekly review text (P9-06) =========================== */
  const origDigest = BT.openDigest;
  BT.openDigest = function () {
    origDigest();
    try {
      const b = $('.modal.wide .dlg-b'); if (!b) return;
      const on = assistantOn();
      b.insertAdjacentHTML('beforeend', `<div class="card"><div class="card-h"><h3>Weekly review text ${K.spec('P9-06')}</h3><span class="small muted">at most 400 words</span></div><div class="card-b" style="display:grid;gap:6px"><div class="small muted">${on ? 'Drafted by a local model from stored records; every number was checked against them.' : 'Fixed-format text; the model was not used (the assistant is switched off).'}</div>
        <p style="margin:0">Week 43: the digest lists the changes above. Each insight keeps its n. Nothing was applied: every item above is a proposal; applying any of it is the owner's own act.</p></div></div>`);
    } catch (e) { /* ignore */ }
  };
})();
