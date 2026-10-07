/* xDrishti prototype — pages: Data quality, Services (with schedule configuration), Accounts (redesigned), Reports (overhauled) */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const { $, $$, esc, inr, num, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);
  const avg = (a, f) => (a.length ? sum(a, f) / a.length : 0);
  const hm = (t) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };

  /* =====================================================================
     DATA QUALITY — re-aggregate from 1-minute bars and verify everything
     ===================================================================== */
  const TF_CHECK = ['5m', '15m', '30m', '1h', '1D'];
  // independent reference implementation (minute index from 09:15) used to verify the stored aggregates
  function refAggregate(bars, tf) {
    const out = new Map();
    for (const b of bars) {
      const d = new Date(b.t);
      const day = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
      const mi = d.getHours() * 60 + d.getMinutes() - 555;
      const size = tf === '1D' ? 100000 : parseInt(tf, 10) * (tf.endsWith('h') ? 60 : 1);
      const key = tf === '1D' ? day : day + 555 * 60000 + Math.floor(mi / size) * size * 60000;
      const g = out.get(key);
      if (!g) out.set(key, { t: key, open: b.open, high: b.high, low: b.low, close: b.close, volume: b.volume, n: 1 });
      else { g.high = Math.max(g.high, b.high); g.low = Math.min(g.low, b.low); g.close = b.close; g.volume += b.volume; g.n++; }
    }
    return [...out.values()];
  }
  function storedAggregate(sym, day, bars, tf, fixed) {
    const agg = D.aggregate(bars, tf).map((b) => ({ ...b }));
    if (D.hasStaleAggregate(sym, day, fixed) && (tf === '15m' || tf === '1h')) {
      // simulates an aggregate that was not refreshed after late-arriving minute data
      const last = agg[agg.length - 1]; last.close = D.tick(last.close * 0.996); last.volume = Math.round(last.volume * 0.82);
    }
    return agg;
  }
  function dayBars(sym, day, fixed) {
    const imp = BT.mem.imported[sym];
    if (imp) { const b = imp.filter((x) => D.iso(new Date(x.t)) === day); if (b.length) return { bars: b, source: 'CSV' }; }
    return { bars: D.intraday1m(sym, day, { fixed }), source: 'Dhan' };
  }
  BT.dayBarsForQuality = dayBars; BT.storedAggregate = storedAggregate; BT.refAggregate = refAggregate;
  function validateSymbol(sym, days, P) {
    const fixed = !!BT.S.dataFixes[sym];
    const checks = { completeness: [], sanity: 0, dupes: 0, spikes: [], agg: Object.fromEntries(TF_CHECK.map((t) => [t, 0])), official: [], rows: 0 };
    for (const day of days) {
      const { bars } = dayBars(sym, day, fixed);
      checks.rows += bars.length;
      const have = new Set(bars.map((b) => { const d = new Date(b.t); return d.getHours() * 60 + d.getMinutes(); }));
      const missing = []; for (let m = 555; m < 930; m++) if (!have.has(m)) missing.push(m);
      if (missing.length) checks.completeness.push({ day, missing });
      const seen = new Set();
      bars.forEach((b) => { if (b.low > Math.min(b.open, b.close) || b.high < Math.max(b.open, b.close) || b.low <= 0) checks.sanity++; if (seen.has(b.t)) checks.dupes++; seen.add(b.t); });
      const rets = bars.slice(1).map((b, i) => Math.abs(b.close / bars[i].close - 1)).sort((a, b) => a - b);
      const med = rets[Math.floor(rets.length / 2)] || 0;
      bars.slice(1).forEach((b, i) => { const r = Math.abs(b.close / bars[i].close - 1); if (r > Math.max(0.01, med * P.spikeMultiple)) checks.spikes.push({ day, t: b.t, r }); });
      for (const tf of TF_CHECK) {
        const st = storedAggregate(sym, day, bars, tf, fixed), ref = refAggregate(bars, tf);
        const mm = st.filter((s, i) => { const r = ref[i]; return !r || s.t !== r.t || s.open !== r.open || s.high !== r.high || s.low !== r.low || s.close !== r.close || s.volume !== r.volume; }).length + Math.abs(st.length - ref.length);
        checks.agg[tf] += mm;
      }
      const off = D.officialDaily(sym, day, fixed), der = refAggregate(bars, '1D')[0];
      const diffs = ['open', 'high', 'low', 'close'].map((k) => ({ k, d: Math.abs(der[k] / off[k] - 1) * 100 })).filter((x) => x.d > P.tolerancePct);
      const vd = off.volume ? Math.abs(der.volume / off.volume - 1) * 100 : 0;
      if (diffs.length || vd > 1) checks.official.push({ day, diffs, vd, off, der });
    }
    const missingTotal = sum(checks.completeness, (c) => c.missing.length);
    const aggBad = sum(Object.values(checks.agg));
    const list = [
      { id: 'complete', label: 'Completeness (375 one-minute bars per session)', status: missingTotal === 0 ? 'pass' : missingTotal <= 2 ? 'warn' : 'fail', detail: missingTotal ? `${missingTotal} missing minute(s) on ${checks.completeness.map((c) => c.day).join(', ')}` : 'All minutes present' },
      { id: 'sanity', label: 'Bar sanity (low ≤ open/close ≤ high, positive prices)', status: checks.sanity ? 'fail' : 'pass', detail: checks.sanity ? `${checks.sanity} invalid bar(s)` : 'OK' },
      { id: 'dupes', label: 'No duplicate timestamps', status: checks.dupes ? 'fail' : 'pass', detail: checks.dupes ? `${checks.dupes} duplicate(s)` : 'OK' },
      { id: 'spikes', label: `Spikes (move > ${P.spikeMultiple}× median and > 1%)`, status: checks.spikes.length ? 'warn' : 'pass', detail: checks.spikes.length ? checks.spikes.map((x) => `${fmtDT(x.t)} ${pct(x.r, 2)}`).join('; ') : 'None' },
      { id: 'agg', label: 'Stored aggregates = recomputed from 1-minute (5m, 15m, 30m, 1h, 1D)', status: aggBad ? 'fail' : 'pass', detail: aggBad ? Object.entries(checks.agg).filter(([, n]) => n).map(([tf, n]) => `${tf}: ${n} bucket(s) differ`).join(', ') + ' — aggregate not refreshed after late data' : 'All buckets match exactly' },
      { id: 'official', label: `Derived 1D = official daily (±${P.tolerancePct}% OHLC, ±1% volume)`, status: checks.official.length ? (checks.official.some((o) => o.diffs.length) ? 'fail' : 'warn') : 'pass', detail: checks.official.length ? checks.official.map((o) => `${o.day}: ${o.diffs.map((d) => `${d.k} ${d.d.toFixed(2)}%`).join(', ')}${o.vd > 1 ? ` volume ${o.vd.toFixed(1)}%` : ''}`).join('; ') : 'Matches' },
      { id: 'fresh', label: 'Freshness (data up to the last trading day)', status: (BT.S.tracked[sym] || {}).dataTo === D.DAYS[D.DAYS.length - 1] || BT.mem.imported[sym] ? 'pass' : 'warn', detail: 'Last bar ' + ((BT.S.tracked[sym] || {}).dataTo || '—') },
    ];
    const status = list.some((c) => c.status === 'fail') ? 'fail' : list.some((c) => c.status === 'warn') ? 'warn' : 'pass';
    return { sym, status, checks: list, raw: checks, rows: checks.rows, fixed };
  }
  BT.runValidation = function (only) {
    const S = BT.S;
    const P = S.services.find((s) => s.id === 'validation').params;
    const days = D.DAYS.slice(-Math.max(1, Math.min(10, P.daysToCheck || 5)));
    const syms = only ? [only] : Object.keys(S.tracked).filter((s) => S.tracked[s].status === 'ready');
    const prev = (BT.mem.validation && BT.mem.validation.results) || {};
    const results = only ? { ...prev } : {};
    for (const s of syms) results[s] = validateSymbol(s, days, P);
    const vals = Object.values(results);
    const summary = { at: Date.now(), days: days.length, n: vals.length, pass: vals.filter((v) => v.status === 'pass').length, warn: vals.filter((v) => v.status === 'warn').length, fail: vals.filter((v) => v.status === 'fail').length };
    BT.mem.validation = { ...summary, dayList: days, results };
    if (!only) { S.validationRuns = [summary].concat(S.validationRuns || []).slice(0, 20); BT.save(); }
    return summary;
  };
  BT.qualityOf = (sym) => (BT.mem.validation && BT.mem.validation.results[sym] ? BT.mem.validation.results[sym].status : null);
  const statusChip = (s) => `<span class="chip ${s === 'pass' ? 'green' : s === 'warn' ? 'amber' : s === 'fail' ? 'red' : ''}">${s === 'pass' ? '✓ pass' : s === 'warn' ? '⚠ warning' : s === 'fail' ? '✗ fail' : '—'}</span>`;
  const qf = { filter: 'all', q: '' };
  BT.pages.quality = function (el) {
    const S = BT.S;
    if (!BT.mem.validation) BT.runValidation();
    const V = BT.mem.validation;
    const rows = Object.values(V.results).filter((r) => (qf.filter === 'all' || r.status === qf.filter) && (!qf.q || r.sym.includes(qf.q.toUpperCase()))).sort((a, b) => ({ fail: 0, warn: 1, pass: 2 }[a.status] - { fail: 0, warn: 1, pass: 2 }[b.status]) || a.sym.localeCompare(b.sym));
    const score = V.n ? Math.round((V.pass + V.warn * 0.5) / V.n * 100) : 100;
    const aggCell = (r) => TF_CHECK.map((tf) => `<span class="chip ${r.raw.agg[tf] ? 'red' : 'green'}" title="${tf}: ${r.raw.agg[tf] ? r.raw.agg[tf] + ' bucket(s) differ' : 'matches'}">${tf}</span>`).join(' ');
    el.innerHTML = `<div class="page-head"><div><h1>Data quality</h1><p>Every tracked instrument, its coverage and whether every timeframe built from 1-minute data is correct. Runs automatically after the end-of-day fetch.</p></div>
      <div class="row"><a class="btn" href="#/services">Configure validation</a><button class="btn primary" id="dq-run">Run validation now</button></div></div>
      <div class="tiles"><div class="tile"><div class="l">Data quality score</div><div class="v ${score >= 95 ? 'pos' : score >= 85 ? '' : 'neg'}">${score}%</div><div class="s">pass = 1, warning = ½</div></div>
        <div class="tile"><div class="l">Instruments checked</div><div class="v">${V.n}</div><div class="s">last ${V.days} session(s) · ${fmtTime(V.at)}</div></div>
        <div class="tile"><div class="l">Pass</div><div class="v pos">${V.pass}</div></div><div class="tile"><div class="l">Warnings</div><div class="v" style="color:var(--warn)">${V.warn}</div></div>
        <div class="tile"><div class="l">Failures</div><div class="v neg">${V.fail}</div><div class="s">excluded from tomorrow's scan until fixed</div></div></div>
      ${V.fail ? `<div class="banner warn">Fail-closed per instrument: the nightly pipeline skips instruments that fail validation, and tickets on them show a failed pre-trade check. Use <b>Re-fetch</b> / <b>Refresh aggregates</b> to repair.</div>` : '<div class="banner good">All tracked instruments passed — every timeframe matches its 1-minute source.</div>'}
      <div class="card" style="margin-bottom:12px"><div class="card-b row"><div class="seg" id="dq-f">${[['all', 'All'], ['fail', 'Failures'], ['warn', 'Warnings'], ['pass', 'Pass']].map(([k, l]) => `<button data-f="${k}" class="${qf.filter === k ? 'on' : ''}">${l}</button>`).join('')}</div>
        <input class="input" id="dq-q" placeholder="Filter symbol" style="width:180px" value="${esc(qf.q)}"><span class="small muted">Checks: completeness · bar sanity · duplicates · spikes · stored vs recomputed aggregates · derived vs official daily · freshness</span></div></div>
      <div class="card"><div class="card-b flush"><div class="tbl-wrap" style="max-height:620px"><table class="tbl"><thead><tr><th>Instrument</th><th>Status</th><th>Coverage</th><th class="r">1m bars checked</th><th class="r">Missing</th><th>Aggregates vs 1-minute</th><th>Official 1D</th><th class="r">Spikes</th><th>Source</th><th></th></tr></thead><tbody>
        ${rows.map((r) => { const t = S.tracked[r.sym] || {}; const miss = sum(r.raw.completeness, (c) => c.missing.length); return `<tr><td><b>${esc(r.sym)}</b></td><td>${statusChip(r.status)}${r.fixed ? ' <span class="chip">repaired</span>' : ''}</td><td class="small">${t.dataFrom || '—'} → ${t.dataTo || '—'}</td><td class="r num">${r.rows.toLocaleString('en-IN')}</td><td class="r num ${miss ? 'neg' : ''}">${miss}</td><td>${aggCell(r)}</td><td>${statusChip(r.checks.find((c) => c.id === 'official').status)}</td><td class="r num">${r.raw.spikes.length}</td><td>${BT.mem.imported[r.sym] ? 'CSV + Dhan' : 'Dhan'}</td>
          <td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" data-insp="${esc(r.sym)}">Inspect</button>${r.status !== 'pass' ? `<button class="btn sm primary" data-fix="${esc(r.sym)}">${r.checks.find((c) => c.id === 'agg').status === 'fail' ? 'Refresh aggregates' : 'Re-fetch'}</button>` : ''}</div></td></tr>`; }).join('') || '<tr><td colspan="10" class="empty">Nothing in this view.</td></tr>'}
      </tbody></table></div></div></div>
      <div class="card" style="margin-top:14px"><div class="card-h"><h3>Validation runs</h3></div><div class="card-b flush"><table class="tbl"><thead><tr><th>When</th><th class="r">Instruments</th><th class="r">Sessions</th><th class="r">Pass</th><th class="r">Warn</th><th class="r">Fail</th></tr></thead><tbody>
        ${(S.validationRuns || []).map((v) => `<tr><td>${fmtDT(v.at)}</td><td class="r">${v.n}</td><td class="r">${v.days}</td><td class="r pos">${v.pass}</td><td class="r">${v.warn}</td><td class="r neg">${v.fail}</td></tr>`).join('')}</tbody></table></div></div>`;
    $('#dq-run').onclick = () => { BT.runService('validation'); BT.toast('Validation started on the worker service'); };
    $$('#dq-f button').forEach((b) => (b.onclick = () => { qf.filter = b.dataset.f; BT.rerender(); }));
    $('#dq-q').onchange = (e) => { qf.q = e.target.value.trim(); BT.rerender(); };
    $$('[data-insp]').forEach((b) => (b.onclick = () => inspect(b.dataset.insp)));
    $$('[data-fix]').forEach((b) => (b.onclick = () => repair(b.dataset.fix)));
  };
  function repair(sym) {
    const r = BT.mem.validation.results[sym];
    const agg = r.checks.find((c) => c.id === 'agg').status === 'fail';
    BT.S.dataFixes[sym] = true;
    BT.audit('owner', agg ? 'Aggregates refreshed' : 'Day re-fetched', `${sym}: ${agg ? 'continuous aggregates recomputed from 1-minute data' : 'minute data re-downloaded from Dhan'} → re-validated`);
    BT.runValidation(sym); BT.save(); BT.rerender();
    BT.toast(`${sym}: ${BT.qualityOf(sym) === 'pass' ? 'repaired — all checks pass' : 'still has issues'}`, BT.qualityOf(sym) === 'pass' ? 'good' : 'bad');
  }
  function inspect(sym) {
    const V = BT.mem.validation, r = V.results[sym];
    let tf = (r.raw.agg['15m'] ? '15m' : r.raw.agg['1h'] ? '1h' : '15m'), day = V.dayList[V.dayList.length - 1], diffOnly = false;
    BT.drawer({
      title: `${esc(sym)} — data validation`,
      body: `<div class="row">${statusChip(r.status)}<span class="small muted">${V.days} session(s) · ${r.rows} one-minute bars</span><span class="spacer"></span>${r.status !== 'pass' ? '<button class="btn sm primary" id="iq-fix">Repair</button>' : ''}</div>
        <div class="card"><div class="card-h"><h3>Checks</h3></div><div class="card-b flush"><table class="tbl"><tbody>${r.checks.map((c) => `<tr><td>${statusChip(c.status)}</td><td style="white-space:normal"><b>${esc(c.label)}</b><div class="small muted">${esc(c.detail)}</div></td></tr>`).join('')}</tbody></table></div></div>
        <div class="card"><div class="card-h"><h3>Stored aggregate vs recomputed from 1-minute</h3><div class="row"><select class="input" id="iq-day" style="width:auto">${V.dayList.map((d) => `<option ${d === day ? 'selected' : ''}>${d}</option>`).join('')}</select>
          <div class="seg" id="iq-tf">${['5m', '10m', '15m', '30m', '1h', '1D'].map((t) => `<button data-tf="${t}" class="${t === tf ? 'on' : ''}">${t}</button>`).join('')}</div><label class="check small"><input type="checkbox" id="iq-diff"> differences only</label></div></div>
          <div class="card-b flush"><div class="tbl-wrap" style="max-height:360px" id="iq-cmp"></div></div></div>
        <div class="card"><div class="card-h"><h3>Chart (${'<span id="iq-tfl"></span>'}) built from 1-minute</h3></div><div class="card-b"><div class="chart" id="iq-chart"></div></div></div>
        ${r.raw.completeness.length ? `<div class="card"><div class="card-h"><h3>Missing minutes</h3></div><div class="card-b small">${r.raw.completeness.map((c) => `<b>${c.day}</b>: ${c.missing.map((m) => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0')).join(', ')}`).join('<br>')}</div></div>` : ''}`,
      onMount: (ov, close, charts) => {
        const draw = () => {
          const { bars } = dayBars(sym, day, r.fixed);
          const st = storedAggregate(sym, day, bars, tf, r.fixed), ref = refAggregate(bars, tf);
          const keys = ['open', 'high', 'low', 'close', 'volume'];
          const rowsHtml = ref.map((b, i) => {
            const s = st[i] || {};
            const bad = keys.some((k) => s[k] !== b[k]);
            if (diffOnly && !bad) return '';
            return `<tr style="${bad ? 'background:var(--bad-soft)' : ''}"><td>${tf === '1D' ? day : fmtTime(b.t).slice(0, 5)}</td>${keys.map((k) => `<td class="r num">${s[k] === b[k] ? (k === 'volume' ? b[k].toLocaleString('en-IN') : num(b[k])) : `<span class="neg">${k === 'volume' ? (s[k] || 0).toLocaleString('en-IN') : num(s[k])}</span><br><span class="pos small">${k === 'volume' ? b[k].toLocaleString('en-IN') : num(b[k])}</span>`}</td>`).join('')}<td class="r">${b.n}</td><td>${bad ? '<span class="chip red">differs</span>' : '<span class="chip green">=</span>'}</td></tr>`;
          }).join('');
          $('#iq-cmp', ov).innerHTML = `<table class="tbl"><thead><tr><th>Bucket</th><th class="r">Open</th><th class="r">High</th><th class="r">Low</th><th class="r">Close</th><th class="r">Volume</th><th class="r">1m bars</th><th></th></tr></thead><tbody>${rowsHtml || '<tr><td colspan="8" class="empty">No differences.</td></tr>'}</tbody></table><div class="small muted" style="padding:8px 12px">Red = stored value · green = recomputed from 1-minute bars.</div>`;
          charts.forEach((c) => c.dispose()); charts.length = 0; $('#iq-chart', ov).innerHTML = ''; $('#iq-tfl', ov).textContent = tf;
          BT.candles($('#iq-chart', ov), ref, {}, charts);
        };
        $$('#iq-tf button', ov).forEach((b) => (b.onclick = () => { tf = b.dataset.tf; $$('#iq-tf button', ov).forEach((x) => x.classList.toggle('on', x === b)); draw(); }));
        $('#iq-day', ov).onchange = (e) => { day = e.target.value; draw(); };
        $('#iq-diff', ov).onchange = (e) => { diffOnly = e.target.checked; draw(); };
        if ($('#iq-fix', ov)) $('#iq-fix', ov).onclick = () => { close(); repair(sym); };
        draw();
      },
    });
  }

  /* =====================================================================
     SERVICES — status + schedule configuration with dependency checks
     ===================================================================== */
  BT.pages.services = function (el) {
    const S = BT.S;
    const sets = BT.liveSets();
    const issues = BT.scheduleIssues();
    const issueOf = (id) => issues.filter((i) => i.id === id);
    el.innerHTML = `<div class="page-head"><div><h1>Services</h1><p>Configure once — services run on the machine under the service identity, whether or not you are signed in. Schedules, retries and parameters are editable here.</p></div></div>
      ${issues.length ? `<div class="banner ${issues.some((i) => i.level === 'error') ? 'warn' : 'info'}"><div><b>Schedule checks</b><br>${issues.map((i) => `${i.level === 'error' ? '✗' : '⚠'} <b>${esc(S.services.find((s) => s.id === i.id).name)}</b>: ${esc(i.msg)}`).join('<br>')}</div></div>` : '<div class="banner good">Schedule checks passed — dependencies and timing are consistent.</div>'}
      <div class="card" style="margin-bottom:14px"><div class="card-h"><h3>Trading-day timeline</h3><span class="small muted">market 09:15–15:30 · review cutoff ${S.settings.reviewCutoff}</span></div><div class="card-b">${timeline()}</div></div>
      <div class="card"><div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Service</th><th>Schedule</th><th>Runs as</th><th>Status</th><th>Last run</th><th>Next run</th><th></th></tr></thead><tbody>
        ${S.services.map((s) => `<tr><td><b>${esc(s.name)}</b><div class="small muted" style="white-space:normal;max-width:380px">${esc(s.desc)}</div></td>
          <td style="white-space:normal">${esc(BT.schedText(s))}<div class="small muted">retries ${s.retries} · timeout ${s.timeoutMin}m${s.catchUp ? ' · catch-up' : ''}</div>${issueOf(s.id).map((i) => `<div class="small ${i.level === 'error' ? 'neg' : ''}" style="color:${i.level === 'warn' ? 'var(--warn)' : ''}">${i.level === 'error' ? '✗' : '⚠'} ${esc(i.msg.split(' — ')[0])}</div>`).join('')}</td><td class="mono">${esc(s.runsAs)}</td>
          <td>${!s.enabled ? '<span class="chip">paused</span>' : s.status === 'running' ? '<span class="chip blue">running…</span>' : s.lastOutcome === 'failed' ? '<span class="chip red">failed</span>' : '<span class="chip green">healthy</span>'}</td>
          <td>${ago(s.lastRun)}${s.history[0] ? ` <span class="chip">${esc(s.history[0].trigger)}</span>` : ''}</td><td class="small">${s.enabled ? esc(BT.nextOccurrence(s)) : '—'}</td>
          <td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" data-run="${s.id}" ${s.status === 'running' || !s.enabled ? 'disabled' : ''}>${s.id === 'live-feed' ? (S.settings.demoMarketOpen ? 'Stop' : 'Start') : 'Run now'}</button><button class="btn sm primary" data-cfg="${s.id}">Configure</button><button class="btn sm" data-pause="${s.id}">${s.enabled ? 'Pause' : 'Resume'}</button><button class="btn sm" data-hist="${s.id}">History</button></div></td></tr>`).join('')}
      </tbody></table></div></div></div>
      <div class="grid g3" style="margin-top:14px">
        <div class="card"><div class="card-h"><h3>Live feed</h3><span class="chip ${S.settings.demoMarketOpen ? 'green' : ''}">${S.settings.demoMarketOpen ? 'connected' : 'stopped'}</span></div><div class="card-b"><dl class="kv"><dt>Data account</dt><dd>${BT.dataAccount() ? esc(BT.dataAccount().name) : '⚠ none'}</dd><dt>WebSocket</dt><dd>${sets.ws.length} / ${S.services.find((s) => s.id === 'live-feed').params.maxInstruments} instruments</dd><dt>Polled</dt><dd>${sets.poll.length} holdings & alert symbols</dd><dt>Last tick</dt><dd>${BT.lastTick ? fmtTime(BT.lastTick) : '—'}</dd></dl></div></div>
        <div class="card"><div class="card-h"><h3>Backfill jobs</h3></div><div class="card-b">${Object.entries(S.tracked).filter(([, t]) => t.status === 'backfilling').map(([s, t]) => `<div class="small">${esc(s)}</div><div class="progress" style="margin-bottom:8px"><i style="width:${t.progress}%"></i></div>`).join('') || '<div class="small muted">No backfills running.</div>'}</div></div>
        <div class="card"><div class="card-h"><h3>Catch-up & identity</h3></div><div class="card-b small">Services act as <b>service identity</b>, not as you. When the machine was asleep at a scheduled time, services with catch-up enabled run as soon as they are back. Last catch-up: ${(() => { const c = S.services.flatMap((s) => s.history.filter((h) => h.trigger === 'catch-up').map((h) => ({ s, h }))).sort((a, b) => b.h.at - a.h.at)[0]; return c ? `<b>${esc(c.s.name)}</b> ${ago(c.h.at)}` : 'none yet'; })()}.</div></div></div>`;
    $$('[data-run]').forEach((b) => (b.onclick = () => BT.runService(b.dataset.run)));
    $$('[data-cfg]').forEach((b) => (b.onclick = () => configure(S.services.find((x) => x.id === b.dataset.cfg))));
    $$('[data-pause]').forEach((b) => (b.onclick = () => { const s = S.services.find((x) => x.id === b.dataset.pause); s.enabled = !s.enabled; BT.audit('owner', s.enabled ? 'Service resumed' : 'Service paused', s.name); BT.save(); BT.rerender(); }));
    $$('[data-hist]').forEach((b) => (b.onclick = () => { const s = S.services.find((x) => x.id === b.dataset.hist); BT.drawer({ title: esc(s.name) + ' — run history', body: s.history.length ? s.history.map((h) => `<div class="card"><div class="card-h"><h3>${fmtDT(h.at)}</h3><div class="row"><span class="chip">${esc(h.trigger)}</span><span class="chip ${h.outcome === 'success' ? 'green' : 'red'}">${h.outcome}</span></div></div><div class="card-b"><div class="log">${h.log.map(esc).join('\n')}</div></div></div>`).join('') : '<div class="empty">No runs recorded yet — click “Run now”.</div>' }); }));
  };
  function timeline() {
    const S = BT.S;
    const x = (t) => (hm(t) / 1440 * 100).toFixed(2) + '%';
    const marks = [];
    const bands = [];
    for (const s of S.services.filter((y) => y.enabled)) {
      const sc = s.sched;
      if (sc.type === 'daily' || sc.type === 'weekly') marks.push({ at: sc.time, label: s.name.split(' ')[0] + (sc.type === 'weekly' ? ' (' + BT.DAYN[sc.weekday] + ')' : ''), id: s.id });
      if (sc.type === 'window' || sc.type === 'interval') bands.push({ s: sc.start, e: sc.end, label: s.name, dashed: sc.type === 'interval' });
      if (sc.type === 'after') { const p = S.services.find((y) => y.id === sc.after); if (p && p.sched.time) marks.push({ at: p.sched.time, label: '↳ ' + s.name.split(' ')[0], id: s.id, after: true }); }
    }
    marks.sort((a, b) => hm(a.at) - hm(b.at));
    const rowsN = Math.max(1, bands.length);
    return `<div class="tl"><div class="tl-track"><div class="tl-market" style="left:${x('09:15')};width:calc(${x('15:30')} - ${x('09:15')})" title="Market hours"></div>
      <div class="tl-cut" style="left:${x(S.settings.reviewCutoff)}" title="Review cutoff ${S.settings.reviewCutoff}"></div>
      ${[0, 3, 6, 9, 12, 15, 18, 21, 24].map((h) => `<div class="tl-hour" style="left:${h / 24 * 100}%">${String(h).padStart(2, '0')}</div>`).join('')}</div>
      <div class="tl-bands" style="height:${rowsN * 20}px">${bands.map((b, i) => `<div class="tl-band ${b.dashed ? 'dashed' : ''}" style="top:${i * 20}px;left:${x(b.s)};width:calc(${x(b.e)} - ${x(b.s)})" title="${esc(b.label)} ${b.s}–${b.e}">${esc(b.label)}</div>`).join('')}</div>
      <div class="tl-marks">${marks.map((m, i) => `<div class="tl-mark ${m.after ? 'after' : ''} ${hm(m.at) > 1440 * 0.72 ? 'right' : ''}" style="left:${x(m.at)};top:${(i % 3) * 22}px" title="${esc(m.label)} ${m.at}"><i></i>${m.at} ${esc(m.label)}</div>`).join('')}</div></div>`;
  }
  function configure(svc) {
    const S = BT.S;
    const draft = JSON.parse(JSON.stringify(svc));
    const types = svc.id === 'live-feed' ? ['window'] : ['holdings-refresh', 'alerts'].includes(svc.id) ? ['interval'] : svc.id === 'csv-watcher' ? ['continuous'] : ['daily', 'weekly', 'after'];
    const dayBoxes = (days) => BT.DAYN.map((d, i) => `<label class="check small"><input type="checkbox" data-day="${i}" ${days.includes(i) ? 'checked' : ''}>${d}</label>`).join('');
    const paramFields = Object.entries(draft.params).map(([k, v]) => `<label class="field">${esc(k.replace(/([A-Z])/g, ' $1').toLowerCase())}<input class="input" data-param="${k}" value="${esc(v)}" ${typeof v === 'number' ? 'type="number" step="any"' : ''}></label>`).join('');
    BT.modal({
      wide: true, title: 'Configure — ' + svc.name,
      body: `<div class="small muted">${esc(svc.desc)} · runs as <span class="mono">${esc(svc.runsAs)}</span></div>
        <div class="grid g3"><label class="field">Schedule type<select class="input" id="sc-type">${types.map((t) => `<option value="${t}" ${draft.sched.type === t ? 'selected' : ''}>${{ daily: 'Daily at a time', weekly: 'Weekly', after: 'After another service', window: 'Time window (market hours)', interval: 'Repeat every N minutes in a window', continuous: 'Continuous' }[t]}</option>`).join('')}</select></label>
          <label class="field">Enabled<select class="input" id="sc-en"><option value="1" ${draft.enabled ? 'selected' : ''}>Enabled</option><option value="0" ${!draft.enabled ? 'selected' : ''}>Paused</option></select></label><div></div></div>
        <div id="sc-fields"></div>
        <div class="grid g4"><label class="field">Retries<input class="input" type="number" min="0" max="5" id="sc-retries" value="${draft.retries}"></label><label class="field">Timeout (min)<input class="input" type="number" min="1" max="240" id="sc-timeout" value="${draft.timeoutMin}"></label>
          <label class="check" style="margin-top:20px"><input type="checkbox" id="sc-catch" ${draft.catchUp ? 'checked' : ''}> Catch up missed runs</label><label class="check" style="margin-top:20px"><input type="checkbox" id="sc-notify" ${draft.notifyOnFailure ? 'checked' : ''}> Notify on failure</label></div>
        ${paramFields ? `<div><div class="small" style="font-weight:600;margin-bottom:6px">Parameters</div><div class="grid g3">${paramFields}</div></div>` : ''}
        <div id="sc-preview" class="small muted"></div><div id="sc-issues"></div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn" id="sc-default">Restore default</button><button class="btn primary" id="sc-save">Save schedule</button>',
      onMount: (ov, close) => {
        const fields = () => {
          const t = $('#sc-type', ov).value, sc = draft.sched;
          const box = $('#sc-fields', ov);
          if (t === 'daily') box.innerHTML = `<div class="grid g2"><label class="field">Time<input class="input" type="time" id="f-time" value="${sc.time || '18:30'}"></label><div class="field">Days<div class="row" style="gap:8px">${dayBoxes(sc.days || [1, 2, 3, 4, 5])}<button class="btn sm" type="button" id="f-td">Trading days</button><button class="btn sm" type="button" id="f-all">Every day</button></div></div></div>`;
          if (t === 'weekly') box.innerHTML = `<div class="grid g2"><label class="field">Day<select class="input" id="f-wd">${BT.DAYN.map((d, i) => `<option value="${i}" ${sc.weekday === i ? 'selected' : ''}>${d}</option>`).join('')}</select></label><label class="field">Time<input class="input" type="time" id="f-time" value="${sc.time || '10:00'}"></label></div>`;
          if (t === 'after') box.innerHTML = `<label class="field">Run after<select class="input" id="f-after">${S.services.filter((x) => x.id !== svc.id && x.sched.type !== 'continuous').map((x) => `<option value="${x.id}" ${sc.after === x.id ? 'selected' : ''}>${esc(x.name)}</option>`).join('')}</select></label><div class="small muted">Runs automatically each time that service finishes successfully.</div>`;
          if (t === 'window' || t === 'interval') box.innerHTML = `<div class="grid g3"><label class="field">From<input class="input" type="time" id="f-start" value="${sc.start || '09:15'}"></label><label class="field">To<input class="input" type="time" id="f-end" value="${sc.end || '15:30'}"></label>${t === 'interval' ? `<label class="field">Every (minutes)<input class="input" type="number" min="1" max="120" id="f-every" value="${sc.every || 5}"></label>` : '<div></div>'}</div><div class="field">Days<div class="row" style="gap:8px">${dayBoxes(sc.days || [1, 2, 3, 4, 5])}</div></div>`;
          if (t === 'continuous') box.innerHTML = '<div class="small muted">Runs continuously (file watcher).</div>';
          if ($('#f-td', ov)) $('#f-td', ov).onclick = () => { $$('[data-day]', ov).forEach((c) => (c.checked = [1, 2, 3, 4, 5].includes(+c.dataset.day))); check(); };
          if ($('#f-all', ov)) $('#f-all', ov).onclick = () => { $$('[data-day]', ov).forEach((c) => (c.checked = true)); check(); };
          $$('#sc-fields input, #sc-fields select', ov).forEach((i) => (i.oninput = i.onchange = check));
          check();
        };
        const read = () => {
          const t = $('#sc-type', ov).value;
          const days = $$('[data-day]', ov).filter((c) => c.checked).map((c) => +c.dataset.day);
          const sc = { type: t };
          if (t === 'daily') Object.assign(sc, { time: $('#f-time', ov).value, days });
          if (t === 'weekly') Object.assign(sc, { time: $('#f-time', ov).value, weekday: +$('#f-wd', ov).value });
          if (t === 'after') sc.after = $('#f-after', ov).value;
          if (t === 'window' || t === 'interval') Object.assign(sc, { start: $('#f-start', ov).value, end: $('#f-end', ov).value, days }, t === 'interval' ? { every: +$('#f-every', ov).value } : {});
          const params = {};
          $$('[data-param]', ov).forEach((i) => { const orig = draft.params[i.dataset.param]; params[i.dataset.param] = typeof orig === 'number' ? +i.value : i.value; });
          return { ...draft, sched: sc, enabled: $('#sc-en', ov).value === '1', retries: +$('#sc-retries', ov).value, timeoutMin: +$('#sc-timeout', ov).value, catchUp: $('#sc-catch', ov).checked, notifyOnFailure: $('#sc-notify', ov).checked, params };
        };
        const check = () => {
          const d = read();
          if ((d.sched.type === 'daily' && !d.sched.days.length)) { $('#sc-issues', ov).innerHTML = '<div class="banner warn">Choose at least one day.</div>'; $('#sc-save', ov).disabled = true; return; }
          const list = S.services.map((x) => (x.id === svc.id ? d : x));
          const iss = BT.scheduleIssues(list).filter((i) => i.id === svc.id || list.find((x) => x.id === i.id).sched.after === svc.id || ['nightly', 'preopen'].includes(i.id));
          const errs = iss.filter((i) => i.level === 'error');
          $('#sc-issues', ov).innerHTML = iss.length ? `<div class="banner ${errs.length ? 'warn' : 'info'}">${iss.map((i) => `${i.level === 'error' ? '✗' : '⚠'} <b>${esc(S.services.find((s) => s.id === i.id).name)}</b>: ${esc(i.msg)}`).join('<br>')}</div>` : '<div class="banner good">No conflicts with other services.</div>';
          $('#sc-save', ov).disabled = !!errs.length;
          if (d.sched.type === 'daily' || d.sched.type === 'weekly') {
            let t = new Date(); const next = [];
            for (let i = 0; i < 3; i++) { const n = BT.lastOccurrence ? nextRunOf(d.sched, t) : null; if (!n) break; next.push(fmtDT(n)); t = new Date(n + 60000); }
            $('#sc-preview', ov).textContent = 'Next runs: ' + next.join(' · ');
          } else $('#sc-preview', ov).textContent = '';
        };
        $('#sc-type', ov).onchange = () => { draft.sched = { ...draft.sched, type: $('#sc-type', ov).value }; fields(); };
        $$('#sc-retries, #sc-timeout, #sc-en', ov).forEach((i) => (i.onchange = check));
        $('#sc-default', ov).onclick = () => { const def = BT.SERVICE_DEFS.find((x) => x.id === svc.id); draft.sched = JSON.parse(JSON.stringify(def.sched)); draft.params = { ...def.params }; close(); Object.assign(svc, { sched: draft.sched, params: draft.params }); BT.audit('owner', 'Service schedule reset', svc.name); BT.save(); BT.rerender(); BT.toast('Default schedule restored'); };
        $('#sc-save', ov).onclick = () => {
          const d = read();
          const before = BT.schedText(svc);
          Object.assign(svc, { sched: d.sched, enabled: d.enabled, retries: d.retries, timeoutMin: d.timeoutMin, catchUp: d.catchUp, notifyOnFailure: d.notifyOnFailure, params: d.params });
          if (svc.sched.type === 'daily' || svc.sched.type === 'weekly') svc.lastRun = Math.max(svc.lastRun, BT.lastOccurrence(svc.sched) || 0); // don't fire immediately for a time already passed today
          BT.audit('owner', 'Service schedule changed', `${svc.name}: ${before} → ${BT.schedText(svc)}`);
          BT.save(); close(); BT.rerender(); BT.toast('Schedule saved — the service uses it from now on', 'good');
        };
        fields();
      },
    });
  }
  function nextRunOf(sched, now) {
    const [h, m] = sched.time.split(':').map(Number);
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate(), h, m);
    const ok = (x) => (sched.type === 'weekly' ? x.getDay() === sched.weekday : sched.days.includes(x.getDay()));
    if (d <= now) d.setDate(d.getDate() + 1);
    for (let i = 0; i < 8 && !ok(d); i++) d.setDate(d.getDate() + 1);
    return d.getTime();
  }

  /* =====================================================================
     ACCOUNTS — list + detail with tabs, role matrix, risk profiles, wizard
     ===================================================================== */
  const ac = { view: 'accounts', sel: null, tab: 'overview' };
  const ROLE_TXT = {
    data: ['Data', 'blue', 'Instrument master, end-of-day 1-minute fetch, live feed for the active set, quotes. Needs a Dhan Data API subscription.'],
    trading: ['Trading', 'green', 'Receives approved plan tickets sized to its capital and risk profile; fills auto-journaled.'],
    portfolio: ['Portfolio', 'purple', 'Holdings, positions, trade history and ledger synced nightly; included in portfolio views and XIRR.'],
  };
  function accStats(a) {
    const pos = BT.positions().filter((p) => p.account === a.id);
    const open = pos.filter((p) => p.status === 'open');
    return { value: sum(open, (p) => p.value), open: open.length, unreal: sum(open, (p) => p.unrealised), day: sum(open, (p) => p.dir * p.qty * (p.ltp - D.prevClose(p.symbol))), realised: sum(pos, (p) => p.realised) };
  }
  const roleChips = (a) => ['data', 'trading', 'portfolio'].filter((r) => a.roles[r].enabled).map((r) => `<span class="chip ${ROLE_TXT[r][1]}">${ROLE_TXT[r][0]}${r === 'data' ? (a.roles.data.primary ? ' · primary' : ' · standby') : r === 'trading' ? ' · ' + a.roles.trading.mode.replace('_', ' ') : ''}</span>`).join(' ') || '<span class="chip amber">no roles</span>';
  BT.pages.accounts = function (el) {
    const S = BT.S;
    if (!ac.sel || !S.accounts.find((a) => a.id === ac.sel)) ac.sel = S.accounts[0] && S.accounts[0].id;
    const da = BT.dataAccount(), sb = BT.standbyAccount();
    el.innerHTML = `<div class="page-head"><div><h1>Accounts</h1><p>All your broker accounts, what each is used for, and their trading, portfolio, data and connection settings.</p></div>
      <div class="row"><div class="seg" id="ac-view">${[['accounts', 'Accounts'], ['matrix', 'Role matrix'], ['profiles', 'Risk profiles']].map(([k, l]) => `<button data-v="${k}" class="${ac.view === k ? 'on' : ''}">${l}</button>`).join('')}</div><button class="btn primary" id="ac-add">Add account</button></div></div>
      <div class="banner ${da ? 'info' : 'warn'}">${da ? `Market data comes from <b>${esc(da.name)}</b> (primary Data)${sb ? `; <b>${esc(sb.name)}</b> is standby and takes over automatically` : '; no standby configured'}. Trading allocations go to ${BT.accountsWith('trading').map((a) => '<b>' + esc(a.name) + '</b>').join(', ') || 'no account'}; portfolio covers ${BT.accountsWith('portfolio').length} account(s).` : '<b>No primary Data account</b> — market-data services cannot run.'}</div>
      <div id="ac-body"></div>`;
    $$('#ac-view button').forEach((b) => (b.onclick = () => { ac.view = b.dataset.v; BT.rerender(); }));
    $('#ac-add').onclick = wizard;
    const body = $('#ac-body');
    if (ac.view === 'matrix') return matrixView(body);
    if (ac.view === 'profiles') return profilesView(body);
    const a = S.accounts.find((x) => x.id === ac.sel);
    body.innerHTML = `<div class="split"><div class="split-list">${S.accounts.map((x) => { const st = accStats(x); return `<div class="acc-item ${x.id === ac.sel ? 'sel' : ''}" data-acc="${x.id}" style="border-left-color:${x.color}">
        <div class="row between"><b>${esc(x.name)}</b><span class="dot ${x.token.status === 'valid' ? 'green' : 'amber'}" title="token ${x.token.status}"></span></div>
        <div class="small muted">${esc(x.broker)} · ${esc(x.clientId.slice(0, 4))}••${esc(x.clientId.slice(-2))}</div><div class="row" style="gap:4px;margin-top:6px">${roleChips(x)}</div>
        <div class="small" style="margin-top:6px">${inr(st.value)} · ${st.open} open</div></div>`; }).join('')}</div>
      <div class="card split-detail">${a ? detail(a) : '<div class="empty">Add an account to get started.</div>'}</div></div>`;
    $$('[data-acc]').forEach((x) => (x.onclick = () => { ac.sel = x.dataset.acc; BT.rerender(); }));
    if (a) bindDetail(a);
  };
  const TABS = [['overview', 'Overview'], ['roles', 'Roles'], ['trading', 'Trading'], ['portfolio', 'Portfolio'], ['data', 'Data'], ['connection', 'Connection'], ['activity', 'Activity']];
  function detail(a) {
    const S = BT.S;
    const st = accStats(a);
    const t = a.roles.trading, p = a.roles.portfolio, d = a.roles.data;
    const prof = BT.profileOf(a);
    const off = (role) => `<div class="banner">The <b>${ROLE_TXT[role][0]}</b> role is off for this account. <button class="btn sm" data-goto="roles">Enable in Roles</button></div>`;
    let tab = '';
    if (ac.tab === 'overview') tab = `<div class="tiles" style="margin:0"><div class="tile"><div class="l">Holdings value</div><div class="v">${inr(st.value)}</div><div class="s">${st.open} open positions</div></div>
        <div class="tile"><div class="l">Unrealised</div><div class="v ${sgn(st.unreal)}">${inr(st.unreal)}</div><div class="s">realised ${inr(st.realised)}</div></div><div class="tile"><div class="l">Day change</div><div class="v ${sgn(st.day)}">${inr(st.day)}</div></div>
        <div class="tile"><div class="l">Funds available</div><div class="v">${inr(a.funds.available)}</div><div class="s">used ${inr(a.funds.used)}</div></div></div>
      <dl class="kv"><dt>Roles</dt><dd>${roleChips(a)}</dd><dt>Token</dt><dd><span class="dot ${a.token.status === 'valid' ? 'green' : 'amber'}"></span> ${a.token.status} · expires ${fmtDT(a.token.expiresAt)}</dd><dt>Last sync</dt><dd>${ago(a.lastSync)}</dd>
        ${t.enabled ? `<dt>Trading</dt><dd>${t.mode.replace('_', ' ')} · ${esc(prof.name)} profile (${prof.riskPct}% risk/trade) · capital ${inr(t.capital)} · ${t.styles.join(' + ')}</dd>` : ''}
        ${p.enabled ? `<dt>Portfolio</dt><dd>default bucket ${esc(p.defaultBucket)} · history from ${p.historyFrom} · ${p.includeInTotals ? 'included in totals' : 'excluded from totals'}</dd>` : ''}
        ${d.enabled ? `<dt>Data</dt><dd>${d.primary ? 'primary' : 'standby'} · subscription ${d.subscription.active ? 'active, renews ' + d.subscription.renewsOn : 'inactive'}</dd>` : ''}${a.notes ? `<dt>Notes</dt><dd>${esc(a.notes)}</dd>` : ''}</dl>
      <div class="row"><button class="btn" id="ad-sync">Sync now</button><button class="btn" id="ad-test">Test connection</button><a class="btn" href="#/portfolio" id="ad-pf">Open in Portfolio</a></div>`;
    if (ac.tab === 'roles') tab = `<div class="role-toggles">${['data', 'trading', 'portfolio'].map((r) => `<div class="role ${a.roles[r].enabled ? 'on' : ''}"><label class="check"><input type="checkbox" data-role="${r}" ${a.roles[r].enabled ? 'checked' : ''}> <b>${ROLE_TXT[r][0]}</b></label><div class="small muted">${ROLE_TXT[r][2]}</div>
        ${r === 'data' ? `<label class="check small"><input type="radio" name="dp" value="primary" ${d.primary ? 'checked' : ''}> Primary</label><label class="check small"><input type="radio" name="dp" value="standby" ${d.standby ? 'checked' : ''}> Standby (automatic failover)</label>` : ''}</div>`).join('')}</div>
      <div class="small muted">Services pick up role changes on their next run. Role changes ask for your password.</div><div class="err" id="rl-err"></div><div class="row"><button class="btn primary" id="rl-save">Save roles</button></div>`;
    if (ac.tab === 'trading') tab = !t.enabled ? off('trading') : `<div class="grid g3"><label class="field">Mode<select class="input" id="tr-mode"><option value="paper" ${t.mode === 'paper' ? 'selected' : ''}>Paper</option><option value="manual_live" ${t.mode === 'manual_live' ? 'selected' : ''}>Manual live</option><option disabled>Semi-auto / auto (later phase)</option></select></label>
        <label class="field">Risk profile<select class="input" id="tr-prof">${S.riskProfiles.map((x) => `<option value="${x.id}" ${t.profile === x.id ? 'selected' : ''}>${esc(x.name)}</option>`).join('')}</select></label>
        <label class="field">Capital source<select class="input" id="tr-src"><option value="fixed" ${t.capitalSource === 'fixed' ? 'selected' : ''}>Fixed amount</option><option value="broker_funds" ${t.capitalSource === 'broker_funds' ? 'selected' : ''}>Broker funds (capped)</option></select></label>
        <label class="field">Capital / cap (₹)<input class="input" type="number" id="tr-cap" value="${t.capital}"></label></div>
      <div class="field">Styles<div class="row"><label class="check"><input type="checkbox" id="tr-intra" ${t.styles.includes('intraday') ? 'checked' : ''}> Intraday</label><label class="check"><input type="checkbox" id="tr-swing" ${t.styles.includes('swing') ? 'checked' : ''}> Swing</label></div></div>
      <div class="grid g3">${[['intraday', 'Intraday instruments', ['EQ_MIS', 'FUT']], ['swingBuy', 'Swing BUY instruments', ['EQ_CNC', 'FUT']], ['swingSell', 'Swing SELL instruments', ['FUT']]].map(([k, l, opts]) => `<div class="field">${l}<div class="row">${opts.map((o) => `<label class="check small"><input type="checkbox" data-ins="${k}:${o}" ${t.instruments[k].includes(o) ? 'checked' : ''}> ${o}</label>`).join('')}</div></div>`).join('')}</div>
      <div><div class="small" style="font-weight:600;margin:6px 0">Overrides for this account (blank = use profile)</div><div class="grid g4">${[['riskPct', 'Risk / trade %'], ['maxNewEntries', 'Max new entries/day'], ['maxOpen', 'Max open positions'], ['dailyLossPct', 'Daily loss limit %']].map(([k, l]) => `<label class="field">${l}<input class="input" type="number" step="any" data-ov="${k}" placeholder="${(S.riskProfiles.find((x) => x.id === t.profile) || {})[k]}" value="${t.overrides[k] ?? ''}"></label>`).join('')}</div></div>
      <div class="small muted">Effective: ${prof.riskPct}% risk per trade = ${inr(t.capital * prof.riskPct / 100)} per 1R · max ${prof.maxNewEntries} new entries/day · max ${prof.maxOpen} open · daily loss ${prof.dailyLossPct}%</div>
      <div class="err" id="tr-err"></div><div class="row"><button class="btn primary" id="tr-save">Save trading settings</button></div>`;
    if (ac.tab === 'portfolio') tab = !p.enabled ? off('portfolio') : `<div class="grid g3"><label class="field">Default bucket<select class="input" id="pf-b">${S.buckets.map((b) => `<option ${p.defaultBucket === b ? 'selected' : ''}>${esc(b)}</option>`).join('')}</select></label>
        <label class="field">Import trade history from<input class="input" type="date" id="pf-from" value="${p.historyFrom}"></label><label class="check" style="margin-top:20px"><input type="checkbox" id="pf-tot" ${p.includeInTotals ? 'checked' : ''}> Include in portfolio totals</label></div>
      <div class="row"><input class="input" id="pf-newb" placeholder="New bucket name" style="width:200px"><button class="btn sm" id="pf-addb">Add bucket</button><span class="small muted">Buckets: ${S.buckets.map(esc).join(', ')}</span></div>
      <div class="row"><button class="btn primary" id="pf-save">Save portfolio settings</button></div>`;
    if (ac.tab === 'data') tab = !d.enabled ? off('data') : `<dl class="kv"><dt>Role</dt><dd>${d.primary ? '<b>Primary</b> — all market-data services use this account' : 'Standby — takes over if the primary fails'}</dd><dt>Data API subscription</dt><dd>${d.subscription.active ? '<span class="chip green">active</span> renews ' + d.subscription.renewsOn + ' (₹499 + taxes / 30 days)' : '<span class="chip red">inactive</span>'}</dd>
        <dt>Live-feed budget</dt><dd><input class="input" type="number" id="dt-budget" value="${d.liveBudget}" style="width:120px"> instruments (Dhan allows 5 × 5,000)</dd><dt>Last EOD fetch</dt><dd>${ago(S.services.find((s) => s.id === 'eod-fetch').lastRun)}</dd></dl>
      <div class="row"><button class="btn primary" id="dt-save">Save</button>${d.primary && BT.standbyAccount() ? '<button class="btn bad" id="dt-fail">Simulate primary failure → failover</button>' : ''}</div>`;
    if (ac.tab === 'connection') tab = `<div class="grid g3"><label class="field">Display name<input class="input" id="cn-name" value="${esc(a.name)}"></label><label class="field">Broker<select class="input" id="cn-broker"><option>Dhan</option><option disabled>Upstox (later phase)</option></select></label><label class="field">Client id<input class="input" id="cn-cid" value="${esc(a.clientId)}"></label></div>
      <div class="grid g3"><label class="field">Credential reference (Keychain)<input class="input mono" id="cn-cred" value="${esc(a.credRef)}"></label><label class="field">Token method<input class="input" value="${esc(a.connection.tokenMethod)}" disabled></label><label class="field">Colour<input type="color" id="cn-color" value="${a.color}" style="height:34px"></label></div>
      <label class="field">Notes<input class="input" id="cn-notes" value="${esc(a.notes)}" placeholder="e.g. used for long-term SIP-style buys"></label>
      <dl class="kv"><dt>Token</dt><dd>${a.token.status} · expires ${fmtDT(a.token.expiresAt)}</dd><dt>Last error</dt><dd>${a.connection.lastError ? esc(a.connection.lastError) : 'none'}</dd></dl>
      <div class="small muted">API key & secret live in the macOS Keychain and reach services as Podman secrets — never stored here or shown to the AI.</div>
      <div class="row"><button class="btn primary" id="cn-save">Save</button><button class="btn" id="cn-refresh">Refresh token</button><button class="btn" id="cn-test">Test connection</button><span class="spacer"></span><button class="btn bad" id="cn-del">Remove account</button></div>`;
    if (ac.tab === 'activity') { const items = S.audit.filter((x) => x.detail.includes(a.name) || x.detail.includes(a.id)).slice(0, 40); tab = `<table class="tbl"><thead><tr><th>When</th><th>Actor</th><th>Action</th><th>Detail</th></tr></thead><tbody>${items.map((x) => `<tr><td>${fmtDT(x.at)}</td><td>${esc(x.actor)}</td><td>${esc(x.action)}</td><td style="white-space:normal">${esc(x.detail)}</td></tr>`).join('') || '<tr><td colspan="4" class="empty">No activity yet.</td></tr>'}</tbody></table>`; }
    return `<div class="card-h"><h3><span class="dot" style="background:${a.color}"></span> ${esc(a.name)}</h3><div class="row">${roleChips(a)}</div></div>
      <div class="tabs" id="ac-tabs">${TABS.map(([k, l]) => `<button data-tab="${k}" class="${ac.tab === k ? 'on' : ''}">${l}</button>`).join('')}</div><div class="card-b grid">${tab}</div>`;
  }
  function bindDetail(a) {
    const S = BT.S;
    const done = (msg) => { BT.save(); BT.rerender(); BT.toast(msg, 'good'); };
    $$('#ac-tabs button').forEach((b) => (b.onclick = () => { ac.tab = b.dataset.tab; BT.rerender(); }));
    $$('[data-goto]').forEach((b) => (b.onclick = () => { ac.tab = b.dataset.goto; BT.rerender(); }));
    const test = (btn) => { btn.disabled = true; btn.textContent = 'Testing…'; setTimeout(() => { a.token = { status: 'valid', expiresAt: Date.now() + 24 * 3600000 }; a.connection.lastError = null; BT.audit('system', 'Connection test', `${a.name}: OK${a.roles.data.enabled ? ', Data API active' : ''}`); done(`${a.name}: connected · token valid`); }, 900); };
    if ($('#ad-sync')) $('#ad-sync').onclick = () => { BT.runService('portfolio-sync'); BT.toast('Sync started on the service'); };
    if ($('#ad-test')) $('#ad-test').onclick = (e) => test(e.target);
    if ($('#ad-pf')) $('#ad-pf').onclick = () => { BT.pfFilterAccount && BT.pfFilterAccount(a.id); };
    if ($('#rl-save')) {
      $$('[data-role]').forEach((c) => (c.onchange = () => c.closest('.role').classList.toggle('on', c.checked)));
      $('#rl-save').onclick = async () => {
        const on = Object.fromEntries($$('[data-role]').map((c) => [c.dataset.role, c.checked]));
        const dp = ($('input[name=dp]:checked') || {}).value;
        if (on.data && !dp) return ($('#rl-err').textContent = 'Choose Primary or Standby for the Data role.');
        const others = S.accounts.filter((x) => x.id !== a.id);
        const otherPrimary = others.find((x) => x.roles.data.enabled && x.roles.data.primary);
        if (!(on.data && dp === 'primary') && !otherPrimary) return ($('#rl-err').textContent = 'There must be exactly one primary Data account — make another account primary first.');
        if (on.trading && !(a.roles.trading.capital > 0)) a.roles.trading.capital = 100000;
        if (!(await BT.stepUp('Changing account roles'))) return;
        if (on.data && dp === 'primary' && otherPrimary) { otherPrimary.roles.data.primary = false; otherPrimary.roles.data.standby = true; BT.toast(`${otherPrimary.name} is now standby`); }
        if (on.data && dp === 'standby') others.filter((x) => x.roles.data.standby).forEach((x) => { x.roles.data.standby = false; x.roles.data.enabled = x.roles.data.primary; });
        a.roles.data.enabled = on.data; a.roles.data.primary = on.data && dp === 'primary'; a.roles.data.standby = on.data && dp === 'standby';
        a.roles.trading.enabled = on.trading; a.roles.portfolio.enabled = on.portfolio;
        BT.audit('owner', 'Account roles updated', `${a.name}: ${Object.keys(on).filter((k) => on[k]).join(', ') || 'none'}${a.roles.data.primary ? ' (primary data)' : ''}`);
        done('Roles saved — services pick them up on the next run');
      };
    }
    if ($('#tr-save')) $('#tr-save').onclick = async () => {
      const t = a.roles.trading;
      const styles = [$('#tr-intra').checked && 'intraday', $('#tr-swing').checked && 'swing'].filter(Boolean);
      if (!styles.length) return ($('#tr-err').textContent = 'Choose at least one style.');
      const cap = +$('#tr-cap').value; if (!(cap > 0)) return ($('#tr-err').textContent = 'Capital must be > 0.');
      const ins = { intraday: [], swingBuy: [], swingSell: [] }; $$('[data-ins]').filter((c) => c.checked).forEach((c) => { const [k, o] = c.dataset.ins.split(':'); ins[k].push(o); });
      if (styles.includes('intraday') && !ins.intraday.length) return ($('#tr-err').textContent = 'Pick at least one intraday instrument type.');
      const ov = {}; $$('[data-ov]').forEach((i) => { if (i.value !== '') ov[i.dataset.ov] = +i.value; });
      if (ov.riskPct != null && (ov.riskPct <= 0 || ov.riskPct > 2)) return ($('#tr-err').textContent = 'Risk per trade must be between 0 and 2%.');
      if (!(await BT.stepUp('Changing trading risk settings'))) return;
      Object.assign(t, { mode: $('#tr-mode').value, profile: $('#tr-prof').value, capitalSource: $('#tr-src').value, capital: cap, styles, instruments: ins, overrides: ov });
      BT.audit('owner', 'Trading settings changed', `${a.name}: ${t.mode}, ${t.profile}, capital ${cap}, ${styles.join('+')}`);
      done('Trading settings saved — applied from the next nightly pipeline');
    };
    if ($('#pf-save')) {
      $('#pf-addb').onclick = () => { const n = $('#pf-newb').value.trim(); if (n && !S.buckets.includes(n)) { S.buckets.push(n); BT.audit('owner', 'Bucket added', n); done('Bucket added'); } };
      $('#pf-save').onclick = () => { Object.assign(a.roles.portfolio, { defaultBucket: $('#pf-b').value, historyFrom: $('#pf-from').value, includeInTotals: $('#pf-tot').checked }); BT.audit('owner', 'Portfolio settings changed', a.name); done('Portfolio settings saved'); };
    }
    if ($('#dt-save')) $('#dt-save').onclick = () => { a.roles.data.liveBudget = +$('#dt-budget').value || 200; BT.audit('owner', 'Data settings changed', a.name); done('Saved'); };
    if ($('#dt-fail')) $('#dt-fail').onclick = () => {
      const sb = BT.standbyAccount();
      a.token = { status: 'expired', expiresAt: Date.now() }; a.connection.lastError = 'Token refresh failed (simulated)';
      a.roles.data.primary = false; a.roles.data.standby = true; sb.roles.data.primary = true; sb.roles.data.standby = false;
      BT.notify(`Data failover: ${a.name} failed — ${sb.name} is now the primary Data account`, 'warn', '#/accounts');
      BT.audit('system', 'Data failover', `${a.name} → ${sb.name}`);
      done('Failover performed by the services');
    };
    if ($('#cn-save')) {
      $('#cn-save').onclick = async () => {
        const credChanged = $('#cn-cred').value.trim() !== a.credRef || $('#cn-cid').value.trim() !== a.clientId;
        if (credChanged && !(await BT.stepUp('Changing account credentials'))) return;
        Object.assign(a, { name: $('#cn-name').value.trim() || a.name, clientId: $('#cn-cid').value.trim(), credRef: $('#cn-cred').value.trim(), color: $('#cn-color').value, notes: $('#cn-notes').value });
        BT.audit('owner', 'Account connection updated', a.name); done('Saved');
      };
      $('#cn-refresh').onclick = () => { a.token = { status: 'valid', expiresAt: Date.now() + 24 * 3600000 }; BT.audit('system', 'Token refreshed', a.name); done('Token refreshed (API key & secret flow)'); };
      $('#cn-test').onclick = (e) => test(e.target);
      $('#cn-del').onclick = async () => {
        if (a.roles.data.primary && !BT.standbyAccount()) return BT.toast('This is the only Data account — add another Data account first', 'bad');
        if (!(await BT.confirmDlg('Remove ' + a.name, 'Services stop using this account. Its history is kept.', 'Remove', true))) return;
        if (!(await BT.stepUp('Removing an account'))) return;
        S.accounts = S.accounts.filter((x) => x.id !== a.id);
        if (a.roles.data.primary) { const s = BT.standbyAccount(); if (s) { s.roles.data.primary = true; s.roles.data.standby = false; } }
        BT.audit('owner', 'Account removed', a.name); ac.sel = null; done('Account removed');
      };
    }
  }
  function matrixView(body) {
    const S = BT.S;
    body.innerHTML = `<div class="card"><div class="card-h"><h3>Role matrix</h3><span class="small muted">tick to change roles (password confirmation)</span></div><div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Account</th><th>Data</th><th>Primary data</th><th>Trading</th><th>Mode</th><th>Risk profile</th><th class="r">Capital</th><th>Portfolio</th><th>Default bucket</th><th>Token</th></tr></thead><tbody>
      ${S.accounts.map((a) => `<tr><td><span class="dot" style="background:${a.color}"></span> <b>${esc(a.name)}</b></td><td><input type="checkbox" data-m="${a.id}:data" ${a.roles.data.enabled ? 'checked' : ''}></td><td><input type="radio" name="mprim" data-p="${a.id}" ${a.roles.data.primary ? 'checked' : ''} ${a.roles.data.enabled ? '' : 'disabled'}></td>
        <td><input type="checkbox" data-m="${a.id}:trading" ${a.roles.trading.enabled ? 'checked' : ''}></td><td>${a.roles.trading.enabled ? a.roles.trading.mode.replace('_', ' ') : '—'}</td><td>${a.roles.trading.enabled ? esc(BT.profileOf(a).name) : '—'}</td><td class="r num">${a.roles.trading.enabled ? inr(a.roles.trading.capital) : '—'}</td>
        <td><input type="checkbox" data-m="${a.id}:portfolio" ${a.roles.portfolio.enabled ? 'checked' : ''}></td><td>${a.roles.portfolio.enabled ? esc(a.roles.portfolio.defaultBucket) : '—'}</td><td><span class="dot ${a.token.status === 'valid' ? 'green' : 'amber'}"></span> ${a.token.status}</td></tr>`).join('')}
    </tbody></table></div></div></div>`;
    $$('[data-m]').forEach((c) => (c.onchange = async () => {
      const [id, role] = c.dataset.m.split(':'); const a = S.accounts.find((x) => x.id === id);
      if (role === 'data' && !c.checked && a.roles.data.primary) { c.checked = true; return BT.toast('Make another account primary Data first', 'bad'); }
      if (!(await BT.stepUp('Changing account roles'))) { c.checked = !c.checked; return; }
      a.roles[role].enabled = c.checked; if (role === 'data') { a.roles.data.standby = c.checked && !a.roles.data.primary; if (!c.checked) a.roles.data.standby = false; }
      BT.audit('owner', 'Account roles updated', `${a.name}: ${role} ${c.checked ? 'on' : 'off'}`); BT.save(); BT.rerender();
    }));
    $$('[data-p]').forEach((r) => (r.onchange = async () => {
      if (!(await BT.stepUp('Changing the primary Data account'))) return BT.rerender();
      S.accounts.forEach((a) => { if (a.roles.data.enabled) { a.roles.data.primary = a.id === r.dataset.p; a.roles.data.standby = a.id !== r.dataset.p; } });
      BT.audit('owner', 'Primary data account changed', BT.dataAccount().name); BT.save(); BT.rerender();
    }));
  }
  function profilesView(body) {
    const S = BT.S;
    const F = [['riskPct', 'Risk/trade %', 0.05], ['maxNewEntries', 'Max new entries/day', 1], ['maxOpen', 'Max open positions', 1], ['dailyLossPct', 'Daily loss %', 0.1], ['maxPerSector', 'Max per sector', 1], ['heatPct', 'Max open risk (heat) %', 0.5]];
    body.innerHTML = `<div class="card"><div class="card-h"><h3>Risk profiles</h3><span class="small muted">Shared by Trading accounts; each account can override values</span></div><div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Profile</th>${F.map((f) => `<th>${f[1]}</th>`).join('')}<th>Used by</th><th></th></tr></thead><tbody>
      ${S.riskProfiles.map((p) => `<tr><td><input class="input" data-pn="${p.id}" value="${esc(p.name)}" style="width:140px"></td>${F.map(([k, , st]) => `<td><input class="input num" type="number" step="${st}" data-pf="${p.id}:${k}" value="${p[k]}" style="width:90px"></td>`).join('')}
        <td class="small">${S.accounts.filter((a) => a.roles.trading.enabled && a.roles.trading.profile === p.id).map((a) => esc(a.name)).join(', ') || '—'}</td><td><button class="btn sm bad" data-pdel="${p.id}" ${S.accounts.some((a) => a.roles.trading.profile === p.id) ? 'disabled title="in use"' : ''}>✕</button></td></tr>`).join('')}
    </tbody></table></div><div class="row" style="padding:12px 14px"><button class="btn" id="pr-add">Add profile</button><span class="spacer"></span><button class="btn primary" id="pr-save">Save profiles</button></div></div></div>`;
    $('#pr-add').onclick = () => { S.riskProfiles.push({ id: BT.uid('rp-'), name: 'New profile', riskPct: 0.5, maxNewEntries: 3, maxOpen: 5, dailyLossPct: 1.5, maxPerSector: 2, heatPct: 3 }); BT.rerender(); };
    $$('[data-pdel]').forEach((b) => (b.onclick = () => { S.riskProfiles = S.riskProfiles.filter((p) => p.id !== b.dataset.pdel); BT.save(); BT.rerender(); }));
    $('#pr-save').onclick = async () => {
      const bad = $$('[data-pf]').find((i) => !(+i.value > 0));
      if (bad) return BT.toast('All values must be positive', 'bad');
      if ($$('[data-pf]').some((i) => i.dataset.pf.endsWith(':riskPct') && +i.value > 2)) return BT.toast('Risk per trade above 2% is not allowed', 'bad');
      if (!(await BT.stepUp('Changing risk profiles'))) return;
      $$('[data-pf]').forEach((i) => { const [id, k] = i.dataset.pf.split(':'); S.riskProfiles.find((p) => p.id === id)[k] = +i.value; });
      $$('[data-pn]').forEach((i) => { S.riskProfiles.find((p) => p.id === i.dataset.pn).name = i.value.trim() || 'Profile'; });
      BT.audit('owner', 'Risk profiles changed', S.riskProfiles.map((p) => `${p.name} ${p.riskPct}%`).join(', ')); BT.save(); BT.rerender(); BT.toast('Risk profiles saved', 'good');
    };
  }
  function wizard() {
    const S = BT.S;
    const d = { name: '', broker: 'Dhan', clientId: '', credRef: 'xdrishti/dhan/', roles: { data: false, dp: 'standby', trading: false, portfolio: true }, mode: 'paper', profile: 'standard', capital: 200000, styles: ['swing'], bucket: 'Trading', tested: false };
    let step = 0;
    const STEPS = ['Broker & identity', 'Credentials', 'Roles', 'Role settings', 'Review'];
    const m = BT.modal({ wide: true, title: 'Add broker account', body: '<div id="wz"></div>', foot: '<button class="btn" id="wz-back">Back</button><span class="spacer"></span><button class="btn primary" id="wz-next">Next</button>' });
    const ov = m.el;
    const render = () => {
      const steps = `<div class="steps">${STEPS.map((s, i) => `<span class="${i === step ? 'on' : i < step ? 'done' : ''}">${i + 1}. ${s}</span>`).join('')}</div>`;
      let b = '';
      if (step === 0) b = `<div class="grid g3"><label class="field">Broker<select class="input" id="w-broker"><option>Dhan</option><option disabled>Upstox (later)</option></select></label><label class="field">Display name<input class="input" id="w-name" value="${esc(d.name)}" placeholder="e.g. Dhan · Swing 2"></label><label class="field">Client id<input class="input" id="w-cid" value="${esc(d.clientId)}"></label></div>`;
      if (step === 1) b = `<label class="field">Keychain item holding the API key & secret<input class="input mono" id="w-cred" value="${esc(d.credRef)}"></label><div class="small muted">Create the item in macOS Keychain first; the app only stores its name. Services receive it as a Podman secret.</div><div class="row"><button class="btn" id="w-test">${d.tested ? '✓ Connection OK' : 'Test connection'}</button></div>`;
      if (step === 2) b = `<div class="role-toggles">${['data', 'trading', 'portfolio'].map((r) => `<div class="role ${d.roles[r] ? 'on' : ''}"><label class="check"><input type="checkbox" data-wr="${r}" ${d.roles[r] ? 'checked' : ''}> <b>${ROLE_TXT[r][0]}</b></label><div class="small muted">${ROLE_TXT[r][2]}</div>${r === 'data' ? `<label class="check small"><input type="radio" name="wdp" value="primary" ${d.roles.dp === 'primary' ? 'checked' : ''}> Primary</label><label class="check small"><input type="radio" name="wdp" value="standby" ${d.roles.dp === 'standby' ? 'checked' : ''}> Standby</label>` : ''}</div>`).join('')}</div>`;
      if (step === 3) b = `${d.roles.trading ? `<div class="grid g4"><label class="field">Mode<select class="input" id="w-mode"><option value="paper">Paper</option><option value="manual_live">Manual live</option></select></label><label class="field">Risk profile<select class="input" id="w-prof">${S.riskProfiles.map((p) => `<option value="${p.id}">${esc(p.name)}</option>`).join('')}</select></label><label class="field">Capital (₹)<input class="input" type="number" id="w-cap" value="${d.capital}"></label><div class="field">Styles<div class="row"><label class="check small"><input type="checkbox" id="w-intra"> Intraday</label><label class="check small"><input type="checkbox" id="w-swing" checked> Swing</label></div></div></div>` : '<div class="small muted">Trading role off.</div>'}
        ${d.roles.portfolio ? `<label class="field">Default portfolio bucket<select class="input" id="w-b">${S.buckets.map((x) => `<option>${esc(x)}</option>`).join('')}</select></label>` : ''}`;
      if (step === 4) b = `<dl class="kv"><dt>Account</dt><dd>${esc(d.name)} · ${esc(d.broker)} · ${esc(d.clientId)}</dd><dt>Credentials</dt><dd class="mono">${esc(d.credRef)} ${d.tested ? '✓ tested' : '(not tested)'}</dd><dt>Roles</dt><dd>${['data', 'trading', 'portfolio'].filter((r) => d.roles[r]).map((r) => ROLE_TXT[r][0] + (r === 'data' ? ' (' + d.roles.dp + ')' : '')).join(', ') || 'none'}</dd>
        ${d.roles.trading ? `<dt>Trading</dt><dd>${d.mode} · ${d.profile} · ${inr(d.capital)} · ${d.styles.join('+')}</dd>` : ''}${d.roles.portfolio ? `<dt>Portfolio bucket</dt><dd>${esc(d.bucket)}</dd>` : ''}</dl><div class="banner info">After you confirm with your password, services start using this account on their next run (portfolio sync backfills trade history).</div>`;
      $('#wz', ov).innerHTML = steps + `<div class="grid" style="margin-top:12px">${b}</div><div class="err" id="w-err"></div>`;
      $('#wz-back', ov).disabled = step === 0; $('#wz-next', ov).textContent = step === 4 ? 'Create account' : 'Next';
      if ($('#w-test', ov)) $('#w-test', ov).onclick = (e) => { e.target.textContent = 'Testing…'; setTimeout(() => { d.tested = true; render(); }, 800); };
      $$('[data-wr]', ov).forEach((c) => (c.onchange = () => { d.roles[c.dataset.wr] = c.checked; c.closest('.role').classList.toggle('on', c.checked); }));
    };
    const collect = () => {
      const err = (t) => { $('#w-err', ov).textContent = t; return false; };
      if (step === 0) { d.name = $('#w-name', ov).value.trim(); d.clientId = $('#w-cid', ov).value.trim(); if (!d.name || !d.clientId) return err('Name and client id are required.'); }
      if (step === 1) { d.credRef = $('#w-cred', ov).value.trim(); if (!d.credRef) return err('Credential reference is required.'); }
      if (step === 2) { const dp = $('input[name=wdp]:checked', ov); d.roles.dp = dp ? dp.value : 'standby'; if (!d.roles.data && !d.roles.trading && !d.roles.portfolio) return err('Choose at least one role.'); }
      if (step === 3) { if (d.roles.trading) { d.mode = $('#w-mode', ov).value; d.profile = $('#w-prof', ov).value; d.capital = +$('#w-cap', ov).value; d.styles = [$('#w-intra', ov).checked && 'intraday', $('#w-swing', ov).checked && 'swing'].filter(Boolean); if (!(d.capital > 0) || !d.styles.length) return err('Capital and at least one style are required.'); } if (d.roles.portfolio) d.bucket = $('#w-b', ov).value; }
      return true;
    };
    $('#wz-back', ov).onclick = () => { step = Math.max(0, step - 1); render(); };
    $('#wz-next', ov).onclick = async () => {
      if (!collect()) return;
      if (step < 4) { step++; render(); return; }
      if (!(await BT.stepUp('Adding an account'))) return;
      const id = BT.uid('acc_');
      const prim = d.roles.data && d.roles.dp === 'primary';
      if (prim) S.accounts.forEach((x) => { if (x.roles.data.primary) { x.roles.data.primary = false; x.roles.data.standby = true; } });
      S.accounts.push({ id, name: d.name, broker: d.broker, clientId: d.clientId, credRef: d.credRef,
        roles: { data: { enabled: d.roles.data, primary: prim, standby: d.roles.data && !prim }, trading: { enabled: d.roles.trading, mode: d.mode, profile: d.profile, capital: d.capital, styles: d.styles }, portfolio: { enabled: d.roles.portfolio, defaultBucket: d.bucket } },
        token: { status: 'valid', expiresAt: Date.now() + 24 * 3600000 }, lastSync: Date.now() });
      BT.reNormalize && BT.reNormalize();
      BT.audit('owner', 'Account added', `${d.name}: ${['data', 'trading', 'portfolio'].filter((r) => d.roles[r]).join(', ')}`);
      ac.sel = id; ac.view = 'accounts'; ac.tab = 'overview'; BT.save(); m.close(); BT.rerender(); BT.toast('Account added', 'good');
    };
    render();
  }

  /* =====================================================================
     REPORTS — overview dashboard, basket comparison, pivot and analytics
     ===================================================================== */
  const DIMS = { strategy: 'Strategy', tf: 'Timeframe', basket: 'Basket', side: 'Side', style: 'Style', exit: 'Exit policy', regime: 'Regime', account: 'Account', symbol: 'Instrument', sector: 'Sector', weekday: 'Weekday', month: 'Month', decision: 'Decision' };
  const METRICS = { exp: ['Expectancy (R)', (s) => s.exp], total: ['Total R', (s) => s.total], win: ['Win rate', (s) => s.win * 100], n: ['Trades', (s) => s.n], pf: ['Profit factor', (s) => s.pf || 0] };
  const VIEWS = [['overview', 'Overview'], ['baskets', 'Basket comparison'], ['strategies', 'Strategies'], ['exits', 'Exit policies'], ['sideregime', 'Side & regime'], ['calendar', 'Calendar & time'], ['maemfe', 'MAE / MFE'], ['calibration', 'Calibration'], ['instruments', 'Instruments'], ['accounts', 'Accounts'], ['decisions', 'Decisions'], ['pivot', 'Pivot builder'], ['trades', 'Trade list']];
  const rp = { view: 'overview', from: null, to: null, baskets: [], side: '', style: '', account: '', cmp: null, row: 'strategy', col: 'tf', metric: 'exp', page: 0 };
  const dimVal = (t, d) => (d === 'account' ? BT.accName(t.account) : t[d]);
  function stats(list) {
    const s = BT.tradeStats(list);
    let c = 0, peak = 0, dd = 0; list.forEach((t) => { c += t.R; peak = Math.max(peak, c); dd = Math.min(dd, c - peak); });
    return { ...s, dd };
  }
  const color = (v, max) => { const a = Math.min(1, Math.abs(v) / (max || 1)); return v >= 0 ? `rgba(26,127,55,${0.12 + a * 0.55})` : `rgba(207,34,46,${0.12 + a * 0.55})`; };
  function heat(rows, cols, cell, fmt) {
    const vals = rows.flatMap((r) => cols.map((c) => cell(r, c))).filter((x) => x && x.n);
    const max = Math.max(...vals.map((x) => Math.abs(x.v)), 0.01);
    return `<div class="tbl-wrap"><table class="tbl heat"><thead><tr><th></th>${cols.map((c) => `<th class="r">${esc(c)}</th>`).join('')}</tr></thead><tbody>${rows.map((r) => `<tr><td><b>${esc(r)}</b></td>${cols.map((c) => { const x = cell(r, c); return x && x.n ? `<td class="r num" style="background:${color(x.v, max)}" title="n=${x.n}">${fmt(x.v)}<span class="small muted"> ·${x.n}</span></td>` : '<td class="r muted">—</td>'; }).join('')}</tr>`).join('')}</tbody></table></div>`;
  }
  function insights(f) {
    const out = [];
    const by = (d) => { const g = {}; f.forEach((t) => (g[dimVal(t, d)] = g[dimVal(t, d)] || []).push(t)); return Object.entries(g).map(([k, l]) => ({ k, s: stats(l) })).filter((x) => x.s.n >= 15); };
    const best = (d) => by(d).sort((a, b) => b.s.exp - a.s.exp);
    const st = best('strategy'); if (st.length) { out.push(`Best strategy: <b>${esc(st[0].k)}</b> ${st[0].s.exp.toFixed(2)}R/trade (n=${st[0].s.n}); weakest <b>${esc(st[st.length - 1].k)}</b> ${st[st.length - 1].s.exp.toFixed(2)}R — candidate for pausing.`); }
    const bk = best('basket'); if (bk.length > 1) out.push(`Basket <b>${esc(bk[0].k)}</b> outperforms <b>${esc(bk[bk.length - 1].k)}</b> by ${(bk[0].s.exp - bk[bk.length - 1].s.exp).toFixed(2)}R per trade.`);
    const sd = by('side'); const buy = sd.find((x) => x.k === 'BUY'), sell = sd.find((x) => x.k === 'SELL'); if (buy && sell) out.push(`Longs ${buy.s.exp.toFixed(2)}R vs shorts ${sell.s.exp.toFixed(2)}R — ${buy.s.exp > sell.s.exp ? 'shorts are the weaker side; consider stricter filters for SELL setups' : 'shorts are carrying the system'}.`);
    const ex = best('exit'); if (ex.length) out.push(`Best exit policy overall: <b>${esc(ex[0].k)}</b> (${ex[0].s.exp.toFixed(2)}R).`);
    const rg = best('regime'); if (rg.length) out.push(`Most favourable regime: <b>${esc(rg[0].k)}</b> (${rg[0].s.exp.toFixed(2)}R); least: <b>${esc(rg[rg.length - 1].k)}</b> (${rg[rg.length - 1].s.exp.toFixed(2)}R).`);
    const wd = best('weekday'); if (wd.length) out.push(`Best weekday: <b>${esc(wd[0].k)}</b>; worst <b>${esc(wd[wd.length - 1].k)}</b>.`);
    const cal = f.filter((t) => t.pWin != null); if (cal.length) { const pr = avg(cal, (t) => t.pWin), ac2 = cal.filter((t) => t.R > 0).length / cal.length; out.push(`Calibration: predicted ${Math.round(pr * 100)}% wins vs actual ${Math.round(ac2 * 100)}% — ${Math.abs(pr - ac2) <= 0.05 ? 'well calibrated' : 'needs recalibration'}.`); }
    return out;
  }
  BT.pages.reports = function (el) {
    const S = BT.S;
    const all = BT.tradesData();
    rp.from = rp.from || D.DAYS[D.DAYS.length - 251]; rp.to = rp.to || D.DAYS[D.DAYS.length - 1];
    const basketNames = S.baskets.map((b) => b.name);
    if (!rp.cmp) rp.cmp = S.baskets.filter((b) => b.purpose === 'trading' || b.type === 'manual').slice(0, 3).map((b) => b.name);
    const base = all.filter((t) => t.date >= rp.from && t.date <= rp.to && (!rp.baskets.length || rp.baskets.includes(t.basket)) && (!rp.side || t.side === rp.side) && (!rp.style || t.style === rp.style) && (!rp.account || t.account === rp.account));
    const f = rp.view === 'decisions' || rp.view === 'trades' ? base : base.filter((t) => t.taken);
    const st = stats(f);
    el.innerHTML = `<div class="page-head"><div><h1>Reports</h1><p>${all.length} graded trades (demo). Pick a report on the left; filters apply to every report.</p></div></div>
      <div class="card" style="margin-bottom:12px"><div class="card-b row">
        <label class="field" style="flex-direction:row;align-items:center;gap:6px">From <input class="input" type="date" id="rp-from" value="${rp.from}" style="width:auto"></label><label class="field" style="flex-direction:row;align-items:center;gap:6px">To <input class="input" type="date" id="rp-to" value="${rp.to}" style="width:auto"></label>
        <div class="seg" id="rp-pre">${[['1M', 21], ['3M', 63], ['6M', 126], ['1Y', 250], ['2Y', 500]].map(([l, n]) => `<button data-n="${n}">${l}</button>`).join('')}</div>
        <details class="ms"><summary class="btn">Baskets: ${rp.baskets.length ? rp.baskets.map(esc).join(', ') : 'all'} ▾</summary><div class="ms-pop">${basketNames.map((n) => `<label class="check"><input type="checkbox" data-fb="${esc(n)}" ${rp.baskets.includes(n) ? 'checked' : ''}> ${esc(n)}</label>`).join('')}<button class="btn sm" id="rp-ball">All</button></div></details>
        <select class="input" id="rp-side" style="width:auto"><option value="">Both sides</option><option ${rp.side === 'BUY' ? 'selected' : ''}>BUY</option><option ${rp.side === 'SELL' ? 'selected' : ''}>SELL</option></select>
        <select class="input" id="rp-style" style="width:auto"><option value="">All styles</option><option value="intraday" ${rp.style === 'intraday' ? 'selected' : ''}>Intraday</option><option value="swing" ${rp.style === 'swing' ? 'selected' : ''}>Swing</option></select>
        <select class="input" id="rp-acc" style="width:auto"><option value="">All accounts</option>${S.accounts.map((a) => `<option value="${a.id}" ${rp.account === a.id ? 'selected' : ''}>${esc(a.name)}</option>`).join('')}</select></div></div>
      <div class="split"><nav class="split-list rp-nav">${VIEWS.map(([k, l]) => `<a href="#" data-view="${k}" class="${rp.view === k ? 'on' : ''}">${l}</a>`).join('')}</nav>
      <div class="split-detail" style="min-width:0">
        <div class="tiles"><div class="tile"><div class="l">Trades</div><div class="v">${st.n}</div><div class="s">${rp.view === 'decisions' || rp.view === 'trades' ? 'incl. not taken' : 'taken'}</div></div><div class="tile"><div class="l">Win rate</div><div class="v">${pct(st.win).replace('+', '')}</div><div class="s">avg +${st.avgW.toFixed(2)}R / ${st.avgL.toFixed(2)}R</div></div>
          <div class="tile"><div class="l">Expectancy</div><div class="v ${sgn(st.exp)}">${st.exp.toFixed(2)}R</div></div><div class="tile"><div class="l">Total</div><div class="v ${sgn(st.total)}">${st.total.toFixed(1)}R</div><div class="s ${sgn(st.pnl)}">${inr(st.pnl)}</div></div>
          <div class="tile"><div class="l">Profit factor</div><div class="v">${st.pf ? st.pf.toFixed(2) : '—'}</div></div><div class="tile"><div class="l">Max drawdown</div><div class="v neg">${st.dd.toFixed(1)}R</div></div></div>
        <div id="rp-body"></div></div></div>`;
    const go = () => BT.rerender();
    $$('[data-view]').forEach((a) => (a.onclick = (e) => { e.preventDefault(); rp.view = a.dataset.view; rp.page = 0; go(); }));
    $('#rp-from').onchange = (e) => { rp.from = e.target.value; go(); }; $('#rp-to').onchange = (e) => { rp.to = e.target.value; go(); };
    $$('#rp-pre button').forEach((b) => (b.onclick = () => { rp.from = D.DAYS[Math.max(0, D.DAYS.length - 1 - +b.dataset.n)]; rp.to = D.DAYS[D.DAYS.length - 1]; go(); }));
    $$('[data-fb]').forEach((c) => (c.onchange = () => { rp.baskets = $$('[data-fb]').filter((x) => x.checked).map((x) => x.dataset.fb); go(); }));
    $('#rp-ball').onclick = () => { rp.baskets = []; go(); };
    $('#rp-side').onchange = (e) => { rp.side = e.target.value; go(); }; $('#rp-style').onchange = (e) => { rp.style = e.target.value; go(); }; $('#rp-acc').onchange = (e) => { rp.account = e.target.value; go(); };
    const body = $('#rp-body');
    const card = (title, inner, cls = '') => `<div class="card ${cls}"><div class="card-h"><h3>${title}</h3></div><div class="card-b">${inner}</div></div>`;
    const group = (list, d) => { const g = {}; list.forEach((t) => (g[dimVal(t, d)] = g[dimVal(t, d)] || []).push(t)); return g; };
    const uniq = (list, d) => [...new Set(list.map((t) => dimVal(t, d)))].sort();
    const cum = (list) => { let c = 0; return list.map((t) => [t.date, +(c += t.R).toFixed(2)]); };
    const V = rp.view;
    if (!f.length) { body.innerHTML = '<div class="card"><div class="empty">No trades match the filters.</div></div>'; return; }
    if (V === 'overview') {
      body.innerHTML = `<div class="grid g2">${card('Auto-insights', `<ul class="insights">${insights(f).map((x) => `<li>${x}</li>`).join('')}</ul>`)}${card('Equity curve & drawdown (R)', '<div class="chart" id="c1"></div>')}</div>
        <div class="grid g2" style="margin-top:14px">${card('Monthly result (R)', '<div class="chart" id="c2"></div>')}${card('Strategy × timeframe — expectancy (R)', heat(uniq(f, 'strategy'), uniq(f, 'tf'), (r, c) => { const l = f.filter((t) => t.strategy === r && t.tf === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}</div>
        <div class="grid g3" style="margin-top:14px">${card('Expectancy by basket', '<div class="chart sm" id="c3"></div>')}${card('R distribution', '<div class="chart sm" id="c4"></div>')}${card('Win rate by weekday', '<div class="chart sm" id="c5"></div>')}</div>`;
      const eq = cum(f); let peak = 0;
      BT.echart($('#c1'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 24 }, xAxis: { type: 'category', data: eq.map((x) => x[0]) }, yAxis: { type: 'value' }, series: [{ name: 'Cumulative R', type: 'line', showSymbol: false, data: eq.map((x) => x[1]) }, { name: 'Drawdown', type: 'line', showSymbol: false, areaStyle: { opacity: 0.15 }, color: '#f43f5e', data: eq.map((x) => { peak = Math.max(peak, x[1]); return +(x[1] - peak).toFixed(2); }) }] });
      const months = uniq(f, 'month'); const mg = group(f, 'month');
      BT.echart($('#c2'), { tooltip: { trigger: 'axis' }, grid: { left: 40, right: 10, top: 10, bottom: 40 }, xAxis: { type: 'category', data: months, axisLabel: { rotate: 45 } }, yAxis: { type: 'value' }, series: [{ type: 'bar', data: months.map((m) => { const v = +sum(mg[m], (t) => t.R).toFixed(1); return { value: v, itemStyle: { color: v >= 0 ? '#10b981' : '#f43f5e' } }; }) }] });
      const bg = group(f, 'basket'); const bks = Object.keys(bg);
      BT.echart($('#c3'), { tooltip: { trigger: 'axis' }, grid: { left: 100, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'value' }, yAxis: { type: 'category', data: bks }, series: [{ type: 'bar', data: bks.map((b) => { const v = +stats(bg[b]).exp.toFixed(2); return { value: v, itemStyle: { color: v >= 0 ? '#10b981' : '#f43f5e' } }; }) }] });
      const bins = {}; f.forEach((t) => { const b = Math.floor(t.R * 2) / 2; bins[b] = (bins[b] || 0) + 1; }); const ks = Object.keys(bins).map(Number).sort((a, b) => a - b);
      BT.echart($('#c4'), { tooltip: {}, grid: { left: 36, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'category', data: ks.map((k) => k + 'R') }, yAxis: { type: 'value' }, series: [{ type: 'bar', data: ks.map((k) => ({ value: bins[k], itemStyle: { color: k >= 0 ? '#10b981' : '#f43f5e' } })) }] });
      const wd = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']; const wg = group(f, 'weekday');
      BT.echart($('#c5'), { tooltip: { trigger: 'axis', valueFormatter: (v) => v + '%' }, grid: { left: 36, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'category', data: wd }, yAxis: { type: 'value', min: 0, max: 100 }, series: [{ type: 'bar', data: wd.map((d) => (wg[d] ? +(stats(wg[d]).win * 100).toFixed(1) : 0)) }] });
    } else if (V === 'baskets') {
      const pool = all.filter((t) => t.taken && t.date >= rp.from && t.date <= rp.to && (!rp.side || t.side === rp.side) && (!rp.style || t.style === rp.style) && (!rp.account || t.account === rp.account));
      const sel = rp.cmp.filter((n) => basketNames.includes(n));
      const rows = sel.map((n) => { const l = pool.filter((t) => t.basket === n); const b = S.baskets.find((x) => x.name === n); const ms = (b ? b.members : []).filter((s) => D.BY_SYMBOL[s] && D.BY_SYMBOL[s].type === 'EQ'); const bh = ms.length ? avg(ms, (s) => D.closeOn(s, rp.to) / D.closeOn(s, rp.from) - 1) : null; const strat = Object.entries(group(l, 'strategy')).map(([k, x]) => ({ k, e: stats(x).exp, n: x.length })).filter((x) => x.n >= 5).sort((a, b) => b.e - a.e)[0]; return { n, l, s: stats(l), bh, strat }; });
      const strategies = uniq(pool.filter((t) => sel.includes(t.basket)), 'strategy');
      body.innerHTML = card(`Compare baskets <span class="small muted">— select 2 or more</span>`, `<div class="row">${basketNames.map((n) => `<label class="check chip ${sel.includes(n) ? 'blue' : ''}" style="padding:4px 10px"><input type="checkbox" data-cmp="${esc(n)}" ${sel.includes(n) ? 'checked' : ''}> ${esc(n)}</label>`).join('')}</div>`) +
        (sel.length < 2 ? '<div class="banner warn" style="margin-top:12px">Select at least two baskets to compare.</div>' : `
        <div class="card" style="margin-top:14px"><div class="card-b flush"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Basket</th><th class="r">Trades</th><th class="r">Win</th><th class="r">Expectancy</th><th class="r">Total R</th><th class="r">PF</th><th class="r">Max DD</th><th class="r">Net ₹</th><th>Best strategy</th><th class="r">Buy & hold</th></tr></thead><tbody>
        ${rows.sort((a, b) => b.s.exp - a.s.exp).map((r, i) => `<tr><td><b>${esc(r.n)}</b> ${i === 0 ? '<span class="chip green">best</span>' : ''}</td><td class="r num">${r.s.n}</td><td class="r num">${pct(r.s.win).replace('+', '')}</td><td class="r num ${sgn(r.s.exp)}">${r.s.exp.toFixed(2)}R</td><td class="r num ${sgn(r.s.total)}">${r.s.total.toFixed(1)}</td><td class="r num">${r.s.pf ? r.s.pf.toFixed(2) : '—'}</td><td class="r num neg">${r.s.dd.toFixed(1)}</td><td class="r num ${sgn(r.s.pnl)}">${inr(r.s.pnl)}</td><td>${r.strat ? esc(r.strat.k) + ' ' + r.strat.e.toFixed(2) + 'R' : '—'}</td><td class="r num ${sgn(r.bh)}">${pct(r.bh)}</td></tr>`).join('')}</tbody></table></div></div></div>
        <div class="grid g2" style="margin-top:14px">${card('Cumulative R by basket', '<div class="chart" id="b1"></div>')}${card('Expectancy by strategy and basket', '<div class="chart" id="b2"></div>')}</div>
        <div class="grid g2" style="margin-top:14px">${card('Basket × side — expectancy', heat(sel, ['BUY', 'SELL'], (r, c) => { const l = pool.filter((t) => t.basket === r && t.side === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}${card('Basket × style — expectancy', heat(sel, ['intraday', 'swing'], (r, c) => { const l = pool.filter((t) => t.basket === r && t.style === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}</div>`);
      $$('[data-cmp]').forEach((c) => (c.onchange = () => { rp.cmp = $$('[data-cmp]').filter((x) => x.checked).map((x) => x.dataset.cmp); go(); }));
      if (sel.length >= 2) {
        BT.echart($('#b1'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 24 }, xAxis: { type: 'time' }, yAxis: { type: 'value' }, series: rows.map((r) => ({ name: r.n, type: 'line', showSymbol: false, data: cum(r.l).map(([d, v]) => [d, v]) })) });
        BT.echart($('#b2'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 60 }, xAxis: { type: 'category', data: strategies, axisLabel: { rotate: 30 } }, yAxis: { type: 'value' }, series: rows.map((r) => ({ name: r.n, type: 'bar', data: strategies.map((s) => { const l = r.l.filter((t) => t.strategy === s); return l.length >= 3 ? +stats(l).exp.toFixed(2) : null; }) })) });
      }
    } else if (V === 'strategies') {
      const strategies = uniq(f, 'strategy');
      body.innerHTML = `<div class="grid g2">${card('Strategy × timeframe — expectancy', heat(strategies, uniq(f, 'tf'), (r, c) => { const l = f.filter((t) => t.strategy === r && t.tf === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}${card('Strategy × regime — expectancy', heat(strategies, uniq(f, 'regime'), (r, c) => { const l = f.filter((t) => t.strategy === r && t.regime === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}</div>
        <div style="margin-top:14px">${card('Equity by strategy (R)', '<div class="chart lg" id="s1"></div>')}</div>`;
      const g = group(f, 'strategy');
      BT.echart($('#s1'), { tooltip: { trigger: 'axis' }, legend: { top: 0, type: 'scroll' }, grid: { left: 40, right: 10, top: 34, bottom: 24 }, xAxis: { type: 'time' }, yAxis: { type: 'value' }, series: strategies.map((s) => ({ name: s, type: 'line', showSymbol: false, data: cum(g[s]) })) });
    } else if (V === 'exits') {
      const exits = uniq(f, 'exit'), strategies = uniq(f, 'strategy');
      body.innerHTML = card('Exit policy × strategy — expectancy', heat(strategies, exits, (r, c) => { const l = f.filter((t) => t.strategy === r && t.exit === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2))) + `<div style="margin-top:14px">${card('Expectancy by exit policy', '<div class="chart" id="e1"></div>')}</div>`;
      const g = group(f, 'exit');
      BT.echart($('#e1'), { tooltip: { trigger: 'axis' }, grid: { left: 120, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'value' }, yAxis: { type: 'category', data: exits }, series: [{ type: 'bar', data: exits.map((e) => { const v = +stats(g[e]).exp.toFixed(2); return { value: v, itemStyle: { color: v >= 0 ? '#10b981' : '#f43f5e' } }; }) }] });
    } else if (V === 'sideregime') {
      body.innerHTML = `<div class="grid g2">${card('Longs vs shorts (cumulative R)', '<div class="chart" id="r1"></div>')}${card('Side × regime — expectancy', heat(['BUY', 'SELL'], uniq(f, 'regime'), (r, c) => { const l = f.filter((t) => t.side === r && t.regime === c); return l.length ? { v: stats(l).exp, n: l.length } : null; }, (v) => v.toFixed(2)))}</div>`;
      const g = group(f, 'side');
      BT.echart($('#r1'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 24 }, xAxis: { type: 'time' }, yAxis: { type: 'value' }, series: ['BUY', 'SELL'].filter((s) => g[s]).map((s) => ({ name: s, type: 'line', showSymbol: false, data: cum(g[s]), color: s === 'BUY' ? '#10b981' : '#f43f5e' })) });
    } else if (V === 'calendar') {
      body.innerHTML = `<div class="grid g3">${card('Expectancy by weekday', '<div class="chart sm" id="k1"></div>')}${card('Expectancy by entry hour (intraday)', '<div class="chart sm" id="k2"></div>')}${card('Total R by month', '<div class="chart sm" id="k3"></div>')}</div>`;
      const bar = (el, cats, g) => BT.echart(el, { tooltip: { trigger: 'axis' }, grid: { left: 36, right: 10, top: 10, bottom: 30 }, xAxis: { type: 'category', data: cats }, yAxis: { type: 'value' }, series: [{ type: 'bar', data: cats.map((c) => { const v = g[c] ? +g[c] : 0; return { value: v, itemStyle: { color: v >= 0 ? '#10b981' : '#f43f5e' } }; }) }] });
      const wg = group(f, 'weekday'), hg = group(f.filter((t) => t.style === 'intraday'), 'hour'), mg = group(f, 'month');
      bar($('#k1'), ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'], Object.fromEntries(Object.entries(wg).map(([k, l]) => [k, stats(l).exp.toFixed(2)])));
      bar($('#k2'), ['9', '10', '11', '12', '13', '14'], Object.fromEntries(Object.entries(hg).map(([k, l]) => [k, stats(l).exp.toFixed(2)])));
      bar($('#k3'), uniq(f, 'month'), Object.fromEntries(Object.entries(mg).map(([k, l]) => [k, sum(l, (t) => t.R).toFixed(1)])));
    } else if (V === 'maemfe') {
      body.innerHTML = `<div class="grid g2">${card('MAE vs final R <span class="small muted">— are stops too tight?</span>', '<div class="chart lg" id="m1"></div>')}${card('MFE of all trades <span class="small muted">— are targets too greedy?</span>', '<div class="chart lg" id="m2"></div>')}</div>
        <div class="banner info" style="margin-top:12px">Winners' worst excursion: 90th percentile ${(() => { const w = f.filter((t) => t.R > 0).map((t) => -t.mae).sort((a, b) => a - b); return w.length ? w[Math.floor(w.length * 0.9)].toFixed(2) : '—'; })()}R — a 1R stop leaves room. ${Math.round(f.filter((t) => t.mfe >= 2).length / f.length * 100)}% of trades reached +2R at some point; ${Math.round(f.filter((t) => t.mfe >= 3).length / f.length * 100)}% reached +3R.</div>`;
      BT.echart($('#m1'), { tooltip: { formatter: (p) => `MAE ${p.value[0]}R → ${p.value[1]}R` }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 30 }, xAxis: { type: 'value', name: 'MAE (R)' }, yAxis: { type: 'value', name: 'Result (R)' }, series: [{ name: 'Winners', type: 'scatter', symbolSize: 6, color: '#10b981', data: f.filter((t) => t.R > 0).map((t) => [t.mae, t.R]) }, { name: 'Losers', type: 'scatter', symbolSize: 6, color: '#f43f5e', data: f.filter((t) => t.R <= 0).map((t) => [t.mae, t.R]) }] });
      const bins = {}; f.forEach((t) => { const b = Math.floor(t.mfe * 2) / 2; bins[b] = (bins[b] || 0) + 1; }); const ks = Object.keys(bins).map(Number).sort((a, b) => a - b);
      BT.echart($('#m2'), { tooltip: {}, grid: { left: 36, right: 10, top: 10, bottom: 30 }, xAxis: { type: 'category', data: ks.map((k) => k + 'R') }, yAxis: { type: 'value' }, series: [{ type: 'bar', data: ks.map((k) => bins[k]), markLine: { symbol: 'none', data: [{ xAxis: '2R' }, { xAxis: '3R' }], label: { formatter: 'target {b}' } } }] });
    } else if (V === 'calibration') {
      const bks = []; for (let p = 0.3; p < 0.75; p += 0.05) bks.push(p);
      const rows = bks.map((p) => { const l = f.filter((t) => t.pWin >= p && t.pWin < p + 0.05); return { p, n: l.length, act: l.length ? l.filter((t) => t.R > 0).length / l.length : null }; }).filter((r) => r.n);
      const err = sum(rows, (r) => Math.abs(r.p + 0.025 - r.act) * r.n) / Math.max(1, sum(rows, (r) => r.n));
      body.innerHTML = `<div class="grid g2">${card('Reliability: predicted vs actual win rate', '<div class="chart lg" id="cl1"></div>')}${card('Calibration table', `<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Predicted</th><th class="r">Trades</th><th class="r">Actual win rate</th></tr></thead><tbody>${rows.map((r) => `<tr><td>${Math.round(r.p * 100)}–${Math.round((r.p + 0.05) * 100)}%</td><td class="r">${r.n}</td><td class="r num">${Math.round(r.act * 100)}%</td></tr>`).join('')}</tbody></table></div><div class="banner ${err <= 0.05 ? 'good' : 'warn'}" style="margin-top:10px">Mean calibration error ${(err * 100).toFixed(1)} pp — gate is ≤ 5 pp.</div>`)}</div>`;
      BT.echart($('#cl1'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 30 }, xAxis: { type: 'value', min: 0.25, max: 0.8, name: 'predicted' }, yAxis: { type: 'value', min: 0, max: 1, name: 'actual' }, series: [{ name: 'Perfect', type: 'line', showSymbol: false, lineStyle: { type: 'dashed' }, data: [[0.25, 0.25], [0.8, 0.8]] }, { name: 'Model', type: 'line', data: rows.map((r) => [+(r.p + 0.025).toFixed(3), +r.act.toFixed(3)]) }] });
    } else if (V === 'instruments' || V === 'accounts') {
      const d = V === 'instruments' ? 'symbol' : 'account';
      const g = group(f, d);
      const rows = Object.entries(g).map(([k, l]) => ({ k, s: stats(l), best: Object.entries(group(l, 'strategy')).map(([s, x]) => ({ s, e: stats(x).exp, n: x.length })).sort((a, b) => b.e - a.e)[0] })).sort((a, b) => b.s.total - a.s.total);
      body.innerHTML = `<div class="grid g2">${card(V === 'instruments' ? 'Instrument scorecard' : 'Account scorecard', `<div class="tbl-wrap" style="max-height:520px"><table class="tbl"><thead><tr><th>${DIMS[d]}</th><th class="r">Trades</th><th class="r">Win</th><th class="r">Exp.</th><th class="r">Total R</th><th class="r">Net ₹</th><th>Best strategy</th></tr></thead><tbody>${rows.map((r) => `<tr><td><b>${esc(r.k)}</b></td><td class="r">${r.s.n}</td><td class="r num">${pct(r.s.win).replace('+', '')}</td><td class="r num ${sgn(r.s.exp)}">${r.s.exp.toFixed(2)}</td><td class="r num ${sgn(r.s.total)}">${r.s.total.toFixed(1)}</td><td class="r num ${sgn(r.s.pnl)}">${inr(r.s.pnl)}</td><td>${r.best ? esc(r.best.s) : '—'}</td></tr>`).join('')}</tbody></table></div>`)}
        ${card('Top & bottom by total R', '<div class="chart lg" id="i1"></div>')}</div>`;
      const tb = rows.length > 16 ? rows.slice(0, 8).concat(rows.slice(-8)) : rows;
      BT.echart($('#i1'), { tooltip: { trigger: 'axis' }, grid: { left: 110, right: 10, top: 10, bottom: 24 }, xAxis: { type: 'value' }, yAxis: { type: 'category', data: tb.map((r) => r.k), inverse: true }, series: [{ type: 'bar', data: tb.map((r) => ({ value: +r.s.total.toFixed(1), itemStyle: { color: r.s.total >= 0 ? '#10b981' : '#f43f5e' } })) }] });
    } else if (V === 'decisions') {
      const groups = ['approved', 'modified', 'rejected', 'lapsed'].map((k) => ({ k, s: stats(f.filter((t) => t.decision === k)) }));
      const rej = groups.find((g) => g.k === 'rejected').s.total + groups.find((g) => g.k === 'lapsed').s.total;
      const reserve = stats(f.filter((t) => t.reserve)), primary = stats(f.filter((t) => !t.reserve));
      body.innerHTML = `<div class="banner ${rej > 0 ? 'warn' : 'good'}">Tickets you rejected or let lapse would have made <b>${rej > 0 ? '+' : ''}${rej.toFixed(1)}R</b> — ${rej > 0 ? 'your overrides cost performance; review your rejection reasons.' : 'your overrides saved performance.'}</div>
        <div class="grid g2">${card('Your decisions', `<table class="tbl"><thead><tr><th>Decision</th><th class="r">Tickets</th><th class="r">Win</th><th class="r">Exp.</th><th class="r">Total R</th></tr></thead><tbody>${groups.map((g) => `<tr><td>${BT.ticketChip(g.k)}</td><td class="r">${g.s.n}</td><td class="r num">${pct(g.s.win).replace('+', '')}</td><td class="r num ${sgn(g.s.exp)}">${g.s.exp.toFixed(2)}R</td><td class="r num ${sgn(g.s.total)}">${g.s.total.toFixed(1)}R</td></tr>`).join('')}</tbody></table>`)}
        ${card('Selector effectiveness', `<table class="tbl"><thead><tr><th>Ticket rank</th><th class="r">Trades</th><th class="r">Exp.</th></tr></thead><tbody><tr><td>Primary (top N)</td><td class="r">${primary.n}</td><td class="r num ${sgn(primary.exp)}">${primary.exp.toFixed(2)}R</td></tr><tr><td>Reserve</td><td class="r">${reserve.n}</td><td class="r num ${sgn(reserve.exp)}">${reserve.exp.toFixed(2)}R</td></tr></tbody></table><div class="small muted" style="margin-top:8px">${primary.exp > reserve.exp ? 'Ranking adds value: primary tickets beat the reserve list.' : 'Reserve tickets do as well as primary — the ranking needs attention.'}</div>`)}</div>`;
    } else if (V === 'pivot') {
      const rowsK = uniq(f, rp.row), colsK = uniq(f, rp.col); const [ml, mf] = METRICS[rp.metric];
      body.innerHTML = card('Pivot builder', `<div class="row" style="margin-bottom:12px"><label class="field" style="flex-direction:row;align-items:center;gap:6px">Rows <select class="input" id="pv-r" style="width:auto">${Object.entries(DIMS).map(([k, l]) => `<option value="${k}" ${rp.row === k ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
          <label class="field" style="flex-direction:row;align-items:center;gap:6px">Columns <select class="input" id="pv-c" style="width:auto">${Object.entries(DIMS).map(([k, l]) => `<option value="${k}" ${rp.col === k ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
          <label class="field" style="flex-direction:row;align-items:center;gap:6px">Metric <select class="input" id="pv-m" style="width:auto">${Object.entries(METRICS).map(([k, [l]]) => `<option value="${k}" ${rp.metric === k ? 'selected' : ''}>${l}</option>`).join('')}</select></label></div>` +
        (rowsK.length * colsK.length > 4000 ? '<div class="banner warn">Too many cells — choose smaller dimensions.</div>' : heat(rowsK, colsK, (r, c) => { const l = f.filter((t) => String(dimVal(t, rp.row)) === String(r) && String(dimVal(t, rp.col)) === String(c)); if (!l.length) return null; const s = stats(l); return { v: mf(s), n: l.length }; }, (v) => (rp.metric === 'win' ? v.toFixed(0) + '%' : rp.metric === 'n' ? v : v.toFixed(2)))));
      $('#pv-r').onchange = (e) => { rp.row = e.target.value; go(); }; $('#pv-c').onchange = (e) => { rp.col = e.target.value; go(); }; $('#pv-m').onchange = (e) => { rp.metric = e.target.value; go(); };
    } else if (V === 'trades') {
      const rows = f.slice().reverse();
      body.innerHTML = `<div class="card" data-nocollapse="1"><div class="card-b flush"><div class="tbl-wrap grid-scroll"><table class="tbl"><thead><tr><th>Date</th><th>Instrument</th><th>Basket</th><th>Setup</th><th>Side</th><th>Exit</th><th>Account</th><th>Decision</th><th class="r">P(win)</th><th class="r">R plan</th><th class="r">R fills</th><th class="r">P&L</th><th>Loss tags</th></tr></thead><tbody>
        ${rows.map((t) => `<tr><td>${t.date}</td><td><b>${t.symbol}</b></td><td>${esc(t.basket)}</td><td>${t.strategy} · ${t.tf}</td><td><span class="chip ${t.side === 'BUY' ? 'green' : 'red'}">${t.side}</span></td><td>${t.exit}</td><td>${esc(BT.accName(t.account))}</td><td>${BT.ticketChip(t.decision)}</td><td class="r">${Math.round(t.pWin * 100)}%</td><td class="r num muted">${t.planR > 0 ? '+' : ''}${t.planR.toFixed(2)}R</td><td class="r num ${sgn(t.R)}">${t.R > 0 ? '+' : ''}${t.R.toFixed(2)}</td><td class="r num ${sgn(t.pnl)}">${t.taken ? inr(t.pnl) : '<span class="muted">would be ' + inr(t.pnl) + '</span>'}</td><td class="small">${(t.tags || []).map((g) => `<span class="chip ${g.startsWith('Normal') ? '' : 'amber'}">${esc(g)}</span>`).join(' ')}</td></tr>`).join('')}</tbody></table></div>
        <div class="grid-foot"><span class="small muted">${f.length} trades · graded on actual fills (R plan = at plan prices) · losses tagged automatically</span></div></div></div>`;
    }
  };
})();
