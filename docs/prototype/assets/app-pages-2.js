/* xDrishti prototype — pages: Instruments, Baskets, Data & import, Reports, Strategy Lab, Accounts, Services, Settings, Audit */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const { $, $$, esc, inr, num, pct, sgn, fmtDate, fmtDT, fmtTime, ago } = BT;
  const sum = (a, f) => a.reduce((s, x) => s + (f ? f(x) : x), 0);
  const SECTORS = [...new Set(D.INSTRUMENTS.map((i) => i.sector))].sort();
  const TFS = ['1m', '5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M'];

  function trackedChip(sym) {
    const t = BT.S.tracked[sym];
    if (!t) return '<span class="chip">not tracked</span>';
    if (t.status === 'backfilling') return `<span class="chip amber">backfilling ${Math.round(t.progress)}%</span>`;
    return '<span class="chip green">tracked</span>';
  }

  /* ---------- bars for an instrument at any timeframe (CSV import if present, else demo 1-minute data) ---------- */
  function barsFor(sym, tf) {
    const imported = BT.mem.imported[sym];
    if (['1D', '1W', '1M'].includes(tf)) {
      if (imported) return { bars: D.aggregate(imported, tf), source: 'CSV 1-minute → ' + tf };
      const daily = D.daily(sym).map((d) => { const [y, m, dd] = d.date.split('-').map(Number); return { ...d, t: new Date(y, m - 1, dd, 9, 15).getTime() }; });
      return { bars: tf === '1D' ? daily.slice(-180) : D.aggregate(daily, tf).slice(-120), source: tf === '1D' ? 'official daily' : 'aggregated from daily' };
    }
    const m1 = imported || D.DAYS.slice(-3).flatMap((d) => D.intraday1m(sym, d));
    return { bars: D.aggregate(m1, tf), source: (imported ? 'CSV' : 'Dhan') + ' 1-minute → ' + tf + ' (09:15-anchored)', n1m: m1.length };
  }
  BT.barsFor = barsFor;
  BT.openInstrument = (sym) => instrumentDrawer(sym);
  function instrumentDrawer(sym) {
    const inst = BT.inst(sym);
    let tf = '15m';
    BT.drawer({
      title: `${esc(sym)} · ${esc(inst.name)}`,
      body: `<div class="row">${trackedChip(sym)}<span class="chip">${esc(inst.sector)}</span>${inst.fno ? '<span class="chip blue">F&O</span>' : ''}<span class="chip">Dhan id ${inst.securityId}</span>${BT.basketsOf(sym).map((b) => `<span class="chip purple">${esc(b)}</span>`).join('')}</div>
        <div class="row"><div class="seg" id="id-tf">${TFS.map((t) => `<button data-tf="${t}" class="${t === tf ? 'on' : ''}">${t}</button>`).join('')}</div><span class="small muted" id="id-src"></span></div>
        <div class="card"><div class="card-b"><div class="chart lg" id="id-chart"></div></div></div>
        <dl class="kv"><dt>LTP</dt><dd>${num(BT.ltp(sym))} (${pct(BT.dayChange(sym), 2)})</dd><dt>Data coverage</dt><dd>${BT.S.tracked[sym] && BT.S.tracked[sym].dataFrom ? BT.S.tracked[sym].dataFrom + ' → ' + BT.S.tracked[sym].dataTo : '—'}${BT.mem.imported[sym] ? ' · CSV import present' : ''}</dd>
        <dt>Aggregation</dt><dd>All timeframes are built from 1-minute bars: open = first, high = max, low = min, close = last, volume = sum; intraday buckets anchored at 09:15.</dd></dl>`,
      onMount: (ov, close, charts) => {
        const draw = () => {
          charts.forEach((c) => c.dispose()); charts.length = 0;
          const box = $('#id-chart', ov); box.innerHTML = '';
          const r = barsFor(sym, tf);
          $('#id-src', ov).textContent = `${r.bars.length} bars · ${r.source}`;
          BT.candles(box, r.bars, {}, charts);
        };
        $$('#id-tf button', ov).forEach((b) => (b.onclick = () => { tf = b.dataset.tf; $$('#id-tf button', ov).forEach((x) => x.classList.toggle('on', x === b)); draw(); }));
        draw();
      },
    });
  }
  BT.instrumentDrawer = instrumentDrawer;
  function basketPicker(symbols, done) {
    const S = BT.S;
    const manual = S.baskets.filter((b) => b.type === 'manual');
    BT.modal({
      title: `Add ${symbols.length === 1 ? symbols[0] : symbols.length + ' instruments'} to baskets`,
      body: `<div class="grid">${manual.map((b) => `<label class="check"><input type="checkbox" value="${b.id}" ${symbols.every((s) => b.members.includes(s)) ? 'checked' : ''}> <span class="dot" style="background:${b.color}"></span> ${esc(b.name)} <span class="small muted">(${b.members.length})</span></label>`).join('')}</div>
        <div class="small muted">Untracked instruments are tracked automatically (history backfill runs on the service). Rule-based baskets update themselves.</div>`,
      foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="bp-ok">Save</button>',
      onMount: (ov, close) => {
        $('#bp-ok', ov).onclick = () => {
          const chosen = $$('input:checked', ov).map((x) => x.value);
          for (const b of manual) {
            for (const s of symbols) {
              const has = b.members.includes(s);
              if (chosen.includes(b.id) && !has) { b.members.push(s); BT.audit('owner', 'Basket member added', `${s} → ${b.name}`); }
              if (!chosen.includes(b.id) && has && symbols.length === 1) { b.members = b.members.filter((m) => m !== s); BT.audit('owner', 'Basket member removed', `${s} ✕ ${b.name}`); }
            }
          }
          if (chosen.length) symbols.forEach((s) => BT.track(s));
          BT.save(); close(); BT.rerender(); BT.toast('Baskets updated', 'good'); done && done();
        };
      },
    });
  }

  BT.basketPicker = basketPicker;
  /* ================= INSTRUMENTS ================= */
  const ins = { q: '', sector: '', fno: false, tracked: 'all', page: 0, sel: new Set() };
  BT.pages.instruments = function (el) {
    const S = BT.S;
    const all = D.ALL.filter((i) => (!ins.q || (i.symbol + ' ' + i.name).toLowerCase().includes(ins.q.toLowerCase())) && (!ins.sector || i.sector === ins.sector) && (!ins.fno || i.fno) &&
      (ins.tracked === 'all' || (ins.tracked === 'yes' ? S.tracked[i.symbol] : !S.tracked[i.symbol])));
    const rows = all; // one scrollable grid (virtualised in the real React UI)
    const nTracked = Object.keys(S.tracked).length;
    el.innerHTML = `<div class="page-head"><div><h1>Instruments</h1><p>Master list from Dhan (${D.ALL.length} in this demo) · ${nTracked} tracked — only tracked instruments get data, scans and analytics</p></div>
      <div class="row"><button class="btn" id="in-track" ${ins.sel.size ? '' : 'disabled'}>Track selected (${ins.sel.size})</button><button class="btn primary" id="in-basket" ${ins.sel.size ? '' : 'disabled'}>Add selected to basket…</button></div></div>
      <div class="card" style="margin-bottom:12px"><div class="card-b row">
        <input class="input" id="in-q" placeholder="Search symbol or name" style="width:240px" value="${esc(ins.q)}">
        <select class="input" id="in-sector" style="width:auto"><option value="">All sectors</option>${SECTORS.concat(['Index']).map((s) => `<option ${ins.sector === s ? 'selected' : ''}>${s}</option>`).join('')}</select>
        <div class="seg" id="in-tr">${[['all', 'All'], ['yes', 'Tracked'], ['no', 'Not tracked']].map(([k, l]) => `<button data-t="${k}" class="${ins.tracked === k ? 'on' : ''}">${l}</button>`).join('')}</div>
        <label class="check"><input type="checkbox" id="in-fno" ${ins.fno ? 'checked' : ''}> F&O only</label>
        <span class="spacer"></span><span class="small muted">${all.length} match</span></div></div>
      <div class="card" data-nocollapse="1"><div class="card-b flush"><div class="tbl-wrap grid-scroll sticky-col" id="in-grid"><table class="tbl"><thead><tr><th><input type="checkbox" id="in-all"></th><th>Symbol</th><th>Name</th><th>Sector</th><th>F&O</th><th class="r">LTP</th><th class="r">Day</th><th>Status</th><th>Baskets</th><th></th></tr></thead><tbody>
        ${rows.map((i) => `<tr><td><input type="checkbox" data-sel="${esc(i.symbol)}" ${ins.sel.has(i.symbol) ? 'checked' : ''}></td><td><a href="#" data-view="${esc(i.symbol)}"><b>${esc(i.symbol)}</b></a></td><td>${esc(i.name)}</td><td>${esc(i.sector)}</td><td>${i.fno ? '✓' : ''}</td>
          <td class="r num">${num(BT.ltp(i.symbol))}</td><td class="r num ${sgn(BT.dayChange(i.symbol))}">${pct(BT.dayChange(i.symbol), 2)}</td><td data-st="${esc(i.symbol)}">${trackedChip(i.symbol)}</td>
          <td>${BT.basketsOf(i.symbol).map((b) => `<span class="chip">${esc(b)}</span>`).join(' ')}</td>
          <td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap">${S.tracked[i.symbol] ? `<button class="btn sm" data-untrack="${esc(i.symbol)}">Untrack</button>` : `<button class="btn sm primary" data-track="${esc(i.symbol)}">Track</button>`}<button class="btn sm" data-bk="${esc(i.symbol)}">Baskets…</button></div></td></tr>`).join('')}
      </tbody></table></div>
      <div class="grid-foot"><span class="small muted">${rows.length} row(s) · scroll inside the grid — headers and the symbol column stay in place</span><span class="small muted">${ins.sel.size} selected</span></div></div></div>`;
    const grid = $('#in-grid'); grid.scrollTop = ins.scroll || 0; grid.onscroll = () => { ins.scroll = grid.scrollTop; };
    let tmr;
    $('#in-q').oninput = (e) => { clearTimeout(tmr); tmr = setTimeout(() => { ins.q = e.target.value; ins.page = 0; ins.scroll = 0; BT.rerender(); const q = $('#in-q'); q.focus(); q.setSelectionRange(q.value.length, q.value.length); }, 250); };
    $('#in-sector').onchange = (e) => { ins.sector = e.target.value; ins.page = 0; ins.scroll = 0; BT.rerender(); };
    $('#in-fno').onchange = (e) => { ins.fno = e.target.checked; ins.page = 0; ins.scroll = 0; BT.rerender(); };
    $$('#in-tr button').forEach((b) => (b.onclick = () => { ins.tracked = b.dataset.t; ins.page = 0; ins.scroll = 0; BT.rerender(); }));
    $('#in-all').onchange = (e) => { rows.forEach((r) => (e.target.checked ? ins.sel.add(r.symbol) : ins.sel.delete(r.symbol))); BT.rerender(); };
    $$('[data-sel]').forEach((c) => (c.onchange = () => { c.checked ? ins.sel.add(c.dataset.sel) : ins.sel.delete(c.dataset.sel); BT.rerender(); }));
    $$('[data-track]').forEach((b) => (b.onclick = () => { BT.track(b.dataset.track); BT.rerender(); BT.toast(`${b.dataset.track} tracked — backfill job running on the service`, 'good'); }));
    $$('[data-untrack]').forEach((b) => (b.onclick = async () => { if (await BT.confirmDlg('Untrack ' + b.dataset.untrack, 'Data collection and scans stop. Existing history is kept and is reused if you track it again.', 'Untrack')) { BT.untrack(b.dataset.untrack); BT.rerender(); } }));
    $$('[data-bk]').forEach((b) => (b.onclick = () => basketPicker([b.dataset.bk])));
    $$('[data-view]').forEach((a) => (a.onclick = (e) => { e.preventDefault(); instrumentDrawer(a.dataset.view); }));
    $('#in-track').onclick = () => { let n = 0; ins.sel.forEach((s) => { if (BT.track(s)) n++; }); ins.sel.clear(); BT.rerender(); BT.toast(`${n} instrument(s) tracked — backfills queued`, 'good'); };
    $('#in-basket').onclick = () => basketPicker([...ins.sel], () => ins.sel.clear());
    BT.onBackfill = () => $$('[data-st]').forEach((td) => (td.innerHTML = trackedChip(td.dataset.st)));
  };

  /* ================= BASKETS ================= */
  const bk = { sel: null };
  function tradesData() { if (!BT.mem.trades) BT.mem.trades = D.reportTrades(BT.S.baskets); return BT.mem.trades; }
  function stats(list) {
    const wins = list.filter((t) => t.R > 0), losses = list.filter((t) => t.R <= 0);
    const sw = sum(wins, (t) => t.R), sl = -sum(losses, (t) => t.R);
    return { n: list.length, win: list.length ? wins.length / list.length : 0, exp: list.length ? sum(list, (t) => t.R) / list.length : 0, total: sum(list, (t) => t.R),
      pf: sl ? sw / sl : null, avgW: wins.length ? sw / wins.length : 0, avgL: losses.length ? -sl / losses.length : 0, pnl: sum(list, (t) => t.pnl) };
  }
  BT.tradeStats = stats; BT.tradesData = tradesData;
  function eqReturn(members, days = 250) {
    const ms = members.filter((s) => D.BY_SYMBOL[s] && D.BY_SYMBOL[s].type === 'EQ');
    if (!ms.length) return null;
    const i0 = D.DAYS.length - 1 - days;
    return sum(ms, (s) => D.daily(s)[D.DAYS.length - 1].close / D.daily(s)[i0].close - 1) / ms.length;
  }
  BT.pages.baskets = function (el) {
    const S = BT.S;
    const all = S.baskets.concat(BT.systemBaskets());
    if (!bk.sel || !all.find((b) => b.id === bk.sel)) bk.sel = all[0].id;
    const b = all.find((x) => x.id === bk.sel);
    const st = stats(tradesData().filter((t) => t.basket === b.name && t.taken));
    const er = eqReturn(b.members), nifty = D.daily('NIFTY 50')[D.DAYS.length - 1].close / D.daily('NIFTY 50')[D.DAYS.length - 251].close - 1;
    const pinned = (S.settings.pinnedBaskets || []).includes(b.id);
    el.innerHTML = `<div class="page-head"><div><h1>Baskets</h1><p>Group tracked instruments. Baskets define what the nightly plan scans, Strategy Lab universes, report lenses and portfolio grouping.</p></div>
      <div class="row"><button class="btn primary" id="bk-new">New basket</button></div></div>
      <div class="basket-cards" style="margin-bottom:16px">${all.map((x) => `<div class="bcard ${x.id === bk.sel ? 'sel' : ''}" data-b="${x.id}" style="border-top-color:${x.color}"><h4>${esc(x.name)}</h4>
        <div class="row" style="gap:6px"><span class="chip">${x.type}</span><span class="chip ${x.purpose === 'trading' ? 'blue' : x.purpose === 'long_term' ? 'green' : ''}">${x.purpose.replace('_', '-')}</span><span class="small muted">${x.members.length} instruments</span></div></div>`).join('')}</div>
      <div class="grid g2">
        <div class="card"><div class="card-h"><h3><span class="dot" style="background:${b.color}"></span> ${esc(b.name)}</h3><div class="row">
          ${b.type !== 'system' ? `<button class="btn sm" id="bk-pin">${pinned ? 'Unpin from live' : 'Pin to live feed'}</button><button class="btn sm" id="bk-edit">Edit</button><button class="btn sm bad" id="bk-del">Delete</button>` : '<span class="small muted">maintained automatically</span>'}</div></div>
          <div class="card-b">
            <div class="small muted" style="margin-bottom:10px">${b.type === 'rule' ? 'Rule-based — re-evaluated nightly by the EOD service.' : b.type === 'manual' ? 'Manual — you add and remove instruments.' : 'System basket.'} ${b.styles ? 'Scanned for: ' + b.styles.join(', ') : ''}</div>
            ${b.type === 'manual' ? `<div class="row" style="margin-bottom:10px"><input class="input" id="bk-add" list="bk-syms" placeholder="Add instrument (symbol)…" style="flex:1"><datalist id="bk-syms">${D.INSTRUMENTS.filter((i) => !b.members.includes(i.symbol)).map((i) => `<option value="${i.symbol}">${esc(i.name)}</option>`).join('')}</datalist><button class="btn primary" id="bk-add-btn">Add</button></div>` : ''}
            ${b.type === 'rule' ? ruleEditor(b) : ''}
            <div class="tbl-wrap" style="max-height:420px"><table class="tbl"><thead><tr><th>Symbol</th><th>Sector</th><th class="r">LTP</th><th class="r">Day</th><th>Status</th><th></th></tr></thead><tbody>
              ${b.members.map((s) => `<tr><td><a href="#" data-view="${esc(s)}"><b>${esc(s)}</b></a></td><td>${esc(BT.inst(s).sector)}</td><td class="r num">${num(BT.ltp(s))}</td><td class="r num ${sgn(BT.dayChange(s))}">${pct(BT.dayChange(s), 2)}</td><td>${trackedChip(s)}</td>
                <td class="r">${b.type === 'manual' ? `<button class="btn sm ghost" data-rm="${esc(s)}" title="Remove">✕</button>` : ''}</td></tr>`).join('') || '<tr><td colspan="6" class="empty">No instruments yet.</td></tr>'}
            </tbody></table></div></div></div>
        <div class="grid">
          <div class="card"><div class="card-h"><h3>Basket performance (taken trades)</h3><a class="small" href="#/reports">Open in Reports →</a></div><div class="card-b">
            <div class="tiles" style="margin:0"><div class="tile"><div class="l">Trades</div><div class="v">${st.n}</div></div><div class="tile"><div class="l">Win rate</div><div class="v">${pct(st.win).replace('+', '')}</div></div>
            <div class="tile"><div class="l">Expectancy</div><div class="v ${sgn(st.exp)}">${st.exp.toFixed(2)}R</div></div><div class="tile"><div class="l">Total</div><div class="v ${sgn(st.total)}">${st.total.toFixed(1)}R</div></div></div></div></div>
          <div class="card"><div class="card-h"><h3>1-year buy-and-hold benchmark</h3></div><div class="card-b"><div class="chart sm" id="bk-bench"></div>
            <div class="small muted">Equal-weight basket ${pct(er)} vs Nifty 50 ${pct(nifty)} — judge trading results against simply holding the basket.</div></div></div>
        </div></div>`;
    $$('[data-b]').forEach((c) => (c.onclick = () => { bk.sel = c.dataset.b; BT.rerender(); }));
    $('#bk-new').onclick = () => basketDialog();
    $$('[data-view]').forEach((a) => (a.onclick = (e) => { e.preventDefault(); instrumentDrawer(a.dataset.view); }));
    if (b.type !== 'system') {
      $('#bk-edit').onclick = () => basketDialog(b);
      $('#bk-del').onclick = async () => { if (await BT.confirmDlg('Delete ' + b.name, 'The basket is removed from scan scopes, reports and portfolio grouping. Instruments stay tracked.', 'Delete', true)) { S.baskets = S.baskets.filter((x) => x.id !== b.id); BT.audit('owner', 'Basket deleted', b.name); BT.save(); bk.sel = null; BT.rerender(); } };
      $('#bk-pin').onclick = () => { const p = S.settings.pinnedBaskets || (S.settings.pinnedBaskets = []); if (pinned) S.settings.pinnedBaskets = p.filter((x) => x !== b.id); else p.push(b.id); BT.audit('owner', pinned ? 'Basket unpinned from live feed' : 'Basket pinned to live feed', b.name); BT.save(); BT.rerender(); BT.toast(pinned ? 'Unpinned' : `Pinned — ${b.members.length} instruments join the live active set`, 'good'); };
    }
    if (b.type === 'manual') {
      const add = () => { const s = $('#bk-add').value.trim().toUpperCase(); if (!D.BY_SYMBOL[s]) return BT.toast('Unknown symbol', 'bad'); if (!b.members.includes(s)) { b.members.push(s); BT.track(s); BT.audit('owner', 'Basket member added', `${s} → ${b.name}`); BT.save(); } BT.rerender(); BT.toast(`${s} added to ${b.name}`, 'good'); };
      $('#bk-add-btn').onclick = add; $('#bk-add').onkeydown = (e) => { if (e.key === 'Enter') add(); };
      $$('[data-rm]').forEach((x) => (x.onclick = () => { b.members = b.members.filter((m) => m !== x.dataset.rm); BT.audit('owner', 'Basket member removed', `${x.dataset.rm} ✕ ${b.name}`); BT.save(); BT.rerender(); }));
    }
    if (b.type === 'rule') bindRule(b);
    // benchmark chart
    const days = D.DAYS.slice(-250);
    const ms = b.members.filter((s) => D.BY_SYMBOL[s] && D.BY_SYMBOL[s].type === 'EQ');
    const idx = (s) => days.map((d) => D.closeOn(s, d) / D.closeOn(s, days[0]) * 100);
    const basketLine = ms.length ? days.map((d, i) => +(sum(ms, (s) => D.closeOn(s, d) / D.closeOn(s, days[0]) * 100) / ms.length).toFixed(2)) : [];
    BT.echart($('#bk-bench'), { tooltip: { trigger: 'axis' }, legend: { top: 0 }, grid: { left: 40, right: 10, top: 30, bottom: 24 }, xAxis: { type: 'category', data: days.map((d) => d.slice(5)) }, yAxis: { type: 'value', scale: true },
      series: [{ name: b.name + ' (equal weight)', type: 'line', showSymbol: false, data: basketLine }, { name: 'Nifty 50', type: 'line', showSymbol: false, data: idx('NIFTY 50').map((v) => +v.toFixed(2)), lineStyle: { type: 'dashed' } }] });
    BT.onBackfill = () => BT.rerender();
  };
  function ruleEditor(b) {
    const r = b.rule;
    return `<div class="card" style="margin-bottom:10px;box-shadow:none"><div class="card-b grid" style="gap:10px">
      <div class="small"><b>Rule</b> — members are recomputed from the master list</div>
      <div class="row">${SECTORS.map((s) => `<label class="check small"><input type="checkbox" data-rs="${s}" ${r.sectors.includes(s) ? 'checked' : ''}>${s}</label>`).join('')}</div>
      <div class="row"><label class="check"><input type="checkbox" id="r-fno" ${r.fnoOnly ? 'checked' : ''}> F&O only</label><label class="check"><input type="checkbox" id="r-tr" ${r.trackedOnly ? 'checked' : ''}> Tracked only</label>
        <label class="field" style="flex-direction:row;align-items:center">Min price <input class="input num" id="r-min" type="number" style="width:100px" value="${r.minPrice || 0}"></label>
        <span class="spacer"></span><span class="chip blue" id="r-prev">${BT.ruleMembers(r).length} match</span><button class="btn sm primary" id="r-save">Save rule</button></div></div></div>`;
  }
  function bindRule(b) {
    const read = () => ({ sectors: $$('[data-rs]').filter((x) => x.checked).map((x) => x.dataset.rs), fnoOnly: $('#r-fno').checked, trackedOnly: $('#r-tr').checked, minPrice: parseFloat($('#r-min').value) || 0 });
    const prev = () => ($('#r-prev').textContent = BT.ruleMembers(read()).length + ' match');
    $$('[data-rs], #r-fno, #r-tr, #r-min').forEach((x) => (x.oninput = x.onchange = prev));
    $('#r-save').onclick = () => { b.rule = read(); b.members = BT.ruleMembers(b.rule); BT.audit('owner', 'Basket rule updated', `${b.name}: ${b.members.length} members`); BT.save(); BT.rerender(); BT.toast('Rule saved — re-evaluated nightly by the EOD service', 'good'); };
  }
  function basketDialog(b) {
    const S = BT.S;
    const isNew = !b;
    b = b || { name: '', type: 'manual', purpose: 'trading', color: '#6366f1', styles: ['intraday', 'swing'], members: [], rule: { sectors: [], fnoOnly: true, minPrice: 100, trackedOnly: true } };
    BT.modal({
      title: isNew ? 'New basket' : 'Edit ' + b.name,
      body: `<label class="field">Name<input class="input" id="bd-name" value="${esc(b.name)}" placeholder="e.g. MyChoice"></label>
        <div class="grid g2"><label class="field">Type<select class="input" id="bd-type" ${isNew ? '' : 'disabled'}><option value="manual" ${b.type === 'manual' ? 'selected' : ''}>Manual</option><option value="rule" ${b.type === 'rule' ? 'selected' : ''}>Rule-based</option></select></label>
        <label class="field">Purpose<select class="input" id="bd-purpose">${[['trading', 'Trading (scanned for the plan)'], ['long_term', 'Long-term (holdings)'], ['watch', 'Watch only']].map(([k, l]) => `<option value="${k}" ${b.purpose === k ? 'selected' : ''}>${l}</option>`).join('')}</select></label></div>
        <div class="row"><span class="small muted">Scan for:</span><label class="check"><input type="checkbox" id="bd-intra" ${b.styles.includes('intraday') ? 'checked' : ''}> Intraday</label><label class="check"><input type="checkbox" id="bd-swing" ${b.styles.includes('swing') ? 'checked' : ''}> Swing</label>
          <span class="spacer"></span><label class="field" style="flex-direction:row;align-items:center;gap:8px">Colour <input type="color" id="bd-color" value="${b.color}"></label></div><div class="err" id="bd-err"></div>`,
      foot: `<button class="btn" data-close>Cancel</button><button class="btn primary" id="bd-ok">${isNew ? 'Create' : 'Save'}</button>`,
      onMount: (ov, close) => {
        $('#bd-ok', ov).onclick = () => {
          const name = $('#bd-name', ov).value.trim();
          if (!name) return ($('#bd-err', ov).textContent = 'Name is required.');
          if (S.baskets.some((x) => x.name.toLowerCase() === name.toLowerCase() && x !== b)) return ($('#bd-err', ov).textContent = 'A basket with this name exists.');
          b.name = name; b.purpose = $('#bd-purpose', ov).value; b.color = $('#bd-color', ov).value;
          b.styles = [$('#bd-intra', ov).checked && 'intraday', $('#bd-swing', ov).checked && 'swing'].filter(Boolean);
          if (isNew) { b.type = $('#bd-type', ov).value; b.id = BT.uid('b-'); b.createdAt = Date.now(); if (b.type === 'rule') b.members = BT.ruleMembers(b.rule); S.baskets.push(b); bk.sel = b.id; }
          BT.audit('owner', isNew ? 'Basket created' : 'Basket updated', `${b.name} (${b.type}, ${b.purpose})`);
          BT.save(); close(); BT.rerender(); BT.toast(isNew ? 'Basket created — add instruments to it' : 'Basket saved', 'good');
        };
      },
    });
  }

  /* ================= DATA & IMPORT ================= */
  const imp = { text: null, file: null, parsed: null, profile: { delimiter: 'auto', symbolFrom: 'filename', label: 'start', tz: 'Asia/Kolkata' }, tf: '15m', viewSym: null };
  function parseTs(s) {
    s = s.trim();
    if (/^\d{10,13}$/.test(s)) { const n = +s; return n < 1e12 ? n * 1000 : n; }
    let m = s.match(/^(\d{4})-(\d{2})-(\d{2})[ T](\d{1,2}):(\d{2})(?::(\d{2}))?/);
    if (m) return new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0)).getTime();
    m = s.match(/^(\d{2})[-/](\d{2})[-/](\d{4})[ T](\d{1,2}):(\d{2})(?::(\d{2}))?/);
    if (m) return new Date(+m[3], +m[2] - 1, +m[1], +m[4], +m[5], +(m[6] || 0)).getTime();
    return NaN;
  }
  function parseCsv(text, fileName, profile) {
    const lines = text.replace(/\r/g, '').split('\n').filter((l) => l.trim());
    const delim = profile.delimiter !== 'auto' ? profile.delimiter : [',', ';', '\t', '|'].sort((a, b) => lines[0].split(b).length - lines[0].split(a).length)[0];
    const head = lines[0].split(delim).map((h) => h.trim().toLowerCase().replace(/"/g, ''));
    const find = (...names) => head.findIndex((h) => names.includes(h));
    const ci = { ts: find('datetime', 'timestamp', 'time_stamp', 'date_time'), date: find('date'), time: find('time'), o: find('open', 'o'), h: find('high', 'h'), l: find('low', 'l'), c: find('close', 'c', 'ltp'), v: find('volume', 'vol', 'v'), sym: find('symbol', 'ticker', 'scrip', 'tradingsymbol') };
    const errors = [];
    if (ci.ts < 0 && !(ci.date >= 0 && ci.time >= 0)) errors.push('No timestamp column (expected datetime/timestamp, or date + time).');
    for (const k of ['o', 'h', 'l', 'c']) if (ci[k] < 0) errors.push(`Missing column: ${{ o: 'open', h: 'high', l: 'low', c: 'close' }[k]}`);
    if (errors.length) return { errors, head, delim };
    const fileSym = (fileName.match(/^([A-Za-z0-9&-]+?)(?:[_\-. ](?:1m|1min|1minute|minute|nse|eq)|\.csv)/i) || [])[1];
    const bySym = {};
    const rejected = [];
    const seen = new Set();
    for (let i = 1; i < lines.length; i++) {
      const c = lines[i].split(delim).map((x) => x.trim().replace(/"/g, ''));
      const sym = (ci.sym >= 0 && profile.symbolFrom === 'column' ? c[ci.sym] : fileSym || (ci.sym >= 0 ? c[ci.sym] : '')).toUpperCase();
      let t = ci.ts >= 0 ? parseTs(c[ci.ts]) : parseTs(c[ci.date] + ' ' + c[ci.time]);
      const o = +c[ci.o], h = +c[ci.h], l = +c[ci.l], cl = +c[ci.c], v = ci.v >= 0 ? +c[ci.v] : 0;
      const bad = (r) => rejected.push({ line: i + 1, reason: r, raw: lines[i].slice(0, 80) });
      if (!sym) { bad('No symbol (file name or symbol column)'); continue; }
      if (!D.BY_SYMBOL[sym]) { bad(`Unknown symbol ${sym} — map it to the master list`); continue; }
      if (!isFinite(t)) { bad('Unparseable timestamp'); continue; }
      if (profile.label === 'end') t -= 60000;
      const d = new Date(t); const mins = d.getHours() * 60 + d.getMinutes();
      if (mins < 555 || mins > 929) { bad('Outside session 09:15–15:29 (check bar start/end label or timezone)'); continue; }
      if (![o, h, l, cl].every((x) => isFinite(x) && x > 0)) { bad('Non-numeric or non-positive price'); continue; }
      if (l > Math.min(o, cl) || h < Math.max(o, cl)) { bad('OHLC inconsistent (low/high do not contain open/close)'); continue; }
      if (v < 0) { bad('Negative volume'); continue; }
      const key = sym + t; if (seen.has(key)) { bad('Duplicate timestamp'); continue; } seen.add(key);
      (bySym[sym] = bySym[sym] || []).push({ t, open: o, high: h, low: l, close: cl, volume: v });
    }
    Object.values(bySym).forEach((a) => a.sort((x, y) => x.t - y.t));
    return { errors: [], head, delim, bySym, rejected, total: lines.length - 1, fileSym };
  }
  function sampleCsv(sym) {
    const rows = D.DAYS.slice(-3).flatMap((d) => D.intraday1m(sym, d));
    const p = (n) => String(n).padStart(2, '0');
    return 'datetime,open,high,low,close,volume\n' + rows.map((b) => { const d = new Date(b.t); return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:00,${b.open},${b.high},${b.low},${b.close},${b.volume}`; }).join('\n')
      + `\n${D.DAYS[D.DAYS.length - 1]} 08:59:00,1,1,1,1,1\n${D.DAYS[D.DAYS.length - 1]} 10:00:00,100,90,95,96,10`;
  }
  BT.pages.data = function (el) {
    const S = BT.S;
    const p = imp.parsed;
    const syms = p && p.bySym ? Object.keys(p.bySym) : [];
    const reasons = p && p.rejected ? Object.entries(p.rejected.reduce((m, r) => { m[r.reason] = (m[r.reason] || 0) + 1; return m; }, {})) : [];
    const importedSyms = Object.keys(BT.mem.imported);
    if (!imp.viewSym || !BT.mem.imported[imp.viewSym]) imp.viewSym = importedSyms[0] || null;
    el.innerHTML = `<div class="page-head"><div><h1>Data & import</h1><p>Import your 1-minute CSV files; every timeframe is aggregated from 1-minute bars. End-of-day Dhan fetch keeps tracked instruments complete.</p></div>
      <div class="row"><button class="btn" id="dt-eod">Run EOD fetch now</button></div></div>
      <div class="grid g2">
        <div class="card"><div class="card-h"><h3>1 · Choose CSV file</h3><div class="row"><select class="input" id="dt-ssym" style="width:auto">${['TATASTEEL', 'INFY', 'SBIN', 'PERSISTENT', 'TATAPOWER'].map((s) => `<option>${s}</option>`).join('')}</select><button class="btn sm" id="dt-sample">Load sample</button><button class="btn sm" id="dt-dl">Download sample</button></div></div>
          <div class="card-b"><div class="dropzone" id="dt-drop">Drop a CSV file here or <b>click to choose</b><br><span class="small">One instrument per file (symbol from file name, e.g. <span class="mono">TATASTEEL_1min.csv</span>) or a <span class="mono">symbol</span> column</span><input type="file" id="dt-file" accept=".csv,.txt" hidden></div>
          <div class="grid g3" style="margin-top:12px"><label class="field">Delimiter<select class="input" id="pf-delim">${[['auto', 'Auto-detect'], [',', 'Comma'], [';', 'Semicolon'], ['\t', 'Tab']].map(([v, l]) => `<option value="${v === '\t' ? 'tab' : v}" ${imp.profile.delimiter === v ? 'selected' : ''}>${l}</option>`).join('')}</select></label>
            <label class="field">Timestamp marks<select class="input" id="pf-label"><option value="start" ${imp.profile.label === 'start' ? 'selected' : ''}>Bar start</option><option value="end" ${imp.profile.label === 'end' ? 'selected' : ''}>Bar end</option></select></label>
            <label class="field">Symbol from<select class="input" id="pf-sym"><option value="filename" ${imp.profile.symbolFrom === 'filename' ? 'selected' : ''}>File name</option><option value="column" ${imp.profile.symbolFrom === 'column' ? 'selected' : ''}>Symbol column</option></select></label></div>
          <div class="small muted" style="margin-top:8px">Timezone Asia/Kolkata · session filter 09:15–15:29 · raw prices (system applies corporate actions)</div></div></div>
        <div class="card"><div class="card-h"><h3>2 · Preview & validate</h3>${p && !p.errors.length ? `<button class="btn primary sm" id="dt-commit" ${syms.length ? '' : 'disabled'}>Commit import</button>` : ''}</div><div class="card-b">
          ${!p ? '<div class="empty">Choose a file or load the sample to preview it here.</div>' : p.errors.length ? `<div class="banner warn">${p.errors.map(esc).join('<br>')}<br>Columns found: ${p.head.map(esc).join(', ')}</div>` : `
            <div class="row" style="margin-bottom:10px"><span class="chip">${esc(imp.file)}</span><span class="chip green">${sum(syms, (s) => p.bySym[s].length)} valid rows</span><span class="chip ${p.rejected.length ? 'red' : ''}">${p.rejected.length} rejected</span><span class="chip blue">${syms.map(esc).join(', ') || 'no symbol'}</span></div>
            ${reasons.length ? `<div class="small" style="margin-bottom:8px">${reasons.map(([r, n]) => `<div class="neg">✗ ${n} × ${esc(r)}</div>`).join('')}</div>` : ''}
            <div class="tbl-wrap" style="max-height:230px"><table class="tbl"><thead><tr><th>Bar start</th><th class="r">Open</th><th class="r">High</th><th class="r">Low</th><th class="r">Close</th><th class="r">Volume</th></tr></thead><tbody>
            ${syms.length ? p.bySym[syms[0]].slice(0, 8).map((b) => `<tr><td>${fmtDT(b.t)}</td><td class="r num">${b.open}</td><td class="r num">${b.high}</td><td class="r num">${b.low}</td><td class="r num">${b.close}</td><td class="r num">${b.volume}</td></tr>`).join('') : ''}</tbody></table></div>`}
        </div></div></div>
      <div class="card" style="margin-top:14px"><div class="card-h"><h3>3 · Aggregated timeframes</h3><div class="row">${importedSyms.length ? `<select class="input" id="dt-vsym" style="width:auto">${importedSyms.map((s) => `<option ${s === imp.viewSym ? 'selected' : ''}>${s}</option>`).join('')}</select>` : ''}<div class="seg" id="dt-tf">${TFS.filter((t) => t !== '1W' && t !== '1M').map((t) => `<button data-tf="${t}" class="${imp.tf === t ? 'on' : ''}">${t}</button>`).join('')}</div></div></div>
        <div class="card-b">${imp.viewSym ? `<div class="grid g2"><div class="chart lg" id="dt-chart"></div><div class="tbl-wrap" style="max-height:380px" id="dt-agg"></div></div>` : '<div class="empty">Commit an import to see it aggregated into 5m, 10m, 15m, 30m, 1h and 1D bars.</div>'}</div></div>
      <div class="grid g2" style="margin-top:14px">
        <div class="card"><div class="card-h"><h3>Import history</h3></div><div class="card-b flush"><table class="tbl"><thead><tr><th>When</th><th>File</th><th>Symbols</th><th class="r">Rows</th><th class="r">Rejected</th><th></th></tr></thead><tbody>
          ${S.imports.map((j) => `<tr><td>${fmtDT(j.at)}</td><td>${esc(j.file)}</td><td>${j.symbols.map(esc).join(', ')}</td><td class="r num">${j.rows}</td><td class="r num">${j.rejected}</td><td class="r">${j.undone ? '<span class="chip">undone</span>' : BT.mem.imported[j.symbols[0]] ? `<button class="btn sm" data-undo="${j.id}">Undo</button>` : '<span class="small muted">session data cleared</span>'}</td></tr>`).join('') || '<tr><td colspan="6" class="empty">No imports yet.</td></tr>'}</tbody></table></div></div>
        <div class="card"><div class="card-h"><h3>Coverage of tracked instruments</h3></div><div class="card-b flush"><div class="tbl-wrap" style="max-height:300px"><table class="tbl"><thead><tr><th>Symbol</th><th>Status</th><th>From</th><th>To</th><th>Source</th></tr></thead><tbody>
          ${Object.entries(S.tracked).map(([s, t]) => `<tr><td><b>${esc(s)}</b></td><td>${trackedChip(s)}</td><td>${t.dataFrom || '—'}</td><td>${t.dataTo || '—'}</td><td>${BT.mem.imported[s] ? 'CSV + Dhan' : 'Dhan'}</td></tr>`).join('')}</tbody></table></div></div></div></div>`;
    const loadText = (text, name) => { imp.text = text; imp.file = name; imp.parsed = parseCsv(text, name, imp.profile); BT.rerender(); };
    const drop = $('#dt-drop'), file = $('#dt-file');
    drop.onclick = () => file.click();
    file.onchange = () => { const f = file.files[0]; if (f) f.text().then((t) => loadText(t, f.name)); };
    drop.ondragover = (e) => { e.preventDefault(); drop.classList.add('over'); };
    drop.ondragleave = () => drop.classList.remove('over');
    drop.ondrop = (e) => { e.preventDefault(); drop.classList.remove('over'); const f = e.dataTransfer.files[0]; if (f) f.text().then((t) => loadText(t, f.name)); };
    $('#dt-sample').onclick = () => { const s = $('#dt-ssym').value; loadText(sampleCsv(s), `${s}_1min.csv`); };
    $('#dt-dl').onclick = () => { const s = $('#dt-ssym').value; const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([sampleCsv(s)], { type: 'text/csv' })); a.download = `${s}_1min.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000); };
    const reparse = () => { imp.profile = { ...imp.profile, delimiter: $('#pf-delim').value === 'tab' ? '\t' : $('#pf-delim').value, label: $('#pf-label').value, symbolFrom: $('#pf-sym').value }; if (imp.text) imp.parsed = parseCsv(imp.text, imp.file, imp.profile); BT.rerender(); };
    ['#pf-delim', '#pf-label', '#pf-sym'].forEach((s) => ($(s).onchange = reparse));
    $('#dt-eod').onclick = () => { BT.runService('eod-fetch'); BT.toast('EOD fetch started on the service'); };
    if ($('#dt-commit')) $('#dt-commit').onclick = () => {
      const job = { id: BT.uid('IMP'), at: Date.now(), file: imp.file, symbols: syms, rows: sum(syms, (s) => p.bySym[s].length), rejected: p.rejected.length };
      for (const s of syms) {
        const prev = BT.mem.imported[s] || [];
        const map = new Map(prev.map((b) => [b.t, b]));
        p.bySym[s].forEach((b) => map.set(b.t, b));
        BT.mem.imported[s] = [...map.values()].sort((a, b) => a.t - b.t);
        if (!S.tracked[s]) S.tracked[s] = { since: D.iso(new Date()), status: 'ready', progress: 100 };
        S.tracked[s].dataFrom = S.tracked[s].dataFrom || D.iso(new Date(BT.mem.imported[s][0].t)); S.tracked[s].dataTo = D.iso(new Date(BT.mem.imported[s][BT.mem.imported[s].length - 1].t));
      }
      S.imports.unshift(job);
      BT.audit('owner', 'CSV import committed', `${job.file}: ${job.rows} rows, ${job.rejected} rejected → aggregates refreshed`);
      BT.save(); imp.viewSym = syms[0]; imp.parsed = null; imp.text = null; BT.rerender();
      BT.toast(`Imported ${job.rows} rows · ${syms.join(', ')} auto-tracked · all timeframes rebuilt`, 'good');
    };
    $$('[data-undo]').forEach((b) => (b.onclick = () => { const j = S.imports.find((x) => x.id === b.dataset.undo); j.symbols.forEach((s) => delete BT.mem.imported[s]); j.undone = true; BT.audit('owner', 'CSV import undone', j.file); BT.save(); BT.rerender(); }));
    if ($('#dt-vsym')) $('#dt-vsym').onchange = (e) => { imp.viewSym = e.target.value; BT.rerender(); };
    $$('#dt-tf button').forEach((b) => (b.onclick = () => { imp.tf = b.dataset.tf; BT.rerender(); }));
    if (imp.viewSym) {
      const bars = D.aggregate(BT.mem.imported[imp.viewSym], imp.tf);
      const per = imp.tf === '1D' ? 375 : imp.tf === '1m' ? 1 : parseInt(imp.tf, 10) * (imp.tf.endsWith('h') ? 60 : 1);
      BT.candles($('#dt-chart'), bars, { daily: false });
      $('#dt-agg').innerHTML = `<table class="tbl"><thead><tr><th>${imp.tf} bar</th><th class="r">O</th><th class="r">H</th><th class="r">L</th><th class="r">C</th><th class="r">Vol</th><th class="r">Complete</th></tr></thead><tbody>${bars.slice(-60).reverse().map((b) => `<tr><td>${imp.tf === '1D' ? fmtDate(b.t) : fmtDT(b.t)}</td><td class="r num">${num(b.open)}</td><td class="r num">${num(b.high)}</td><td class="r num">${num(b.low)}</td><td class="r num">${num(b.close)}</td><td class="r num">${b.volume.toLocaleString('en-IN')}</td><td class="r">${(b.n ?? per) >= per ? '<span class="chip green">100%</span>' : `<span class="chip amber" title="partial bucket">${Math.round(b.n / per * 100)}%</span>`}</td></tr>`).join('')}</tbody></table>`;
    }
    BT.onBackfill = null;
  };

  /* ================= STRATEGY LAB ================= */
  const DEFAULT_YAML = `strategy:
  id: VWAP_RSI_TREND
  version: 1
  style: intraday
  timeframes: [15m]
  sides: [BUY, SELL]
  params: { rsi_len: 14, rsi_min: 55, swing_lookback: 5 }
  setup:
    all:
      - time >= "09:45"
      - tf("1h").ema(20) > tf("1h").ema(50)
      - rsi(rsi_len) > rsi_min
  trigger:
    all:
      - crosses_above(close, vwap)
  entry: { type: at_close }
  stop:  { type: swing_low, lookback: swing_lookback, buffer_atr: 0.1 }
  exits:
    - { policy: FIXED_RR, rr: [2.0] }
    - { policy: PARTIAL_TRAIL, book_pct: 50, t1_rr: 1.0 }
  time_rules: { last_entry: "14:30", square_off: "15:15" }`;
  const lab = { yaml: null, basket: 'MyChoice', from: '2024-01-01', running: false, result: null, msgs: null };
  function validateYaml(y) {
    const errs = [], warns = [];
    for (const k of ['id:', 'timeframes:', 'sides:', 'setup:', 'trigger:', 'stop:', 'exits:']) if (!y.includes(k)) errs.push(`Missing "${k.replace(':', '')}"`);
    const tfm = y.match(/timeframes:\s*\[([^\]]*)\]/);
    if (tfm) tfm[1].split(',').map((s) => s.trim()).filter(Boolean).forEach((t) => { if (!['5m', '10m', '15m', '30m', '1h', '1D'].includes(t)) errs.push(`Unknown timeframe "${t}"`); });
    if (/\[\s*-\d+\s*\]|next_|tomorrow|future|shift\(\s*-/.test(y)) errs.push('Look-ahead detected: expressions may only use closed bars (e.g. close[1], not close[-1] / next_*)');
    const pm = y.match(/params:\s*\{([^}]*)\}/);
    if (pm && pm[1].split(',').filter((s) => s.includes(':')).length > 4) errs.push('More than 4 tunable params — reduce to limit overfitting');
    if (!/square_off|max_hold/.test(y) && /style:\s*intraday/.test(y)) warns.push('No square_off given — default 15:15 will be used');
    return { errs, warns };
  }
  BT.pages.lab = function (el) {
    const S = BT.S;
    lab.yaml = lab.yaml ?? DEFAULT_YAML;
    const r = lab.result;
    el.innerHTML = `<div class="page-head"><div><h1>Strategy Lab</h1><p>Write rules, choose a basket as universe, run an honest walk-forward test. Tests run as jobs on the worker service.</p></div></div>
      <div class="grid g2">
        <div class="card"><div class="card-h"><h3>Rules (DSL)</h3><span class="small muted">closed bars only · ≤4 params</span></div><div class="card-b grid">
          <textarea class="input" id="lb-yaml" rows="22" spellcheck="false">${esc(lab.yaml)}</textarea>
          <div class="row"><label class="field" style="flex-direction:row;align-items:center;gap:6px">Universe <select class="input" id="lb-basket" style="width:auto">${S.baskets.map((b) => `<option ${lab.basket === b.name ? 'selected' : ''}>${esc(b.name)}</option>`).join('')}</select></label>
            <label class="field" style="flex-direction:row;align-items:center;gap:6px">From <input class="input" type="date" id="lb-from" value="${lab.from}" style="width:auto"></label></div>
          <div id="lb-msgs">${lab.msgs ? lab.msgs : ''}</div>
          <div class="row"><button class="btn" id="lb-validate">Validate</button><button class="btn" id="lb-quick" ${lab.running ? 'disabled' : ''}>Quick test (1y, 30 symbols)</button><button class="btn primary" id="lb-full" ${lab.running ? 'disabled' : ''}>Full walk-forward test</button></div>
          <div id="lb-prog" class="${lab.running ? '' : 'hidden'}"><div class="progress"><i id="lb-bar" style="width:0%"></i></div><div class="small muted" id="lb-step"></div></div></div></div>
        <div class="card"><div class="card-h"><h3>Result</h3>${r ? `<span class="chip ${r.verdict === 'PASS' ? 'green' : r.verdict === 'MARGINAL' ? 'amber' : 'red'}" style="font-size:14px">${r.verdict}</span>` : ''}</div><div class="card-b">
          ${!r ? '<div class="empty">Run a test to see the verdict, gates, equity curve and results by basket instrument.</div>' : `
          <div class="small muted" style="margin-bottom:8px">${esc(r.id)} · ${r.mode} · universe ${esc(r.basket)} · ${r.from} → today · ${fmtDT(r.at)}</div>
          <div class="tiles" style="margin:0 0 10px"><div class="tile"><div class="l">OOS trades</div><div class="v">${r.n}</div></div><div class="tile"><div class="l">Win rate</div><div class="v">${Math.round(r.win * 100)}%</div></div><div class="tile"><div class="l">Expectancy</div><div class="v ${sgn(r.exp)}">${r.exp.toFixed(2)}R</div></div><div class="tile"><div class="l">Profit factor</div><div class="v">${r.pf.toFixed(2)}</div></div></div>
          <div class="grid" style="gap:4px;margin-bottom:10px">${r.gates.map((g) => `<div class="small">${g.ok ? '<span class="pos">✓</span>' : '<span class="neg">✗</span>'} ${esc(g.label)}</div>`).join('')}</div>
          <div class="chart sm" id="lb-eq"></div>
          <div class="tbl-wrap" style="max-height:220px;margin-top:10px"><table class="tbl"><thead><tr><th>Instrument</th><th class="r">Trades</th><th class="r">Exp.</th></tr></thead><tbody>${r.bySym.map((x) => `<tr><td>${x.s}</td><td class="r num">${x.n}</td><td class="r num ${sgn(x.e)}">${x.e.toFixed(2)}R</td></tr>`).join('')}</tbody></table></div>
          ${r.warns.length ? `<div class="banner warn" style="margin-top:10px">${r.warns.map(esc).join('<br>')}</div>` : ''}
          <div class="row" style="margin-top:10px"><button class="btn good" id="lb-promote" ${r.verdict === 'PASS' && !r.promoted ? '' : 'disabled'}>${r.promoted ? 'Sent to paper stage' : 'Promote → Candidate → Paper'}</button><span class="small muted">The assistant can run tests but cannot promote.</span></div>`}
        </div></div></div>
      <div class="card" style="margin-top:14px"><div class="card-h"><h3>Test ledger (multiple-testing aware)</h3><span class="small muted">${S.labRuns.length} runs — pass thresholds tighten as variants grow</span></div><div class="card-b flush"><table class="tbl"><thead><tr><th>Run</th><th>When</th><th>Strategy</th><th>Universe</th><th>Mode</th><th class="r">Trades</th><th class="r">Exp.</th><th>Verdict</th></tr></thead><tbody>
        ${S.labRuns.map((x) => `<tr><td>${esc(x.id)}</td><td>${fmtDT(x.at)}</td><td>${esc(x.sid)}</td><td>${esc(x.basket)}</td><td>${x.mode}</td><td class="r num">${x.n}</td><td class="r num ${sgn(x.exp)}">${x.exp.toFixed(2)}R</td><td><span class="chip ${x.verdict === 'PASS' ? 'green' : x.verdict === 'MARGINAL' ? 'amber' : 'red'}">${x.verdict}</span></td></tr>`).join('') || '<tr><td colspan="8" class="empty">No runs yet.</td></tr>'}</tbody></table></div></div>`;
    const keep = () => { lab.yaml = $('#lb-yaml').value; lab.basket = $('#lb-basket').value; lab.from = $('#lb-from').value; };
    $('#lb-validate').onclick = () => { keep(); const v = validateYaml(lab.yaml); lab.msgs = v.errs.length ? `<div class="banner warn">${v.errs.map(esc).join('<br>')}</div>` : `<div class="banner good">Valid ✓ — schema, names and look-ahead checks passed${v.warns.length ? '<br>' + v.warns.map(esc).join('<br>') : ''}</div>`; BT.rerender(); };
    const run = (mode) => {
      keep();
      const v = validateYaml(lab.yaml);
      if (v.errs.length) { lab.msgs = `<div class="banner warn">${v.errs.map(esc).join('<br>')}</div>`; BT.rerender(); return; }
      lab.running = true; lab.msgs = ''; BT.rerender();
      const steps = mode === 'quick' ? ['Compiling rules to expression trees', 'Loading 1y of bars (30 symbols)', 'Simulating entries & exits with costs', 'Computing verdict'] : ['Compiling rules to expression trees', 'Loading bars for universe', 'Walk-forward fold 1/4', 'Walk-forward fold 2/4', 'Walk-forward fold 3/4', 'Walk-forward fold 4/4', 'Robustness: parameter sensitivity, Monte Carlo', 'Computing verdict'];
      let i = 0;
      const t = setInterval(() => {
        if (!$('#lb-bar')) { /* page changed: keep running silently */ }
        else { $('#lb-bar').style.width = Math.round(((i + 1) / steps.length) * 100) + '%'; $('#lb-step').textContent = steps[i]; }
        if (++i >= steps.length) { clearInterval(t); finishRun(mode, v.warns); }
      }, 550);
    };
    $('#lb-quick').onclick = () => run('quick');
    $('#lb-full').onclick = () => run('full');
    if (r && $('#lb-promote')) $('#lb-promote').onclick = () => { r.promoted = true; BT.audit('owner', 'Strategy promoted', `${r.sid} → Candidate → Paper stage`); BT.save(); BT.rerender(); BT.toast('Promoted — it must pass the paper stage before it can appear in plans', 'good'); };
    if (r) {
      let c = 0;
      BT.echart($('#lb-eq'), { tooltip: { trigger: 'axis' }, grid: { left: 36, right: 10, top: 10, bottom: 22 }, xAxis: { type: 'category', data: r.curve.map((_, i) => i + 1), show: false }, yAxis: { type: 'value' }, series: [{ type: 'line', showSymbol: false, areaStyle: { opacity: 0.12 }, data: r.curve.map((x) => +(c += x).toFixed(2)) }] });
    }
  };
  function finishRun(mode, warns) {
    const S = BT.S;
    const sid = (lab.yaml.match(/id:\s*([A-Za-z0-9_]+)/) || [])[1] || 'UNNAMED';
    const rr = D.rng('lab:' + lab.yaml + lab.basket + lab.from + mode);
    const basket = S.baskets.find((b) => b.name === lab.basket);
    const n = mode === 'quick' ? 40 + Math.floor(rr() * 60) : 120 + Math.floor(rr() * 260);
    const edge = (rr() - 0.35) * 0.5;
    const curve = Array.from({ length: n }, () => (rr() < 0.46 + edge * 0.4 ? +(0.8 + rr() * 1.6).toFixed(2) : -+(0.6 + rr() * 0.45).toFixed(2)));
    const wins = curve.filter((x) => x > 0), losses = curve.filter((x) => x <= 0);
    const exp = sum(curve) / n, pf = sum(wins) / -sum(losses), win = wins.length / n;
    const folds = mode === 'quick' ? 1 : 4, posFolds = mode === 'quick' ? (exp > 0 ? 1 : 0) : Math.max(0, Math.min(4, Math.round(2 + exp * 8 + (rr() - 0.5))));
    const adj = 0.15 + Math.min(0.1, S.labRuns.filter((x) => x.sid === sid).length * 0.01);
    const gates = [
      { label: `OOS trades ≥ 30 (${n})`, ok: n >= 30 },
      { label: `Expectancy ≥ +${adj.toFixed(2)}R after costs, multiple-testing adjusted (${exp.toFixed(2)}R)`, ok: exp >= adj },
      { label: `Profit factor ≥ 1.2 (${pf.toFixed(2)})`, ok: pf >= 1.2 },
      { label: `Positive in ≥ 60% of folds (${posFolds}/${folds})`, ok: posFolds / folds >= 0.6 },
      { label: 'Beats random-entry baseline with same exits', ok: exp > 0.05 },
    ];
    const passed = gates.filter((g) => g.ok).length;
    let verdict = passed === gates.length ? 'PASS' : passed >= 3 ? 'MARGINAL' : 'FAIL';
    if (mode === 'quick' && verdict === 'PASS') verdict = 'MARGINAL';
    const w = warns.slice();
    if (mode === 'quick') w.push('Quick test cannot PASS — run the full walk-forward test.');
    if (n < 80) w.push('Low sample size — results are noisy.');
    const syms = (basket ? basket.members : []).slice(0, 12);
    const bySym = syms.map((s) => ({ s, n: Math.max(3, Math.round(n / syms.length * (0.6 + rr() * 0.8))), e: +(exp + (rr() - 0.5) * 0.6).toFixed(2) })).sort((a, b) => b.e - a.e);
    lab.result = { id: BT.uid('LAB-'), sid, mode: mode === 'quick' ? 'quick test' : 'walk-forward', basket: lab.basket, from: lab.from, at: Date.now(), n, win, exp, pf, gates, verdict, curve, bySym, warns: w };
    S.labRuns.unshift({ id: lab.result.id, at: lab.result.at, sid, basket: lab.basket, mode: lab.result.mode, n, exp, verdict });
    S.labRuns.length = Math.min(S.labRuns.length, 30);
    BT.audit('system', 'Strategy Lab run', `${sid} on ${lab.basket}: ${verdict} (${n} trades, ${exp.toFixed(2)}R)`);
    lab.running = false; BT.save();
    if (location.hash.startsWith('#/lab')) BT.rerender();
    BT.toast(`Strategy Lab: ${sid} → ${verdict}`, verdict === 'PASS' ? 'good' : '');
  }

  /* ================= SETTINGS ================= */
  BT.pages.settings = function (el) {
    const S = BT.S;
    const st = S.settings;
    el.innerHTML = `<div class="page-head"><div><h1>Settings</h1><p>Trading limits and review workflow. Changes to limits ask for your password and apply from the next service run.</p></div></div>
      <div class="grid g2">
        <div class="card"><div class="card-h"><h3>Plan & risk limits</h3><span class="chip amber">password confirm</span></div><div class="card-b grid g2">
          <label class="field">Max new entries per day<input class="input num" type="number" id="s-entries" min="1" max="10" value="${st.maxNewEntries}"></label>
          <label class="field">Max tickets in plan<input class="input num" type="number" id="s-items" min="1" max="10" value="${st.maxPlanItems}"></label>
          <label class="field">Max open positions<input class="input num" type="number" id="s-open" min="1" max="20" value="${st.maxOpenPositions}"></label>
          <label class="field">Risk per trade (%)<input class="input num" type="number" id="s-risk" step="0.05" min="0.05" max="2" value="${st.riskPct}"></label></div></div>
        <div class="card"><div class="card-h"><h3>Review workflow & access</h3></div><div class="card-b grid g2">
          <label class="field">Review cutoff<input class="input" type="time" id="s-cut" value="${st.reviewCutoff}"></label>
          <label class="field">Unreviewed tickets<select class="input" id="s-policy"><option value="lapse" ${st.unreviewedPolicy === 'lapse' ? 'selected' : ''}>Lapse (safe default)</option><option value="arm_top_n" ${st.unreviewedPolicy === 'arm_top_n' ? 'selected' : ''}>Arm top N automatically</option></select></label>
          <label class="field">Idle timeout (minutes)<input class="input num" type="number" id="s-idle" min="1" max="240" value="${st.idleTimeoutMin}"></label>
          <div class="small muted" style="align-self:end">Service schedules (pre-open check, EOD fetch, refresh intervals…) are configured on the <a href="#/services">Services</a> page.</div></div></div>
        <div class="card"><div class="card-h"><h3>Action center thresholds</h3></div><div class="card-b grid g3">
          ${[['lossPct', 'Flag loss below cost (%)'], ['gainPct', 'Flag gain above (%)'], ['weightPct', 'Max single position (% of account)'], ['sectorPct', 'Max sector (% of account)'], ['resultsDays', 'Results within (days)'], ['lagPp', 'Lagging Nifty by (pp XIRR)']].map(([k, l]) => `<label class="field">${l}<input class="input num" type="number" data-th="${k}" value="${(st.actions || {})[k] ?? ''}"></label>`).join('')}</div></div>
        <div class="card"><div class="card-h"><h3>Prototype simulation</h3></div><div class="card-b grid">
          <label class="check"><input type="checkbox" id="s-mkt" ${st.demoMarketOpen ? 'checked' : ''}> Simulate market open (live ticks for the active set)</label>
          <label class="field">Simulation speed<select class="input" id="s-speed" style="width:auto">${[1, 3, 10].map((x) => `<option value="${x}" ${(st.simSpeed || 1) === x ? 'selected' : ''}>${x}×</option>`).join('')}</select></label>
          <div class="small muted">Prices are synthetic. All state is stored in this browser (localStorage).</div></div></div>
        <div class="card"><div class="card-h"><h3>Danger zone</h3></div><div class="card-b row"><button class="btn bad" id="s-reset">Reset demo data</button><span class="small muted">Restores original accounts, baskets, plan, portfolio.</span></div></div>
      </div>
      <div class="row" style="margin-top:14px;justify-content:flex-end"><button class="btn primary" id="s-save">Save settings</button></div>`;
    $('#s-save').onclick = async () => {
      const n = { maxNewEntries: +$('#s-entries').value, maxPlanItems: +$('#s-items').value, maxOpenPositions: +$('#s-open').value, riskPct: +$('#s-risk').value,
        reviewCutoff: $('#s-cut').value, unreviewedPolicy: $('#s-policy').value, idleTimeoutMin: +$('#s-idle').value, demoMarketOpen: $('#s-mkt').checked, simSpeed: +$('#s-speed').value };
      const th = Object.assign({}, st.actions); $$('[data-th]').forEach((i) => { if (i.value !== '') th[i.dataset.th] = +i.value; });
      const cutIssues = BT.scheduleIssues(BT.S.services, { ...st, reviewCutoff: n.reviewCutoff }).filter((i) => i.level === 'error' && i.id === 'preopen');
      if (cutIssues.length) return BT.toast(cutIssues[0].msg + ' Change the pre-open time on the Services page first.', 'bad');
      if (n.maxNewEntries > n.maxPlanItems) return BT.toast('Max new entries cannot exceed max tickets in plan', 'bad');
      const limitsChanged = ['maxNewEntries', 'maxPlanItems', 'maxOpenPositions', 'riskPct', 'unreviewedPolicy'].some((k) => n[k] !== st[k]);
      if (limitsChanged && !(await BT.stepUp('Changing risk limits'))) return;
      const diff = Object.keys(n).filter((k) => n[k] !== st[k]).map((k) => `${k}: ${st[k]} → ${n[k]}`).join(', ');
      Object.assign(st, n); st.actions = th;
      BT.audit('owner', 'Settings changed', diff || 'no changes'); BT.save(); BT.rerender(); BT.toast('Settings saved', 'good');
    };
    $('#s-reset').onclick = async () => { if (await BT.confirmDlg('Reset demo data', 'Restore the original demo data in this browser?', 'Reset', true)) BT.reset(); };
  };

  /* ================= AUDIT ================= */
  const au = { actor: '', q: '' };
  BT.pages.audit = function (el) {
    const S = BT.S;
    const list = S.audit.filter((a) => (!au.actor || (au.actor === 'system' ? a.actor === 'system' : a.actor !== 'system')) && (!au.q || (a.action + ' ' + a.detail).toLowerCase().includes(au.q.toLowerCase())));
    el.innerHTML = `<div class="page-head"><div><h1>Audit log</h1><p>Every login, configuration change, approval and service action — who (you or the service identity), what and when.</p></div></div>
      <div class="card" style="margin-bottom:12px"><div class="card-b row"><div class="seg" id="au-a">${[['', 'All'], ['owner', 'You'], ['system', 'Services']].map(([k, l]) => `<button data-a="${k}" class="${au.actor === k ? 'on' : ''}">${l}</button>`).join('')}</div><input class="input" id="au-q" placeholder="Search" style="width:240px" value="${esc(au.q)}"><span class="small muted">${list.length} entries</span></div></div>
      <div class="card"><div class="card-b flush"><div class="tbl-wrap" style="max-height:640px"><table class="tbl"><thead><tr><th>When</th><th>Actor</th><th>Action</th><th>Detail</th></tr></thead><tbody>
        ${list.slice(0, 300).map((a) => `<tr><td>${fmtDT(a.at)}</td><td><span class="chip ${a.actor === 'system' ? 'purple' : a.actor === 'anonymous' ? 'red' : 'blue'}">${esc(a.actor === 'system' ? 'service' : a.actor)}</span></td><td>${esc(a.action)}</td><td style="white-space:normal">${esc(a.detail)}</td></tr>`).join('')}</tbody></table></div></div></div>`;
    $$('#au-a button').forEach((b) => (b.onclick = () => { au.actor = b.dataset.a; BT.rerender(); }));
    $('#au-q').onchange = (e) => { au.q = e.target.value; BT.rerender(); };
  };
})();
