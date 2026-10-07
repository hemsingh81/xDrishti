/* xDrishti prototype — pages: Today, Plan review, Suggestions, Portfolio */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const { $, $$, esc, inr, num, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);

  /* ---------- shared ticket helpers ---------- */
  const STATUS = {
    proposed: ['amber', 'Awaiting review'], approved: ['blue', 'Approved'], modified: ['purple', 'Approved (modified)'],
    rejected: ['red', 'Rejected'], lapsed: ['', 'Lapsed'], armed: ['green', 'Armed · watching'], invalidated: ['red', 'Invalidated'],
    triggered: ['green', 'Triggered · in trade'], closed: ['', 'Closed'], expired: ['', 'Expired'],
  };
  const chip = (st) => { const [c, l] = STATUS[st] || ['', st]; return `<span class="chip ${c}">${l}</span>`; };
  BT.ticketChip = chip;
  const decidedCount = () => BT.S.plan.items.filter((i) => ['approved', 'modified', 'armed', 'triggered', 'closed'].includes(i.status)).length;
  function cutoffInfo() {
    const S = BT.S;
    const [y, m, d] = S.plan.date.split('-').map(Number);
    const [h, mi] = S.settings.reviewCutoff.split(':').map(Number);
    const cut = new Date(y, m - 1, d, h, mi).getTime();
    const left = cut - Date.now();
    return { cut, left, text: left > 0 ? `${Math.floor(left / 3600000)}h ${Math.floor((left % 3600000) / 60000)}m left` : 'cutoff passed' };
  }
  BT.cutoffInfo = cutoffInfo;

  /* ================= TODAY ================= */
  BT.pages.today = function (el) {
    const S = BT.S;
    const open = BT.positions().filter((p) => p.status === 'open');
    const value = sum(open, (p) => p.value);
    const dayPnl = sum(open, (p) => p.dir * p.qty * (p.ltp - D.prevClose(p.symbol)));
    const items = S.plan.items;
    const pending = items.filter((i) => i.status === 'proposed' && !i.reserve).length;
    const svcOk = S.services.filter((s) => s.lastOutcome !== 'failed').length;
    const ci = cutoffInfo();
    const da = BT.dataAccount();
    el.innerHTML = `
      <div class="page-head"><div><h1>Good ${new Date().getHours() < 12 ? 'morning' : new Date().getHours() < 17 ? 'afternoon' : 'evening'}</h1>
        <p>${fmtDate(Date.now())} · plan for <b>${fmtDate(new Date(S.plan.date))}</b> · data from ${da ? esc(da.name) : '⚠ no data account'}</p></div>
        <div class="row"><a class="btn" href="#/services">Services</a><a class="btn primary" href="#/review">Review plan</a></div></div>
      ${pending ? `<div class="banner warn"><b>${pending} proposed ticket(s) await your decision.</b>&nbsp;Review cutoff ${S.settings.reviewCutoff} (${ci.text}). Unreviewed tickets ${S.settings.unreviewedPolicy === 'lapse' ? 'lapse and are not armed' : 'are armed automatically (top N)'}. <a href="#/review" style="margin-left:auto">Review now →</a></div>` : ''}
      <div class="tiles">
        <div class="tile"><div class="l">Portfolio value</div><div class="v num" id="t-value">${inr(value)}</div><div class="s">${open.length} open positions</div></div>
        <div class="tile"><div class="l">Day change</div><div class="v num ${sgn(dayPnl)}" id="t-day">${inr(dayPnl)}</div><div class="s">live + polled quotes</div></div>
        <div class="tile"><div class="l">Plan (${fmtDate(new Date(S.plan.date))})</div><div class="v">${decidedCount()} / ${S.settings.maxPlanItems}</div><div class="s">approved · ${pending} pending · max ${S.settings.maxNewEntries} entries</div></div>
        <div class="tile"><div class="l">Live trades</div><div class="v" id="t-live">${items.filter((i) => i.status === 'armed' || i.status === 'triggered').length}</div><div class="s">armed or in trade</div></div>
        <div class="tile"><div class="l">Services</div><div class="v">${svcOk}/${S.services.length}</div><div class="s">healthy · run without login</div></div>
      </div>
      ${(() => {
        const ins = BT.portfolioInsights ? BT.portfolioInsights(BT.positions().filter((p) => BT.accountsWith('portfolio').some((a) => a.id === p.account))) : [];
        const v = BT.mem.validation; const iss = BT.scheduleIssues ? BT.scheduleIssues() : [];
        const tok = S.accounts.filter((a) => a.token.status !== 'valid');
        const items = [
          [pending, `${pending} plan ticket(s) to review`, '#/review'], [ins.filter((i) => i.sev === 'high').length, `${ins.filter((i) => i.sev === 'high').length} high-priority portfolio action(s)`, '#/portfolio'],
          [v ? v.fail : 0, `${v ? v.fail : 0} instrument(s) failed data validation`, '#/quality'], [iss.filter((i) => i.level === 'error').length, `${iss.filter((i) => i.level === 'error').length} schedule conflict(s)`, '#/services'],
          [tok.length, `${tok.length} account token(s) need attention`, '#/accounts'], [BT.learningPending ? BT.learningPending() : 0, `${BT.learningPending ? BT.learningPending() : 0} learning change(s) to approve`, '#/learning?tab=autonomy'], [S.suggestions.filter((x) => x.status === 'pending').length, `${S.suggestions.filter((x) => x.status === 'pending').length} AI suggestion(s)`, '#/suggestions'],
        ].filter((x) => x[0]);
        return `<div class="card" style="margin-bottom:14px"><div class="card-h"><h3>Needs your attention</h3><span class="small muted">${items.length ? items.length + ' area(s)' : 'all clear'}</span></div><div class="card-b row">${items.map(([, t, h]) => `<a class="btn" href="${h}">${esc(t)} →</a>`).join('') || '<span class="small muted">Nothing needs a decision right now.</span>'}</div></div>`;
      })()}
      ${BT.learnSummary ? (() => { const L = BT.learnSummary(); return `<div class="card" style="margin-bottom:14px"><div class="card-h"><h3>Learning this week</h3><div class="row"><button class="btn sm" id="t-digest">${BT.icon('sparkles')} Weekly digest</button><a class="btn sm" href="#/learning">Learning →</a></div></div>
        <div class="card-b"><div class="mini-stats">
          <div><span class="muted small">vs frozen model</span><b class="pos">${L.uplift}</b><span class="small muted">per trade, last 8 weeks</span></div>
          <div><span class="muted small">Changed automatically</span><b>${L.autoWeek}</b><span class="small muted">${L.pending} waiting for you</span></div>
          <div><span class="muted small">Tomorrow's market gate</span><b class="${L.gate === 'normal' ? 'pos' : 'neg'}">${esc(L.gate)}</b><span class="small muted">risk ×${L.riskMult}</span></div>
          <div><span class="muted small">Most common loss tag</span><b>${esc(L.topTag[0])}</b><span class="small muted">${L.topTag[1]} losses this quarter</span></div>
          <div><span class="muted small">Graded on fills</span><b>${L.execGap}</b><span class="small muted">plan → actual gap</span></div>
        </div></div></div>`; })() : ''}
      <div class="grid g2">
        <div class="card"><div class="card-h"><h3>Live trades (today's armed tickets)</h3><span class="small muted" id="live-stamp"></span></div><div class="card-b flush" id="live-trades"></div></div>
        <div class="card"><div class="card-h"><h3>Active set · live feed</h3><span class="chip ${S.settings.demoMarketOpen ? 'green' : ''}">${S.settings.demoMarketOpen ? 'streaming' : 'stopped'}</span></div><div class="card-b" id="live-set"></div></div>
        <div class="card"><div class="card-h"><h3>AI briefing</h3><span class="small muted">generated ${ago(S.plan.generatedAt)}</span></div><div class="card-b"><p style="margin:0">${esc(S.plan.briefing)}</p>
          <div class="row" style="margin-top:10px"><a class="btn sm" href="#/suggestions">${S.suggestions.filter((s) => s.status === 'pending').length} suggestions in inbox</a></div></div></div>
        <div class="card"><div class="card-h"><h3>Recent alerts</h3><a class="small" href="#" id="all-notes">all</a></div><div class="card-b flush">${S.notifications.slice(0, 6).map((n) => `<div class="live-item" style="padding:8px 14px"><span>${esc(n.text)}</span><span class="small muted">${ago(n.at)}</span>${n.link ? `<a class="btn sm" href="${n.link}">Open</a>` : '<span></span>'}</div>`).join('') || '<div class="empty">No alerts yet — approve tickets and run the pre-open check to see live alerts.</div>'}</div></div>
      </div>`;
    $('#all-notes').onclick = (e) => { e.preventDefault(); $('#bell').click(); };
    if ($('#t-digest')) $('#t-digest').onclick = () => BT.openDigest();
    const renderLive = () => {
      const tk = S.plan.items.filter((i) => ['armed', 'triggered', 'closed'].includes(i.status));
      $('#live-trades').innerHTML = tk.length ? `<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Ticket</th><th>Status</th><th class="r">LTP</th><th class="r">Trigger</th><th class="r">Stop / Target</th><th class="r">P&L (R)</th></tr></thead><tbody>${tk.map((t) => {
        const p = BT.ltp(t.symbol); const risk = Math.abs(t.trigger - t.stop);
        const r = t.status === 'triggered' ? ((p - (t.fillEntry ?? t.entry)) * (t.side === 'BUY' ? 1 : -1)) / risk : t.status === 'closed' ? t.resultR : null;
        const dist = t.status === 'armed' ? pct((t.trigger - p) / p, 2) + ' away' : '';
        return `<tr><td><b>${esc(t.symbol)}</b> <span class="chip ${t.side === 'BUY' ? 'green' : 'red'}">${t.side}</span> <span class="small muted">${t.strategy} ${t.tf}</span></td><td>${chip(t.status)} <span class="small muted">${dist}</span></td>
          <td class="r num">${num(p)}</td><td class="r num">${num(t.trigger)}</td><td class="r num">${num(t.stop)} / ${num(t.target)}</td><td class="r num ${sgn(r)}">${r == null ? '—' : (r > 0 ? '+' : '') + r.toFixed(2) + 'R'}${t.status === 'closed' && t.planR != null ? `<div class="small muted">plan ${t.planR > 0 ? '+' : ''}${t.planR.toFixed(2)}R${t.tags ? ' · ' + esc(t.tags.join(', ')) : ''}</div>` : t.fillEntry ? `<div class="small muted">filled ${num(t.fillEntry)}</div>` : ''}</td></tr>`;
      }).join('')}</tbody></table></div>` : `<div class="empty">No armed tickets yet.<br>1) Approve tickets in <a href="#/review">Plan review</a> 2) Run <a href="#/services">Pre-open check & arming</a> — triggers then fire live.</div>`;
      const sets = BT.liveSets();
      $('#live-set').innerHTML = `<div class="small muted" style="margin-bottom:8px">WebSocket: <b>${sets.ws.length}</b> instruments (cap 200) · polled holdings: <b>${sets.poll.length}</b> every ${S.services.find((x) => x.id === 'holdings-refresh').sched.every} min</div>
        <div class="live-list">${sets.ws.slice(0, 12).map((s) => `<div class="live-item"><span>${esc(s)}</span><span class="num">${num(BT.ltp(s))}</span><span class="num small ${sgn(BT.dayChange(s))}">${pct(BT.dayChange(s), 2)}</span></div>`).join('')}</div>`;
      $('#live-stamp').textContent = BT.lastTick ? 'tick ' + fmtTime(BT.lastTick) : '';
      const v = sum(BT.positions().filter((p) => p.status === 'open'), (p) => p.value);
      $('#t-value').textContent = inr(v);
      $('#t-live').textContent = S.plan.items.filter((i) => i.status === 'armed' || i.status === 'triggered').length;
    };
    renderLive();
    BT.onTick = renderLive;
  };

  /* ================= PLAN REVIEW ================= */
  let reviewFilter = 'all';
  BT.pages.review = function (el) {
    const S = BT.S;
    const plan = S.plan;
    const ci = cutoffInfo();
    const primary = plan.items.filter((i) => !i.reserve);
    const reserve = plan.items.filter((i) => i.reserve);
    const counts = Object.fromEntries(Object.keys(STATUS).map((k) => [k, plan.items.filter((i) => i.status === k).length]));
    const shown = primary.filter((i) => reviewFilter === 'all' || (reviewFilter === 'pending' ? i.status === 'proposed' : reviewFilter === 'decided' ? i.status !== 'proposed' : true));
    el.innerHTML = `
      <div class="page-head"><div><h1>Plan review — ${fmtDate(new Date(plan.date))}</h1>
        <p>Proposed by the nightly pipeline ${ago(plan.generatedAt)} · ${primary.length} tickets + ${reserve.length} reserve · nothing is armed without your approval</p></div>
        <div class="row"><button class="btn" id="regen">Re-run nightly pipeline</button><button class="btn good" id="approve-all">Approve all within limits</button></div></div>
      <div class="banner ${ci.left > 0 ? 'info' : 'warn'}"><div><b>Review cutoff ${S.settings.reviewCutoff}</b> (${ci.text}). Unreviewed tickets will <b>${S.settings.unreviewedPolicy === 'lapse' ? 'lapse' : 'be armed (top N)'}</b>.
        Approved: <b>${decidedCount()}</b> of max <b>${S.settings.maxPlanItems}</b> tickets; at most <b>${S.settings.maxNewEntries}</b> may actually enter (counted on trigger). Only accounts with the <b>Trading</b> role receive allocations.</div></div>
      ${(() => { const g = plan.gate, th = plan.throttle; if (!g) return ''; const cls = g.status === 'no-trade' ? 'warn' : g.status === 'reduced' ? 'warn' : 'good';
        return `<div class="banner ${cls}">${BT.icon('shield')}<div><b>Smart rules:</b> market gate <b>${g.status === 'no-trade' ? 'NO-TRADE DAY' : g.status}</b>${g.status !== 'normal' ? ' (' + esc(g.factors.filter((f) => f.level !== 'normal').map((f) => f.label + ': ' + f.text).join('; ')) + ')' : ''} · risk <b>×${th.riskMult}</b>${th.mult < 1 ? ' (equity-curve throttle)' : ''} · ${plan.items.filter((x) => x.filtered).length} ticket(s) filtered by the meta-model / correlation · entry windows learned per setup. <a href="#/learning?tab=smart" style="margin-left:auto">Smart rules →</a></div></div>`; })()}
      ${(() => { const au = S.settings.autonomy; const el2 = primary.filter(autoEligible); return au.level === 'advisory' ? '' : `<div class="banner auto">${BT.icon('bolt')}<div><b>Autonomy: ${au.level === 'autonomous' ? 'autonomous within limits' : 'supervised'}.</b> Tickets with P(win) ≥ ${Math.round(au.autoApproveMinP * 100)}% and every check passing are approved by the service at the cutoff — <b>${el2.length}</b> ticket(s) qualify now. Review only what you want to change. <a href="#/learning?tab=autonomy" style="margin-left:auto">Autonomy settings →</a></div></div>`; })()}
      <div class="row" style="margin-bottom:12px"><div class="seg" id="rv-filter">${['all', 'pending', 'decided'].map((f) => `<button data-f="${f}" class="${reviewFilter === f ? 'on' : ''}">${f[0].toUpperCase() + f.slice(1)}</button>`).join('')}</div>
        <span class="small muted">${Object.entries(counts).filter(([, n]) => n).map(([k, n]) => `${STATUS[k][1]}: ${n}`).join(' · ')}</span></div>
      <div class="tickets">${shown.map(ticketCard).join('') || (plan.gate && plan.gate.status === 'no-trade' ? `<div class="card"><div class="empty">${BT.icon('shield')}<br><b>No-trade day.</b> The market gate learned that days like tomorrow lose money on average, so no tickets were proposed.<br>The best candidates are kept on the reserve list for reference — promote one only if you have a strong reason.</div></div>` : '<div class="card"><div class="empty">Nothing in this view.</div></div>')}</div>
      <h3 style="margin:22px 0 10px">Reserve list</h3>
      <div class="card"><div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>#</th><th>Instrument</th><th>Setup</th><th class="r">P(win)</th><th class="r">E[R]</th><th>Why on reserve</th><th>Status</th><th></th></tr></thead><tbody>
        ${reserve.map((r) => `<tr><td>${r.rank}</td><td><b>${esc(r.symbol)}</b> <span class="chip ${r.side === 'BUY' ? 'green' : 'red'}">${r.side}</span></td><td>${r.strategy} · ${r.tf} · ${r.style.toLowerCase()}</td><td class="r num">${Math.round(r.pWin * 100)}%</td><td class="r num">+${r.expR.toFixed(2)}R</td><td class="small">${r.filtered ? esc(r.filtered) : plan.gate && plan.gate.status === 'no-trade' ? 'No-trade day' : 'Below the top ' + (plan.maxItems ?? S.settings.maxPlanItems)}</td><td>${chip(r.status)}</td><td><button class="btn sm" data-promote="${r.id}">Promote to plan</button></td></tr>`).join('') || '<tr><td colspan="8" class="empty">No reserve tickets.</td></tr>'}
      </tbody></table></div></div></div>`;
    $$('#rv-filter button').forEach((b) => (b.onclick = () => { reviewFilter = b.dataset.f; BT.rerender(); }));
    $('#approve-all').onclick = approveAll;
    $('#regen').onclick = async () => {
      if (plan.items.some((i) => i.status !== 'proposed') && !(await BT.confirmDlg('Re-run nightly pipeline', 'This replaces the current proposed plan, including your decisions on it. Continue?', 'Re-run'))) return;
      BT.runService('nightly');
      BT.toast('Nightly pipeline started on the service — the plan refreshes when it finishes');
    };
    $$('[data-act]').forEach((b) => (b.onclick = () => act(b.dataset.act, b.dataset.id)));
    $$('[data-promote]').forEach((b) => (b.onclick = () => promote(b.dataset.promote)));
    // charts for each visible ticket
    for (const it of shown) {
      const box = $(`#ch-${CSS.escape(it.id)}`);
      if (!box) continue;
      const lines = [{ price: it.trigger, color: '#6366f1', title: 'trigger' }, { price: it.stop, color: '#f43f5e', title: 'stop' }, { price: it.target, color: '#10b981', title: 'target' }];
      if (it.tf === '1D' || it.tf === '1h' && it.style === 'SWING') {
        const d = D.daily(it.symbol).slice(-70);
        BT.candles(box, d, { lines, daily: true });
      } else {
        const days = D.DAYS.slice(-2);
        const m1 = days.flatMap((d) => D.intraday1m(it.symbol, d));
        BT.candles(box, D.aggregate(m1, it.tf === '5m' ? '5m' : it.tf === '30m' ? '30m' : '15m'), { lines });
      }
    }
  };
  // Pre-trade checklist: makes conflicts visible before you approve
  function preTradeChecks(it) {
    const S = BT.S;
    const out = [];
    const open = BT.positions().filter((p) => p.status === 'open');
    const held = open.filter((p) => p.symbol === it.symbol);
    const q = BT.qualityOf ? BT.qualityOf(it.symbol) : null;
    out.push(q === 'fail' ? ['fail', 'Data failed validation — repair before trading'] : q === 'warn' ? ['warn', 'Data quality warning'] : ['pass', 'Data quality OK']);
    const longHeld = held.filter((p) => p.dir > 0 && p.bucket !== 'Trading');
    if (it.side === 'SELL' && longHeld.length) out.push(['warn', `You hold ${longHeld.map((p) => p.qty + ' in ' + BT.accName(p.account)).join(', ')} — this short works against your holding`]);
    else if (held.some((p) => p.bucket === 'Trading')) out.push(['warn', 'Already an open trade in this instrument']);
    else out.push(['pass', held.length ? 'Same direction as your holding' : 'No conflict with holdings']);
    const sec = BT.inst(it.symbol).sector;
    const maxSec = Math.max(1, ...BT.accountsWith('trading').map((a) => BT.profileOf(a).maxPerSector || 2));
    const n = open.filter((p) => p.bucket === 'Trading' && BT.inst(p.symbol).sector === sec).length + S.plan.items.filter((x) => x !== it && !x.reserve && ['approved', 'modified', 'armed', 'triggered'].includes(x.status) && BT.inst(x.symbol).sector === sec).length;
    out.push(n >= maxSec ? ['warn', `${n} other ${sec} trade(s) — sector limit ${maxSec}`] : ['pass', `Sector exposure OK (${sec})`]);
    const sm = D.smartCfg(S);
    const peers = [...new Set(open.filter((p) => p.bucket === 'Trading').map((p) => p.symbol).concat(S.plan.items.filter((x) => x !== it && !x.reserve && ['approved', 'modified', 'armed', 'triggered'].includes(x.status)).map((x) => x.symbol)))].filter((x) => x !== it.symbol);
    const cc = peers.map((p) => [p, D.corr(it.symbol, p, sm.corr.lookback)]).sort((a, b) => b[1] - a[1])[0];
    out.push(cc && cc[1] > sm.corr.max ? ['warn', `Same bet as ${cc[0]} (correlation ${cc[1].toFixed(2)})`] : ['pass', cc ? `Diversified (max correlation ${cc[1].toFixed(2)})` : 'No correlated positions']);
    const dd = Math.round((new Date(D.resultsDate(it.symbol)) - Date.now()) / D.DAY);
    out.push(dd >= 0 && dd <= 3 ? ['warn', `Results in ${dd} day(s) — event risk`] : ['pass', 'No results within 3 days']);
    return out;
  }
  BT.preTradeChecks = preTradeChecks;
  // Autonomy: tickets that meet the bar are approved by the service at the cutoff, so you only review the rest
  function autoEligible(it) {
    const au = BT.S.settings.autonomy || {};
    return au.level !== 'advisory' && it.status === 'proposed' && !it.reserve && it.pWin >= au.autoApproveMinP && preTradeChecks(it).every((c) => c[0] === 'pass');
  }
  BT.autoEligible = autoEligible;
  function ticketCard(it) {
    const S = BT.S;
    const risk = Math.abs(it.trigger - it.stop);
    const decided = it.status !== 'proposed';
    const editable = ['proposed', 'approved', 'modified', 'rejected'].includes(it.status);
    return `<div class="ticket ${it.side === 'BUY' ? 'buy' : 'sell'} ${decided ? 'decided' : ''} ${it.status}">
      <div class="t-main">
        <div class="t-head"><span class="chip">#${it.rank}</span><span class="sym">${esc(it.symbol)}</span><span class="chip ${it.side === 'BUY' ? 'green' : 'red'}">${it.side}</span>
          <span class="chip">${it.style.toLowerCase()}</span><span class="chip blue">${it.strategy} · ${it.tf}</span><span class="chip purple">${esc(it.basket)}</span><span class="spacer"></span>${autoEligible(it) ? `<span class="chip pink" title="Meets your autonomy rule — approved automatically at the cutoff unless you decide first">${BT.icon('bolt')} auto at cutoff</span>` : ''}${it.autoApproved ? `<span class="chip pink">${BT.icon('bolt')} auto-approved</span>` : ''}${chip(it.status)}</div>
        <dl>
          <dt>Trigger</dt><dd>${it.style === 'INTRADAY' ? it.tf + ' candle close ' + (it.side === 'BUY' ? 'above' : 'below') : 'enter ' + (it.side === 'BUY' ? 'above' : 'below')} <b>${num(it.trigger)}</b> · ${it.window}</dd>
          <dt>Stop</dt><dd><b>${num(it.stop)}</b> · risk ${num(risk)}/share${it.modStop ? ' <span class="chip purple">tightened</span>' : ''}</dd>
          <dt>Exit</dt><dd>${esc(it.exit)} · target <b>${num(it.target)}</b> · ${it.squareOff}</dd>
          <dt>Probability</dt><dd><div class="row" style="gap:8px"><div class="meter" style="width:120px"><i style="width:${Math.round(it.pWin * 100)}%"></i></div> ${Math.round(it.pWin * 100)}% · E[R] <b class="pos">+${it.expR.toFixed(2)}R</b> · n=${it.samples}</div></dd>
          ${it.meta != null ? `<dt>Smart rules</dt><dd><span class="chip ${it.meta >= 0.6 ? 'green' : it.meta >= 0.5 ? 'blue' : 'red'}" title="Meta-model: probability this signal is worth taking, from your graded trades">meta ${Math.round(it.meta * 100)}% · size ×${it.sizeMult}</span> ${it.windowLearned ? '<span class="chip cyan" title="Best entry window learned for this setup">window learned</span>' : ''} ${it.corrWith ? `<span class="chip ${it.corr > 0.6 ? 'amber' : ''}" title="60-day correlation with the closest other ticket or open position">ρ ${it.corr.toFixed(2)} vs ${esc(it.corrWith)}</span>` : ''} ${S.plan.throttle && S.plan.throttle.riskMult < 1 ? `<span class="chip amber">risk ×${S.plan.throttle.riskMult}</span>` : ''}</dd>` : ''}
          <dt>Allocation</dt><dd>${it.alloc.map((a) => `${esc(BT.accName(a.account))}: <b>${a.qty}</b> (risk ${inr(a.qty * risk)})`).join('<br>') || '<span class="neg">No Trading-role account</span>'}</dd>
          <dt>Cancel if</dt><dd class="small">${esc(it.cancelIf)}</dd>
        </dl>
        <div class="reasons" style="margin-bottom:8px">${it.reasons.map((r) => `<span class="chip">${esc(r)}</span>`).join('')}</div>
        <div class="checks">${preTradeChecks(it).map(([st, txt]) => `<span class="ck ${st}">${st === 'pass' ? '✓' : st === 'warn' ? '⚠' : '✗'} ${esc(txt)}</span>`).join('')}</div>
        ${it.decision && it.decision.note ? `<div class="small muted" style="margin-bottom:8px">Your note: ${esc(it.decision.note)}</div>` : ''}
        ${it.statusNote ? `<div class="small muted" style="margin-bottom:8px">${esc(it.statusNote)}</div>` : ''}
        <div class="t-actions">${editable ? (it.status === 'proposed'
          ? `<button class="btn good" data-act="approve" data-id="${it.id}">Approve</button><button class="btn" data-act="modify" data-id="${it.id}">Modify…</button><button class="btn bad" data-act="reject" data-id="${it.id}">Reject…</button>`
          : `<button class="btn" data-act="undo" data-id="${it.id}">Undo decision</button>${it.status !== 'rejected' ? `<button class="btn" data-act="modify" data-id="${it.id}">Modify…</button>` : ''}<span class="small muted">decided ${it.decision ? ago(it.decision.at) : ''}</span>`)
          : `<span class="small muted">Managed by services since ${it.armedAt ? fmtTime(it.armedAt) : 'pre-open'}</span>`}</div>
      </div>
      <div class="t-chart" id="ch-${esc(it.id)}"></div>
    </div>`;
  }
  function limitOk(extra = 1) { return decidedCount() + extra <= BT.S.settings.maxPlanItems; }
  function decide(it, status, note, mods) {
    it.status = status;
    it.decision = { at: Date.now(), note: note || '', mods: mods || null };
    BT.audit('owner', `Plan ticket ${status}`, `${it.symbol} ${it.side} ${it.strategy} ${it.tf}${note ? ' — ' + note : ''}`);
    BT.save(); BT.rerender();
  }
  function act(action, id) {
    const it = BT.S.plan.items.find((x) => x.id === id);
    if (!it) return;
    if (action === 'approve') {
      if (!limitOk()) return BT.toast(`Limit reached: max ${BT.S.settings.maxPlanItems} approved tickets`, 'bad');
      const bad = preTradeChecks(it).filter((c) => c[0] === 'fail');
      const go = () => { decide(it, 'approved', bad.length ? 'Approved despite: ' + bad.map((c) => c[1]).join('; ') : ''); BT.toast(`${it.symbol} approved — it will be armed at the pre-open check`, 'good'); };
      if (bad.length) BT.confirmDlg('Failed pre-trade check', `${esc(bad.map((c) => c[1]).join('; '))}. Approve anyway?`, 'Approve anyway', true).then((ok) => ok && go()); else go();
    }
    if (action === 'undo') { it.status = 'proposed'; it.decision = null; BT.audit('owner', 'Decision undone', it.symbol); BT.save(); BT.rerender(); }
    if (action === 'reject') rejectDlg(it);
    if (action === 'modify') modifyDlg(it);
  }
  function rejectDlg(it) {
    BT.modal({
      title: `Reject ${it.symbol} ${it.side}`,
      body: `<label class="field">Reason<select class="input" id="rj-r"><option>Not convinced by setup</option><option>News / event risk</option><option>Too much exposure to this sector</option><option>Already holding this stock</option><option>Other</option></select></label>
        <label class="field">Note (optional)<input class="input" id="rj-n" placeholder="e.g. results next week"></label>
        <div class="small muted">Rejected tickets are still graded, so the Decisions report can show whether your overrides add value.</div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn bad solid" id="rj-ok">Reject</button>',
      onMount: (ov, close) => { $('#rj-ok', ov).onclick = () => { close(); decide(it, 'rejected', $('#rj-r', ov).value + ($('#rj-n', ov).value ? ': ' + $('#rj-n', ov).value : '')); }; },
    });
  }
  function modifyDlg(it) {
    const S = BT.S;
    const reserve = S.plan.items.filter((x) => x.reserve && x.status === 'proposed');
    const long = it.side === 'BUY';
    BT.modal({
      title: `Modify ${it.symbol} ${it.side}`,
      body: `<div class="small muted">Allowed: reduce size, tighten stop, change accounts, or swap with a reserve ticket. Changes are re-validated against risk limits.</div>
        <div class="grid g2">${it.alloc.map((a, i) => `<label class="field">${esc(BT.accName(a.account))} — quantity (max ${a.origQty || a.qty})<input class="input num" type="number" min="0" max="${a.origQty || a.qty}" value="${a.qty}" data-q="${i}"></label>`).join('')}</div>
        <label class="field">Stop (tighten only: ${long ? 'between ' + num(it.origStop || it.stop) + ' and ' + num(it.trigger) : 'between ' + num(it.trigger) + ' and ' + num(it.origStop || it.stop)})<input class="input num" type="number" step="0.05" id="md-stop" value="${it.stop}"></label>
        ${reserve.length ? `<label class="field">…or use a reserve ticket instead<select class="input" id="md-swap"><option value="">— keep this ticket —</option>${reserve.map((r) => `<option value="${r.id}">#${r.rank} ${r.symbol} ${r.side} · ${r.strategy} ${r.tf} · +${r.expR.toFixed(2)}R</option>`).join('')}</select></label>` : ''}
        <label class="field">Note<input class="input" id="md-note" placeholder="why you changed it"></label><div class="err" id="md-err"></div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="md-ok">Save & approve</button>',
      onMount: (ov, close) => {
        $('#md-ok', ov).onclick = () => {
          const err = $('#md-err', ov);
          const swap = $('#md-swap', ov) && $('#md-swap', ov).value;
          if (swap) {
            const r = S.plan.items.find((x) => x.id === swap);
            r.reserve = false; it.reserve = true;
            it.status = 'rejected'; it.decision = { at: Date.now(), note: 'Swapped for reserve ' + r.symbol };
            if (!limitOk()) { BT.toast('Limit reached', 'bad'); return; }
            close(); decide(r, 'approved', 'Promoted from reserve (swap)'); return;
          }
          const stop = parseFloat($('#md-stop', ov).value);
          const orig = it.origStop || it.stop;
          const okStop = long ? stop >= orig && stop < it.trigger : stop <= orig && stop > it.trigger;
          if (!okStop) { err.textContent = 'Stop can only be tightened (moved toward the trigger).'; return; }
          const qs = $$('[data-q]', ov).map((x) => parseInt(x.value, 10) || 0);
          if (qs.some((q, i) => q > (it.alloc[i].origQty || it.alloc[i].qty) || q < 0)) { err.textContent = 'Quantity can only be reduced.'; return; }
          if (qs.every((q) => q === 0)) { err.textContent = 'All quantities are zero — reject the ticket instead.'; return; }
          if (it.status === 'proposed' && !limitOk()) { err.textContent = `Limit reached: max ${S.settings.maxPlanItems} approved tickets.`; return; }
          it.origStop = orig; if (stop !== orig) it.modStop = true;
          it.stop = stop;
          it.alloc.forEach((a, i) => { a.origQty = a.origQty || a.qty; a.qty = qs[i]; });
          it.alloc = it.alloc.filter((a) => a.qty > 0);
          close(); decide(it, 'modified', $('#md-note', ov).value, { stop, qty: qs });
          BT.toast(`${it.symbol} modified and approved`, 'good');
        };
      },
    });
  }
  function approveAll() {
    const S = BT.S;
    let n = 0;
    for (const it of S.plan.items.filter((i) => !i.reserve && i.status === 'proposed').sort((a, b) => a.rank - b.rank)) {
      if (!limitOk()) break;
      it.status = 'approved'; it.decision = { at: Date.now(), note: 'Bulk approve within limits' }; n++;
      BT.audit('owner', 'Plan ticket approved', `${it.symbol} (bulk)`);
    }
    BT.save(); BT.rerender();
    BT.toast(n ? `${n} ticket(s) approved` : 'Nothing to approve within limits', n ? 'good' : '');
  }
  function promote(id) {
    const r = BT.S.plan.items.find((x) => x.id === id);
    if (!limitOk()) return BT.toast('Limit reached — reject or undo another ticket first', 'bad');
    r.reserve = false; decide(r, 'approved', 'Promoted from reserve');
  }

  /* ================= SUGGESTIONS ================= */
  let suggTab = 'pending';
  const SUGG_META = {
    track: ['＋', 'Track instrument'], add_to_basket: ['▦', 'Add to basket'], remove_from_basket: ['−', 'Remove from basket'],
    focus: ['☀', 'Next-day focus'], promotion: ['⚗', 'Promote hypothesis'],
  };
  BT.pages.suggestions = function (el) {
    const S = BT.S;
    const list = S.suggestions.filter((s) => s.status === suggTab);
    el.innerHTML = `<div class="page-head"><div><h1>Suggestions inbox</h1><p>AI and learning-engine proposals. Nothing changes until you accept.</p></div>
      <div class="row"><button class="btn" id="ask-ai">Ask AI for new suggestions</button></div></div>
      <div class="row" style="margin-bottom:12px"><div class="seg" id="sg-tabs">${['pending', 'accepted', 'rejected'].map((t) => `<button data-t="${t}" class="${suggTab === t ? 'on' : ''}">${t[0].toUpperCase() + t.slice(1)} (${S.suggestions.filter((s) => s.status === t).length})</button>`).join('')}</div></div>
      <div class="card"><div class="card-b flush">${list.map((s) => {
        const [ico, label] = SUGG_META[s.type] || ['•', s.type];
        const title = s.type === 'track' ? `Track ${s.symbol}` : s.type === 'add_to_basket' ? `Add ${s.symbol} to ${s.basket}` : s.type === 'remove_from_basket' ? `Remove ${s.symbol} from ${s.basket}` : label;
        return `<div class="sugg"><div class="s-ico">${ico}</div><div><div><b>${esc(title)}</b> <span class="chip">${label}</span></div><div style="margin-top:4px">${esc(s.reason)}</div>
          <div class="small muted" style="margin-top:4px">Evidence: ${esc(s.evidence)} · ${s.status === 'pending' ? 'expires ' + fmtDate(s.expiresAt) : s.status + ' ' + ago(s.decidedAt)}</div></div>
          <div class="row">${s.status === 'pending' ? `<button class="btn good sm" data-acc="${s.id}">Accept</button><button class="btn sm bad" data-rej="${s.id}">Reject</button>` : ''}</div></div>`;
      }).join('') || '<div class="empty">No suggestions here.</div>'}</div></div>`;
    $$('#sg-tabs button').forEach((b) => (b.onclick = () => { suggTab = b.dataset.t; BT.rerender(); }));
    $('#ask-ai').onclick = () => { BT.runService('suggestions'); BT.toast('Suggestions service started'); };
    $$('[data-acc]').forEach((b) => (b.onclick = () => accept(b.dataset.acc)));
    $$('[data-rej]').forEach((b) => (b.onclick = () => { const s = S.suggestions.find((x) => x.id === b.dataset.rej); s.status = 'rejected'; s.decidedAt = Date.now(); BT.audit('owner', 'Suggestion rejected', s.reason); BT.save(); BT.rerender(); BT.toast('Rejected — won\'t be suggested again for 14 days'); }));
  };
  function accept(id) {
    const S = BT.S;
    const s = S.suggestions.find((x) => x.id === id);
    let msg = 'Accepted';
    if (s.type === 'track') { BT.track(s.symbol, 'owner (via suggestion)'); msg = `${s.symbol} tracked — backfill running on the service`; }
    if (s.type === 'add_to_basket') { const b = S.baskets.find((x) => x.name === s.basket); if (b && !b.members.includes(s.symbol)) b.members.push(s.symbol); BT.track(s.symbol, 'owner (via suggestion)'); msg = `${s.symbol} added to ${s.basket}`; }
    if (s.type === 'remove_from_basket') { const b = S.baskets.find((x) => x.name === s.basket); if (b) b.members = b.members.filter((m) => m !== s.symbol); msg = `${s.symbol} removed from ${s.basket}`; }
    if (s.type === 'promotion') msg = 'Config version created — activate it in Settings when ready';
    s.status = 'accepted'; s.decidedAt = Date.now();
    BT.audit('owner', 'Suggestion accepted', msg);
    BT.save(); BT.rerender(); BT.toast(msg, 'good');
  }

  /* ================= PORTFOLIO ================= */
  const pf = { groupBy: 'bucket', status: 'open', account: 'all', open: {}, q: '', sev: 'all', acOpen: true };
  function niftyAt(t) { return D.closeOn('NIFTY 50', D.iso(new Date(t))); }
  function benchmarkXirr(flows) {
    const sorted = flows.slice().sort((a, b) => a.t - b.t);
    const terminal = sorted.length && sorted[sorted.length - 1].t >= Date.now() - 5000 ? sorted.pop() : null;
    let units = 0;
    const out = [];
    for (const f of sorted) { units += -f.amount / niftyAt(f.t); out.push(f); }
    out.push({ t: Date.now(), amount: units * BT.ltp('NIFTY 50') });
    return terminal || units ? D.xirr(out) : null;
  }
  const MIN_XIRR_DAYS = 30;
  // XIRR is only meaningful when money has been invested long enough; below the threshold show "short period"
  function safeXirr(flows) {
    if (!flows.length) return { x: null, short: false };
    const ts = flows.map((f) => f.t);
    const span = (Math.max(...ts) - Math.min(...ts)) / D.DAY;
    if (span < MIN_XIRR_DAYS) return { x: null, short: true };
    return { x: D.xirr(flows), short: false };
  }
  const fmtX = (r, title) => (r.short ? `<span class="chip" title="${title || 'Invested for less than ' + MIN_XIRR_DAYS + ' days — annualised return would mislead'}">short period</span>` : r.x == null ? '—' : Math.abs(r.x) > 9.99 ? `<span class="${sgn(r.x)}">${r.x > 0 ? '>+999%' : '<−99%'}</span>` : `<span class="${sgn(r.x)}">${pct(r.x)}</span>`);
  function agg(list) {
    const open = list.filter((p) => p.status === 'open');
    const flows = list.flatMap((p) => p.flows);
    const xr = safeXirr(flows), br = xr.short ? { x: null } : { x: benchmarkXirr(flows) };
    return {
      n: list.length, nOpen: open.length,
      cost: sum(open, (p) => p.remainingCost), value: sum(open, (p) => p.value),
      unrealised: sum(open, (p) => p.unrealised), realised: sum(list, (p) => p.realised),
      day: sum(open, (p) => p.dir * p.qty * (p.ltp - D.prevClose(p.symbol))),
      invested: sum(list, (p) => p.invested), xirr: xr.x, xshort: xr.short, bxirr: br.x,
      first: Math.min(...list.map((p) => p.firstAdded)),
    };
  }
  const xirrCell = (x, days, min) => (days < min ? `<span class="chip" title="Held ${days} days — annualised return would mislead; see Return instead">short period</span>` : fmtX({ x, short: false }));
  /* ---------- Action center: rules that turn portfolio data into decisions ---------- */
  const RULES = {
    drawdown: ['high', '⬇', 'Below cost'], profit: ['medium', '⬆', 'Large gain'], concentration: ['high', '◕', 'Overweight'], maxhold: ['high', '⏱', 'Max holding exceeded'],
    ltcg: ['info', '₹', 'Long-term soon'], results: ['medium', '📅', 'Results due'], unassigned: ['low', '▦', 'Not in a basket'], lagging: ['medium', '↘', 'Lagging Nifty'], sector: ['medium', '◔', 'Sector concentration'],
  };
  // colourful icon per action-center rule
  const tint = (a, b) => `color:${b};background:color-mix(in srgb, ${b} 12%, transparent)`;
  const ACT_ICO = { drawdown: ['trendDown', tint('#fb7185', '#e11d48')], profit: ['trendUp', tint('#34d399', '#059669')], concentration: ['pie', tint('#fbbf24', '#d97706')], maxhold: ['clock', tint('#fb7185', '#e11d48')],
    ltcg: ['coins', tint('#38bdf8', '#0284c7')], results: ['calendar', tint('#c084fc', '#7c3aed')], unassigned: ['basket', tint('#94a3b8', '#475569')], lagging: ['activity', tint('#fbbf24', '#d97706')], sector: ['grid', tint('#f472b6', '#db2777')] };
  const SEV = { high: 0, medium: 1, info: 2, low: 3 };
  BT.portfolioInsights = function (positions) {
    const S = BT.S;
    const th = S.settings.actions || (S.settings.actions = { lossPct: 10, gainPct: 25, weightPct: 15, sectorPct: 35, resultsDays: 7, lagPp: 10 });
    const out = [];
    const open = positions.filter((p) => p.status === 'open');
    const byAcc = {};
    open.forEach((p) => (byAcc[p.account] = byAcc[p.account] || []).push(p));
    const now = Date.now();
    for (const [acc, list] of Object.entries(byAcc)) {
      const accVal = sum(list, (p) => p.value) || 1;
      for (const p of list) {
        const ret = p.unrealised / (p.remainingCost || 1);
        const w = p.value / accVal;
        const add = (type, msg, extra = {}) => out.push({ key: `${type}:${acc}:${p.symbol}:${p.bucket}`, type, account: acc, symbol: p.symbol, pos: p, msg, sev: RULES[type][0], ...extra });
        if (ret <= -th.lossPct / 100) add('drawdown', `Down ${pct(ret)} from cost (${inr(p.unrealised)}). Decide: hold with a stop alert, average down only if the thesis holds, or exit.`, { sev: ret <= -0.2 ? 'high' : 'medium' });
        if (ret >= th.gainPct / 100) add('profit', `Up ${pct(ret)} (${inr(p.unrealised)}). Consider booking part of the gain or setting a trailing-stop alert.`);
        // smarter than a flat limit: small accounts are naturally concentrated, so compare with 1.5× an equal-weight share
        const limit = Math.max(th.weightPct / 100, 1.5 / list.length);
        if (list.length >= 3 && w > limit) add('concentration', `${pct(w).replace('+', '')} of ${BT.accName(acc)} — above the ${Math.round(limit * 100)}% limit for a ${list.length}-position account. Trim or rebalance.`);
        if (p.bucket === 'Trading' && p.daysHeld >= 7) add('maxhold', `Swing position held ${p.daysHeld} days — beyond the 5-session maximum. Exit is due per plan rules.`);
        const toLt = 365 - p.daysHeld;
        if (p.bucket !== 'Trading' && toLt > 0 && toLt <= 30 && ret > 0) add('ltcg', `Becomes long-term (12 months) in ${toLt} day(s). Selling before then may be taxed as short-term — check before selling.`);
        const rd = D.resultsDate(p.symbol); const dd = Math.round((new Date(rd) - now) / D.DAY);
        if (dd >= 0 && dd <= th.resultsDays) add('results', `Quarterly results on ${fmtDate(new Date(rd))} (in ${dd} day(s)) — event risk for this position.`);
        if (!BT.basketsOf(p.symbol).length) add('unassigned', 'Not in any basket — assign it so it shows up in basket views and reports.');
        if (p.daysHeld > 180) { const x = D.xirr(p.flows); const flowsB = benchFlows(p.flows); if (x != null && flowsB != null && x < flowsB - th.lagPp / 100) add('lagging', `XIRR ${pct(x)} vs ${pct(flowsB)} for the same money in Nifty 50 — lagging by ${((flowsB - x) * 100).toFixed(1)} pp.`); }
      }
      const bySec = {}; list.forEach((p) => (bySec[BT.inst(p.symbol).sector] = (bySec[BT.inst(p.symbol).sector] || 0) + p.value));
      for (const [sec, v] of Object.entries(bySec)) if (v / accVal > th.sectorPct / 100 && Object.keys(bySec).length > 1) out.push({ key: `sector:${acc}:${sec}`, type: 'sector', account: acc, symbol: sec, msg: `${sec} is ${pct(v / accVal).replace('+', '')} of ${BT.accName(acc)} — above ${th.sectorPct}%.`, sev: 'medium' });
    }
    const st = S.portfolioActions;
    return out.filter((i) => { const a = st[i.key]; return !a || (a.status === 'snoozed' && a.until < now); }).sort((a, b) => SEV[a.sev] - SEV[b.sev]);
  };
  function benchFlows(flows) { try { return benchmarkXirr(flows); } catch (e) { return null; } }
  function alertDialog(sym, account) {
    const S = BT.S;
    const p = BT.ltp(sym);
    BT.modal({
      title: `Price alert — ${sym}`,
      body: `<div class="small muted">Evaluated by the “Price alerts & action center” service during market hours (and on holdings refresh). You are notified when it triggers.</div>
        <div class="grid g3"><label class="field">When price is<select class="input" id="al-op"><option value="below">at or below</option><option value="above">at or above</option></select></label>
        <label class="field">Price (LTP ${num(p)})<input class="input" type="number" step="0.05" id="al-p" value="${D.tick(p * 0.95)}"></label><label class="field">Note<input class="input" id="al-n" placeholder="e.g. stop for long-term holding"></label></div>
        <div class="row"><button class="btn sm" data-pp="-0.05">−5%</button><button class="btn sm" data-pp="-0.1">−10%</button><button class="btn sm" data-pp="0.1">+10%</button><button class="btn sm" data-pp="0.2">+20%</button></div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="al-ok">Create alert</button>',
      onMount: (ov, close) => {
        $$('[data-pp]', ov).forEach((b) => (b.onclick = () => { const k = +b.dataset.pp; $('#al-p', ov).value = D.tick(p * (1 + k)); $('#al-op', ov).value = k < 0 ? 'below' : 'above'; }));
        $('#al-ok', ov).onclick = () => {
          const price = +$('#al-p', ov).value; if (!(price > 0)) return;
          S.alerts.unshift({ id: BT.uid('AL'), symbol: sym, account, op: $('#al-op', ov).value, price, note: $('#al-n', ov).value, active: true, createdAt: Date.now() });
          BT.audit('owner', 'Price alert created', `${sym} ${$('#al-op', ov).value} ${price}`); BT.save(); close(); BT.rerender(); BT.toast('Alert created — watched by the services', 'good');
        };
      },
    });
  }
  BT.alertDialog = alertDialog;
  BT.pfFilterAccount = (id) => { pf.account = id; };
  BT.pages.portfolio = function (el) {
    const S = BT.S;
    const accs = BT.accountsWith('portfolio');
    const all = BT.positions().filter((p) => accs.some((a) => a.id === p.account));
    let list = all.filter((p) => (pf.account === 'all' || p.account === pf.account) && (pf.status === 'all' || p.status === pf.status) && (!pf.q || p.symbol.includes(pf.q.toUpperCase())));
    const tot = agg(list);
    const openList = list.filter((p) => p.status === 'open');
    const totalValue = tot.value || 1;
    const minDays = 30;
    const keyFns = {
      bucket: (p) => [p.bucket], account: (p) => [BT.accName(p.account)], sector: (p) => [BT.inst(p.symbol).sector],
      basket: (p) => { const b = BT.basketsOf(p.symbol); return b.length ? b : ['(not in a basket)']; }, none: () => ['All positions'],
    };
    const groups = {};
    for (const p of list) for (const k of keyFns[pf.groupBy](p)) (groups[k] = groups[k] || []).push(p);
    const gkeys = Object.keys(groups).sort((a, b) => sum(groups[b], (p) => p.value) - sum(groups[a], (p) => p.value));
    const row = (p) => {
      const ret = p.status === 'open' ? p.unrealised / p.remainingCost : p.realised / p.invested;
      return `<tr class="clickable" data-pos="${esc(p.key)}"><td style="padding-left:30px"><b>${esc(p.symbol)}</b>${p.dir < 0 ? ' <span class="chip red">short</span>' : ''}${p.planItem ? ' <span class="chip blue" title="From a plan ticket">plan</span>' : ''}</td>
        <td>${esc(BT.accName(p.account))}</td><td>${esc(p.bucket)}</td><td>${fmtDate(p.firstAdded)}</td><td class="r num">${p.daysHeld}</td><td class="r num">${p.qty || '—'}</td>
        <td class="r num">${p.qty ? num(p.avgCost) : '—'}</td><td class="r num">${num(p.ltp)}</td><td class="r num">${p.qty ? inr(p.value) : '—'}</td>
        <td class="r num ${sgn(p.status === 'open' ? p.unrealised : p.realised)}">${inr(p.status === 'open' ? p.unrealised : p.realised)}</td><td class="r num ${sgn(ret)}">${pct(ret)}</td>
        <td class="r num">${xirrCell(D.xirr(p.flows), p.daysHeld, minDays)}</td><td class="r num">${p.qty ? pct(p.value / totalValue).replace('+', '') : '—'}</td></tr>`;
    };
    const grpRow = (k) => {
      const a = agg(groups[k]);
      const ret = a.cost ? a.unrealised / a.cost : a.realised / (a.invested || 1);
      const opened = pf.open[pf.groupBy + ':' + k] ?? true;
      return `<tr class="group ${opened ? 'open' : ''}" data-grp="${esc(k)}"><td><span class="caret">▸</span> ${esc(k)} <span class="small muted">(${a.n})</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td>
        <td class="r num">${inr(a.value)}</td><td class="r num ${sgn(a.unrealised + a.realised)}">${inr(pf.status === 'closed' ? a.realised : a.unrealised)}</td><td class="r num ${sgn(ret)}">${pct(ret)}</td>
        <td class="r num">${fmtX({ x: a.xirr, short: a.xshort })}</td><td class="r num">${pct(a.value / totalValue).replace('+', '')}</td></tr>${groups[k].sort((x, y) => y.value - x.value).map((p) => row(p).replace('<tr class="clickable"', `<tr class="clickable child${opened ? '' : ' hidden'}" data-of="${esc(k)}"`)).join('')}`;
    };
    const ret = tot.cost ? tot.unrealised / tot.cost : 0;
    const insightsAll = BT.portfolioInsights(all.filter((p) => accs.find((a) => a.id === p.account).roles.portfolio.includeInTotals !== false));
    const accTile = (a) => {
      const l = all.filter((p) => p.account === a.id), g = agg(l), n = insightsAll.filter((i) => i.account === a.id);
      const top = l.filter((p) => p.status === 'open').sort((x, y) => y.value - x.value)[0];
      return `<div class="acc-tile ${pf.account === a.id ? 'sel' : ''}" data-atile="${a.id}" style="border-top-color:${a.color}"><div class="row between"><b>${esc(a.name)}</b>${n.length ? `<span class="badge" title="items need attention">${n.length}</span>` : ''}</div>
        <div class="v num">${inr(g.value)}</div><div class="small"><span class="${sgn(g.unrealised)}">${inr(g.unrealised)} (${pct(g.cost ? g.unrealised / g.cost : 0)})</span> · day <span class="${sgn(g.day)}">${inr(g.day)}</span></div>
        <div class="small muted">XIRR ${g.xshort ? 'short period' : pct(g.xirr)} · ${g.nOpen} open · realised ${inr(g.realised)}</div>
        <div class="small muted">${top ? `Top: ${esc(top.symbol)} ${pct(top.value / (g.value || 1)).replace('+', '')}` : 'No open positions'}${a.roles.trading.enabled ? ' · trading' : ''}</div></div>`;
    };
    const shown = insightsAll.filter((i) => (pf.account === 'all' || i.account === pf.account) && (pf.sev === 'all' || i.sev === pf.sev));
    el.innerHTML = `
      <div class="page-head"><div><h1>Portfolio</h1><p>${accs.length} account(s) with the Portfolio role · values update live in market hours · XIRR from dated cash flows</p></div>
        <div class="row"><button class="btn" id="pf-sync">Sync accounts now</button><button class="btn" id="pf-alerts">Alerts (${S.alerts.filter((x) => x.active).length})</button><button class="btn primary" id="pf-add">Add transaction</button></div></div>
      <div class="acc-tiles"><div class="acc-tile ${pf.account === 'all' ? 'sel' : ''}" data-atile="all" style="border-top-color:var(--text-2)"><div class="row between"><b>All accounts</b>${insightsAll.length ? `<span class="badge">${insightsAll.length}</span>` : ''}</div>
        <div class="v num">${inr(agg(all).value)}</div><div class="small">${accs.length} accounts · ${all.filter((p) => p.status === 'open').length} open positions</div><div class="small muted">click a tile to focus one account</div></div>${accs.map(accTile).join('')}</div>
      <div class="card" style="margin-bottom:14px"><div class="card-h"><h3>Action center <span class="small muted">— ${shown.length} item(s) need a decision${pf.account !== 'all' ? ' in ' + esc(BT.accName(pf.account)) : ''}</span></h3>
        <div class="row"><div class="seg" id="pf-sev">${[['all', 'All'], ['high', 'High'], ['medium', 'Medium'], ['info', 'Info'], ['low', 'Low']].map(([k, l]) => `<button data-sev="${k}" class="${pf.sev === k ? 'on' : ''}">${l}</button>`).join('')}</div><button class="btn sm" id="pf-ac-toggle">${pf.acOpen ? 'Hide' : 'Show'}</button></div></div>
        ${pf.acOpen ? `<div class="card-b flush" style="max-height:380px;overflow:auto">${shown.map((i) => `<div class="act-item sev-${i.sev}"><div class="act-ico" style="${ACT_ICO[i.type] ? ACT_ICO[i.type][1] : ''}">${ACT_ICO[i.type] ? BT.icon(ACT_ICO[i.type][0]) : RULES[i.type][1]}</div><div><div><b>${esc(i.symbol)}</b> <span class="chip">${RULES[i.type][2]}</span> <span class="small muted">${esc(BT.accName(i.account))}</span></div><div class="small">${esc(i.msg)}</div></div>
          <div class="row" style="flex-wrap:nowrap">${i.pos ? `<button class="btn sm" data-aopen="${esc(i.pos.key)}">Open</button><button class="btn sm" data-aalert="${esc(i.symbol)}" data-aacc="${i.account}">Set alert</button>` : ''}${i.type === 'unassigned' ? `<button class="btn sm" data-abasket="${esc(i.symbol)}">Add to basket</button>` : ''}<button class="btn sm" data-asnooze="${esc(i.key)}">Snooze 7d</button><button class="btn sm good" data-adone="${esc(i.key)}">Done</button></div></div>`).join('') || '<div class="empty">Nothing needs attention. 🎉</div>'}</div>` : ''}</div>
      <div class="tiles">
        <div class="tile"><div class="l">Current value</div><div class="v num">${inr(tot.value)}</div><div class="s">${tot.nOpen} open positions</div></div>
        <div class="tile"><div class="l">Invested (open)</div><div class="v num">${inr(tot.cost)}</div><div class="s">incl. charges</div></div>
        <div class="tile"><div class="l">Unrealised P&L</div><div class="v num ${sgn(tot.unrealised)}">${inr(tot.unrealised)}</div><div class="s ${sgn(ret)}">${pct(ret)}</div></div>
        <div class="tile"><div class="l">Realised P&L</div><div class="v num ${sgn(tot.realised)}">${inr(tot.realised)}</div><div class="s">FIFO, net of charges</div></div>
        <div class="tile"><div class="l">Day change</div><div class="v num ${sgn(tot.day)}">${inr(tot.day)}</div><div class="s">vs previous close</div></div>
        <div class="tile"><div class="l">XIRR</div><div class="v num">${fmtX({ x: tot.xirr, short: tot.xshort })}</div><div class="s">${tot.xirr == null ? 'needs ≥ ' + MIN_XIRR_DAYS + ' days of history' : `Nifty same flows ${pct(tot.bxirr)} · alpha <b class="${sgn(tot.xirr - (tot.bxirr ?? 0))}">${pct(tot.xirr - (tot.bxirr ?? 0))}</b>`}</div></div>
      </div>
      <div class="card" style="margin-bottom:14px"><div class="card-b row">
        <span class="small muted">Group by</span><div class="seg" id="pf-group">${[['bucket', 'Bucket'], ['basket', 'Basket'], ['account', 'Account'], ['sector', 'Sector'], ['none', 'None']].map(([k, l]) => `<button data-g="${k}" class="${pf.groupBy === k ? 'on' : ''}">${l}</button>`).join('')}</div>
        <select class="input" id="pf-acc" style="width:auto"><option value="all">All portfolio accounts</option>${accs.map((a) => `<option value="${a.id}" ${pf.account === a.id ? 'selected' : ''}>${esc(a.name)}</option>`).join('')}</select>
        <div class="seg" id="pf-status">${['open', 'closed', 'all'].map((s) => `<button data-s="${s}" class="${pf.status === s ? 'on' : ''}">${s[0].toUpperCase() + s.slice(1)}</button>`).join('')}</div>
        <input class="input" id="pf-q" placeholder="Filter symbol" style="width:150px" value="${esc(pf.q)}">
        ${pf.groupBy === 'basket' ? '<span class="chip amber" title="An instrument can be in several baskets">Overlapping baskets — group totals are not additive</span>' : '<span class="chip green">Groups add up to the total</span>'}
      </div></div>
      <div class="card" style="margin-bottom:14px"><div class="card-b flush"><div class="tbl-wrap" style="max-height:560px"><table class="tbl"><thead><tr>
        <th>Instrument</th><th>Account</th><th>Bucket</th><th>Added</th><th class="r">Days</th><th class="r">Qty</th><th class="r">Avg cost</th><th class="r">LTP</th><th class="r">Value</th><th class="r">P&L</th><th class="r">Return</th><th class="r">XIRR</th><th class="r">Weight</th></tr></thead>
        <tbody>${gkeys.map(grpRow).join('') || '<tr><td colspan="13" class="empty">No positions match.</td></tr>'}</tbody></table></div></div></div>
      <div class="grid auto-380">
        <div class="card"><div class="card-h"><h3>Allocation by ${pf.groupBy === 'none' ? 'instrument' : pf.groupBy}</h3></div><div class="card-b"><div class="chart sm" id="pf-alloc"></div></div></div>
        <div class="card"><div class="card-h"><h3>Sector exposure by account</h3></div><div class="card-b"><div class="chart sm" id="pf-sector"></div></div></div>
        <div class="card"><div class="card-h"><h3>Value vs same cash flows in Nifty 50</h3></div><div class="card-b"><div class="chart sm" id="pf-hist"></div></div></div>
        <div class="card"><div class="card-h"><h3>XIRR by group</h3></div><div class="card-b"><div class="chart sm" id="pf-xirr"></div></div></div>
      </div>`;
    $$('#pf-group button').forEach((b) => (b.onclick = () => { pf.groupBy = b.dataset.g; BT.rerender(); }));
    $$('#pf-status button').forEach((b) => (b.onclick = () => { pf.status = b.dataset.s; BT.rerender(); }));
    $('#pf-acc').onchange = (e) => { pf.account = e.target.value; BT.rerender(); };
    $('#pf-q').onchange = (e) => { pf.q = e.target.value.trim(); BT.rerender(); };
    // expand / collapse a group in place (animated, no re-render)
    $$('[data-grp]').forEach((r) => (r.onclick = () => {
      const k = pf.groupBy + ':' + r.dataset.grp; const open = !(pf.open[k] ?? true); pf.open[k] = open;
      r.classList.toggle('open', open);
      $$('tr[data-of]').filter((x) => x.dataset.of === r.dataset.grp).forEach((x, i) => {
        if (open) { x.classList.remove('hidden'); x.style.animationDelay = Math.min(i * 18, 300) + 'ms'; x.classList.remove('row-in'); void x.offsetWidth; x.classList.add('row-in'); }
        else x.classList.add('hidden');
      });
    }));
    $$('[data-pos]').forEach((r) => (r.onclick = () => positionDrawer(all.find((p) => p.key === r.dataset.pos))));
    $('#pf-sync').onclick = () => { BT.runService('portfolio-sync'); BT.toast('Portfolio sync started on the service'); };
    $('#pf-add').onclick = addTxDialog;
    $$('[data-atile]').forEach((t) => (t.onclick = () => { pf.account = t.dataset.atile; BT.rerender(); }));
    $$('#pf-sev button').forEach((b) => (b.onclick = () => { pf.sev = b.dataset.sev; BT.rerender(); }));
    $('#pf-ac-toggle').onclick = () => { pf.acOpen = !pf.acOpen; BT.rerender(); };
    $$('[data-aopen]').forEach((b) => (b.onclick = () => positionDrawer(all.find((p) => p.key === b.dataset.aopen))));
    $$('[data-aalert]').forEach((b) => (b.onclick = () => alertDialog(b.dataset.aalert, b.dataset.aacc)));
    $$('[data-abasket]').forEach((b) => (b.onclick = () => BT.basketPicker([b.dataset.abasket])));
    const mark = (key, status) => { S.portfolioActions[key] = { status, until: Date.now() + 7 * D.DAY, at: Date.now() }; BT.audit('owner', status === 'done' ? 'Action marked done' : 'Action snoozed', key); BT.save(); BT.rerender(); };
    $$('[data-asnooze]').forEach((b) => (b.onclick = () => mark(b.dataset.asnooze, 'snoozed')));
    $$('[data-adone]').forEach((b) => (b.onclick = () => mark(b.dataset.adone, 'done')));
    $('#pf-alerts').onclick = alertsDrawer;
    // charts
    const allocData = (pf.groupBy === 'none' ? openList.map((p) => ({ name: p.symbol, value: Math.round(p.value) })) : gkeys.map((k) => ({ name: k, value: Math.round(sum(groups[k].filter((p) => p.status === 'open'), (p) => p.value)) }))).filter((x) => x.value > 0);
    BT.echart($('#pf-alloc'), { tooltip: { trigger: 'item', valueFormatter: (v) => inr(v) }, series: [{ type: 'pie', radius: ['45%', '72%'], itemStyle: { borderRadius: 4 }, label: { show: allocData.length < 9 }, data: allocData }] });
    const hist = valueHistory(list);
    BT.echart($('#pf-hist'), { tooltip: { trigger: 'axis', valueFormatter: (v) => inr(v) }, legend: { top: 0 }, grid: { left: 60, right: 10, top: 30, bottom: 24 },
      xAxis: { type: 'category', data: hist.dates, axisLabel: { hideOverlap: true } }, yAxis: { type: 'value', axisLabel: { formatter: (v) => (v / 100000).toFixed(1) + 'L' } },
      series: [{ name: 'Portfolio', type: 'line', showSymbol: false, data: hist.value }, { name: 'Nifty 50 (same flows)', type: 'line', showSymbol: false, data: hist.bench, lineStyle: { type: 'dashed' } }] });
    const secs = [...new Set(all.filter((p) => p.status === 'open').map((p) => BT.inst(p.symbol).sector))];
    BT.echart($('#pf-sector'), { tooltip: { trigger: 'axis', valueFormatter: (v) => inr(v) }, legend: { top: 0, type: 'scroll' }, grid: { left: 90, right: 10, top: 30, bottom: 24 }, xAxis: { type: 'value', axisLabel: { formatter: (v) => (v / 100000).toFixed(0) + 'L' } }, yAxis: { type: 'category', data: secs },
      series: accs.map((a) => ({ name: a.name, type: 'bar', stack: 'x', color: a.color, data: secs.map((sc) => Math.round(sum(all.filter((p) => p.status === 'open' && p.account === a.id && BT.inst(p.symbol).sector === sc), (p) => p.value))) })) });
    const xg = gkeys.map((k) => ({ k, x: agg(groups[k]).xirr })).filter((g) => g.x != null && Math.abs(g.x) <= 9.99);
    BT.echart($('#pf-xirr'), { tooltip: { trigger: 'axis', valueFormatter: (v) => v + '%' }, grid: { left: 110, right: 16, top: 10, bottom: 24 },
      xAxis: { type: 'value', axisLabel: { formatter: '{value}%' } }, yAxis: { type: 'category', data: xg.map((g) => g.k), inverse: true },
      series: [{ type: 'bar', data: xg.map((g) => ({ value: +(g.x * 100).toFixed(1), itemStyle: { color: g.x >= 0 ? '#10b981' : '#f43f5e' } })) }] });
  };
  function valueHistory(list) {
    const txs = list.flatMap((p) => p.txs).sort((a, b) => a.t - b.t);
    const days = D.DAYS.slice(-250);
    const qty = {};
    let i = 0, units = 0;
    const out = { dates: [], value: [], bench: [] };
    for (const d of days) {
      while (i < txs.length && txs[i].date <= d) {
        const t = txs[i++];
        qty[t.symbol] = (qty[t.symbol] || 0) + (t.side === 'BUY' ? t.qty : -t.qty);
        units += (t.side === 'BUY' ? 1 : -1) * (t.qty * t.price) / D.closeOn('NIFTY 50', t.date);
      }
      out.dates.push(d.slice(5));
      out.value.push(Math.round(Object.entries(qty).reduce((s, [sym, q]) => s + Math.max(0, q) * D.closeOn(sym, d), 0)));
      out.bench.push(Math.round(units * D.closeOn('NIFTY 50', d)));
    }
    return out;
  }
  function positionDrawer(p) {
    if (!p) return;
    const inst = BT.inst(p.symbol);
    const x = D.xirr(p.flows);
    BT.drawer({
      title: `${esc(p.symbol)} · ${esc(BT.accName(p.account))} · ${esc(p.bucket)}`,
      body: `<div class="tiles" style="margin:0"><div class="tile"><div class="l">${p.status === 'open' ? 'Value' : 'Realised'}</div><div class="v num">${inr(p.status === 'open' ? p.value : p.realised)}</div></div>
        <div class="tile"><div class="l">P&L</div><div class="v num ${sgn(p.unrealised + p.realised)}">${inr(p.unrealised + p.realised)}</div></div>
        <div class="tile"><div class="l">XIRR</div><div class="v num">${p.daysHeld < MIN_XIRR_DAYS ? 'short period' : fmtX({ x, short: false })}</div><div class="s">${p.daysHeld} days held</div></div></div>
        <dl class="kv"><dt>Instrument</dt><dd>${esc(inst.name)} · ${esc(inst.sector)} · baskets: ${BT.basketsOf(p.symbol).map((b) => `<span class="chip">${esc(b)}</span>`).join(' ') || '—'}</dd>
        <dt>First added</dt><dd>${fmtDT(p.firstAdded)}</dd><dt>Quantity / avg cost</dt><dd>${p.qty} @ ${num(p.avgCost)} (FIFO, incl. charges)</dd><dt>LTP</dt><dd>${num(p.ltp)} (${pct(BT.dayChange(p.symbol), 2)} today)</dd>
        <dt>Source</dt><dd>${p.planItem ? 'Plan ticket ' + esc(p.planItem) : 'Discretionary / long-term'}</dd><dt>Next results</dt><dd>${fmtDate(new Date(D.resultsDate(p.symbol)))}</dd></dl>
        <div class="row"><button class="btn sm" id="pd-alert">Set price alert</button><button class="btn sm" id="pd-basket">Baskets…</button></div>
        <div class="card"><div class="card-h"><h3>Price with your buys & sells</h3></div><div class="card-b"><div class="chart" id="pd-chart"></div></div></div>
        <div class="card"><div class="card-h"><h3>Transactions</h3></div><div class="card-b flush"><table class="tbl"><thead><tr><th>Date</th><th>Side</th><th class="r">Qty</th><th class="r">Price</th><th class="r">Charges</th><th>Source</th></tr></thead><tbody>
        ${p.txs.map((t) => `<tr><td>${fmtDT(t.t)}</td><td><span class="chip ${t.side === 'BUY' ? 'green' : 'red'}">${t.side}</span></td><td class="r num">${t.qty}</td><td class="r num">${num(t.price)}</td><td class="r num">${inr(t.charges, 2)}</td><td>${esc(t.source)}${t.note ? ' · ' + esc(t.note) : ''}</td></tr>`).join('')}</tbody></table></div></div>
        ${p.lots.length ? `<div class="card"><div class="card-h"><h3>Open lots (FIFO)</h3></div><div class="card-b flush"><table class="tbl"><thead><tr><th>Opened</th><th class="r">Qty</th><th class="r">Cost / unit</th></tr></thead><tbody>${p.lots.map((l) => `<tr><td>${fmtDate(l.t)}</td><td class="r num">${l.qty}</td><td class="r num">${num(l.cost)}</td></tr>`).join('')}</tbody></table></div></div>` : ''}`,
      onMount: (ov, close, charts) => {
        $('#pd-alert', ov).onclick = () => { close(); alertDialog(p.symbol, p.account); };
        $('#pd-basket', ov).onclick = () => { close(); BT.basketPicker([p.symbol]); };
        const firstIdx = Math.max(0, (D.DAY_INDEX[p.txs[0].date] ?? D.DAYS.length - 60) - 20);
        const bars = D.daily(p.symbol).slice(Math.min(firstIdx, D.DAYS.length - 60));
        const markers = p.txs.filter((t) => D.DAY_INDEX[t.date] != null).map((t) => ({ time: t.date, position: t.side === 'BUY' ? 'belowBar' : 'aboveBar', color: t.side === 'BUY' ? '#10b981' : '#f43f5e', shape: t.side === 'BUY' ? 'arrowUp' : 'arrowDown', text: t.side + ' ' + t.qty }));
        BT.candles($('#pd-chart', ov), bars, { daily: true, markers, lines: p.qty ? [{ price: +p.avgCost.toFixed(2), color: '#6366f1', title: 'avg cost' }] : [] }, charts);
      },
    });
  }
  function alertsDrawer() {
    const S = BT.S;
    BT.drawer({ title: 'Price alerts', body: `<div class="small muted">Alerts are evaluated by the services (live feed for the active set, quote polling for others) — not by your browser session.</div>
      <div class="card"><div class="card-b flush"><table class="tbl"><thead><tr><th>Instrument</th><th>Condition</th><th>Status</th><th>Note</th><th></th></tr></thead><tbody>
      ${S.alerts.map((a) => `<tr><td><b>${esc(a.symbol)}</b></td><td>${a.op} ${num(a.price)} <span class="small muted">(LTP ${num(BT.ltp(a.symbol))})</span></td><td>${a.active ? '<span class="chip blue">watching</span>' : `<span class="chip green">triggered ${a.triggeredAt ? ago(a.triggeredAt) : ''}</span>`}</td><td>${esc(a.note || '')}</td><td><button class="btn sm ghost" data-aldel="${a.id}">✕</button></td></tr>`).join('') || '<tr><td colspan="5" class="empty">No alerts yet — use “Set alert” in the Action center or a position.</td></tr>'}</tbody></table></div></div>`,
      onMount: (ov, close) => $$('[data-aldel]', ov).forEach((b) => (b.onclick = () => { S.alerts = S.alerts.filter((x) => x.id !== b.dataset.aldel); BT.save(); close(); alertsDrawer(); })) });
  }
  function addTxDialog() {
    const S = BT.S;
    BT.modal({
      title: 'Add transaction (manual)',
      body: `<div class="small muted">For trades outside the broker sync (e.g. older history or another broker). Broker trades are imported automatically by the portfolio sync service.</div>
        <div class="grid g2"><label class="field">Account<select class="input" id="tx-acc">${BT.accountsWith('portfolio').map((a) => `<option value="${a.id}">${esc(a.name)}</option>`).join('')}</select></label>
        <label class="field">Bucket<select class="input" id="tx-b">${S.buckets.map((b) => `<option>${esc(b)}</option>`).join('')}</select></label>
        <label class="field">Symbol<input class="input" id="tx-sym" list="tx-syms" placeholder="e.g. INFY"><datalist id="tx-syms">${D.INSTRUMENTS.map((i) => `<option value="${i.symbol}">`).join('')}</datalist></label>
        <label class="field">Side<select class="input" id="tx-side"><option>BUY</option><option>SELL</option></select></label>
        <label class="field">Date<input class="input" type="date" id="tx-date" value="${D.DAYS[D.DAYS.length - 1]}"></label>
        <label class="field">Quantity<input class="input" type="number" id="tx-qty" min="1" value="10"></label>
        <label class="field">Price<input class="input" type="number" step="0.05" id="tx-price"></label>
        <label class="field">Charges<input class="input" type="number" step="0.01" id="tx-ch" value="20"></label></div><div class="err" id="tx-err"></div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="tx-ok">Add</button>',
      onMount: (ov, close) => {
        $('#tx-sym', ov).onchange = () => { const s = $('#tx-sym', ov).value.toUpperCase(); if (D.BY_SYMBOL[s]) $('#tx-price', ov).value = D.closeOn(s, $('#tx-date', ov).value); };
        $('#tx-ok', ov).onclick = () => {
          const sym = $('#tx-sym', ov).value.trim().toUpperCase();
          const qty = parseInt($('#tx-qty', ov).value, 10), price = parseFloat($('#tx-price', ov).value), date = $('#tx-date', ov).value;
          if (!D.BY_SYMBOL[sym]) return ($('#tx-err', ov).textContent = 'Unknown symbol — pick one from the master list.');
          if (!(qty > 0) || !(price > 0) || !date) return ($('#tx-err', ov).textContent = 'Enter quantity, price and date.');
          const [y, m, d] = date.split('-').map(Number);
          S.transactions.push({ id: BT.uid('TX'), account: $('#tx-acc', ov).value, symbol: sym, side: $('#tx-side', ov).value, date, t: new Date(y, m - 1, d, 11, 0).getTime(), qty, price, charges: parseFloat($('#tx-ch', ov).value) || 0, bucket: $('#tx-b', ov).value, source: 'manual' });
          BT.audit('owner', 'Manual transaction', `${$('#tx-side', ov).value} ${qty} ${sym} @ ${price} (${date})`);
          BT.save(); close(); BT.rerender(); BT.toast('Transaction added', 'good');
        };
      },
    });
  }
})();
