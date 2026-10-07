/* xDrishti prototype — core: state, auth, router, shell, simulated services & live engine, chart helpers */
(function () {
  'use strict';
  const D = window.BTData;
  const BT = (window.BT = { pages: {} });

  /* ---------------- utilities ---------------- */
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const inr = (v, d = 0) => (v == null || !isFinite(v) ? '—' : (v < 0 ? '−' : '') + '₹' + Math.abs(v).toLocaleString('en-IN', { maximumFractionDigits: d, minimumFractionDigits: d }));
  const num = (v, d = 2) => (v == null || !isFinite(v) ? '—' : v.toLocaleString('en-IN', { maximumFractionDigits: d, minimumFractionDigits: d }));
  const pct = (v, d = 1) => (v == null || !isFinite(v) ? '—' : (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v * 100).toFixed(d) + '%');
  const sgn = (v) => (v > 0 ? 'pos' : v < 0 ? 'neg' : '');
  const fmtDate = (t) => new Date(t).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
  const fmtDT = (t) => new Date(t).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false });
  const fmtTime = (t) => new Date(t).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
  function ago(t) {
    const s = Math.round((Date.now() - t) / 1000);
    if (s < 60) return s + 's ago'; if (s < 3600) return Math.round(s / 60) + 'm ago';
    if (s < 86400) return Math.round(s / 3600) + 'h ago'; return Math.round(s / 86400) + 'd ago';
  }
  const uid = (p = 'ID') => p + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  Object.assign(BT, { $, $$, esc, inr, num, pct, sgn, fmtDate, fmtDT, fmtTime, ago, uid, D });

  /* ---------------- state ---------------- */
  const KEY = 'xd-proto-v1';
  const SKEY = 'xd-proto-session';
  let S = null;
  BT.mem = { imported: {} }; // non-persisted (large) data, e.g. imported CSV bars
  function load() {
    try { S = JSON.parse(localStorage.getItem(KEY)); } catch (e) { S = null; }
    if (!S || S.version !== 1) { S = D.seed(); S.auth = defaultAuth(); }
    if (!S.auth) S.auth = defaultAuth();
    delete S.auth.totp; // second factor removed (personal, LAN-only)
    S.settings.autonomy = Object.assign({ level: 'supervised', autoApproveMinP: 0.58, autoApplyLearning: true, maxChangesPerWeek: 3 }, S.settings.autonomy || {});
    S.experiments = S.experiments || [];
    normalizeServices();
    normalizeAccounts();
    S.alerts = S.alerts || []; S.portfolioActions = S.portfolioActions || {}; S.dataFixes = S.dataFixes || {};
    if (!S.plan) S.plan = D.proposePlan(S, D.iso(D.nextTradingDay()));
    if (!S.suggestions.length) S.suggestions = D.seedSuggestions(S);
    if (!S.audit.length) {
      audit('system', 'Seed', 'Demo data created: 3 accounts, 4 baskets, ' + Object.keys(S.tracked).length + ' tracked instruments');
    }
    refreshRuleBaskets();
    BT.S = S;
    save();
  }
  let resetting = false;
  function save() { if (resetting) return; try { localStorage.setItem(KEY, JSON.stringify(S)); } catch (e) { /* storage full or blocked */ } }
  // stop all further saves first, otherwise the unload handler writes the old state straight back
  function reset() { resetting = true; try { localStorage.removeItem(KEY); sessionStorage.removeItem(SKEY); } catch (e) { /* ignore */ } location.hash = ''; location.reload(); }
  function defaultAuth() { return { user: 'owner', pw: D.hash('demo-password'), failed: 0, lockUntil: 0 }; }
  function audit(actor, action, detail) {
    S.audit.unshift({ at: Date.now(), actor, action, detail });
    if (S.audit.length > 500) S.audit.length = 500;
  }
  function notify(text, level = 'info', link) {
    S.notifications.unshift({ id: uid('N'), at: Date.now(), text, level, link, read: false });
    if (S.notifications.length > 100) S.notifications.length = 100;
    renderTopStatus();
  }
  Object.assign(BT, { load, save, reset, audit, notify, get S() { return S; } });

  /* ---------------- accounts & risk profiles (normalisation / migration) ---------------- */
  const ACC_COLORS = ['#6366f1', '#10b981', '#f59e0b', '#8b5cf6', '#f43f5e', '#06b6d4'];
  function normalizeAccounts() {
    S.riskProfiles = S.riskProfiles || [
      { id: 'standard', name: 'Standard', riskPct: 0.5, maxNewEntries: 3, maxOpen: 5, dailyLossPct: 1.5, maxPerSector: 2, heatPct: 3 },
      { id: 'conservative', name: 'Conservative', riskPct: 0.25, maxNewEntries: 2, maxOpen: 3, dailyLossPct: 1.0, maxPerSector: 1, heatPct: 2 },
      { id: 'aggressive', name: 'Aggressive', riskPct: 1.0, maxNewEntries: 4, maxOpen: 6, dailyLossPct: 2.5, maxPerSector: 3, heatPct: 5 },
    ];
    S.accounts.forEach((a, i) => {
      const r = D.rng('funds:' + a.id);
      a.color = a.color || ACC_COLORS[i % ACC_COLORS.length];
      a.notes = a.notes ?? '';
      const t = a.roles.trading;
      t.styles = t.styles || (t.profile === 'conservative' ? ['swing'] : ['intraday', 'swing']);
      t.instruments = t.instruments || { intraday: ['EQ_MIS'], swingBuy: ['EQ_CNC'], swingSell: ['FUT'] };
      t.capitalSource = t.capitalSource || 'fixed';
      t.overrides = t.overrides || {};
      const p = a.roles.portfolio;
      p.historyFrom = p.historyFrom || '2023-01-01';
      if (p.includeInTotals === undefined) p.includeInTotals = true;
      const d = a.roles.data;
      d.subscription = d.subscription || { active: !!d.enabled, renewsOn: D.iso(new Date(Date.now() + (8 + Math.floor(r() * 20)) * 86400000)) };
      d.liveBudget = d.liveBudget || 200;
      a.connection = a.connection || { tokenMethod: 'API key & secret → daily token', lastError: null };
      a.funds = a.funds || { available: Math.round(50000 + r() * 400000), used: Math.round(r() * 150000) };
    });
  }
  BT.reNormalize = normalizeAccounts;
  BT.profileOf = (acc) => Object.assign({}, (S.riskProfiles.find((p) => p.id === acc.roles.trading.profile) || S.riskProfiles[0]), acc.roles.trading.overrides || {});

  /* ---------------- price alerts ---------------- */
  BT.evaluateAlerts = function () {
    let n = 0;
    for (const al of S.alerts.filter((x) => x.active)) {
      const p = BT.ltp(al.symbol);
      if ((al.op === 'below' && p <= al.price) || (al.op === 'above' && p >= al.price)) {
        al.active = false; al.triggeredAt = Date.now(); al.triggeredPrice = p; n++;
        notify(`Alert: ${al.symbol} ${al.op} ${num(al.price)} (now ${num(p)})${al.note ? ' — ' + al.note : ''}`, 'warn', '#/portfolio');
        audit('system', 'Price alert triggered', `${al.symbol} ${al.op} ${al.price} @ ${p}`);
      }
    }
    if (n) save();
    return n;
  };

  /* ---------------- derived helpers ---------------- */
  BT.inst = (sym) => D.BY_SYMBOL[sym];
  BT.ltpMap = {};
  BT.ltp = (sym) => BT.ltpMap[sym] ?? D.lastClose(sym);
  BT.dayChange = (sym) => BT.ltp(sym) / D.prevClose(sym) - 1;
  BT.accountsWith = (role) => S.accounts.filter((a) => a.roles[role] && a.roles[role].enabled);
  BT.dataAccount = () => S.accounts.find((a) => a.roles.data.enabled && a.roles.data.primary);
  BT.standbyAccount = () => S.accounts.find((a) => a.roles.data.enabled && a.roles.data.standby);
  BT.accName = (id) => (S.accounts.find((a) => a.id === id) || { name: id }).name;
  BT.positions = () => D.buildPositions(S.transactions, BT.ltp);
  BT.basketsOf = (sym) => S.baskets.filter((b) => b.members.includes(sym)).map((b) => b.name);
  function refreshRuleBaskets() {
    for (const b of S.baskets.filter((x) => x.type === 'rule')) b.members = ruleMembers(b.rule);
  }
  function ruleMembers(rule) {
    return D.INSTRUMENTS.filter((i) =>
      (!rule.sectors || !rule.sectors.length || rule.sectors.includes(i.sector)) &&
      (!rule.fnoOnly || i.fno) && (!rule.minPrice || D.lastClose(i.symbol) >= rule.minPrice) &&
      (!rule.trackedOnly || S.tracked[i.symbol])).map((i) => i.symbol);
  }
  Object.assign(BT, { refreshRuleBaskets, ruleMembers });
  BT.systemBaskets = () => {
    const pos = BT.positions().filter((p) => p.status === 'open');
    return [
      { id: 'sys-open', name: 'Open positions', type: 'system', purpose: 'watch', color: '#64748b', members: [...new Set(pos.filter((p) => p.bucket === 'Trading').map((p) => p.symbol))] },
      { id: 'sys-hold', name: 'Holdings', type: 'system', purpose: 'long_term', color: '#64748b', members: [...new Set(pos.filter((p) => p.bucket !== 'Trading').map((p) => p.symbol))] },
    ];
  };

  /* ---------------- toast / modal / drawer / step-up ---------------- */
  function toast(text, level = '') {
    let box = $('#toasts');
    if (!box) { box = document.createElement('div'); box.id = 'toasts'; document.body.appendChild(box); }
    const t = document.createElement('div');
    t.className = 'toast ' + level;
    t.innerHTML = (BT.icon ? BT.icon(level === 'good' ? 'checkCircle' : level === 'bad' ? 'xCircle' : 'bell') : '') + '<span></span>';
    t.lastChild.textContent = text; box.appendChild(t);
    setTimeout(() => (BT.animateOut ? BT.animateOut(t) : t.remove()), 3600);
  }
  function modal({ title, body, foot, wide, onMount }) {
    const ov = document.createElement('div');
    ov.className = 'overlay center';
    ov.innerHTML = `<div class="modal${wide ? ' wide' : ''}" role="dialog" aria-modal="true"><div class="dlg-h"><h3>${esc(title)}</h3><button class="btn ghost icon" data-close aria-label="Close">${BT.icon('x')}</button></div><div class="dlg-b">${body}</div>${foot ? `<div class="dlg-f">${foot}</div>` : ''}</div>`;
    const close = () => { document.removeEventListener('keydown', onKey); BT.animateOut(ov); };
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    ov.addEventListener('click', (e) => { if (e.target === ov || e.target.closest('[data-close]')) close(); });
    document.addEventListener('keydown', onKey);
    document.body.appendChild(ov);
    if (onMount) onMount(ov, close);
    const f = ov.querySelector('input, select, textarea'); if (f) f.focus();
    return { el: ov, close };
  }
  function drawer({ title, body, onMount }) {
    const ov = document.createElement('div');
    ov.className = 'overlay';
    ov.innerHTML = `<aside class="drawer"><div class="dlg-h"><h3>${title}</h3><button class="btn ghost icon" data-close aria-label="Close">${BT.icon('x')}</button></div><div class="dlg-b">${body}</div></aside>`;
    const charts = [];
    const close = () => { document.removeEventListener('keydown', onKey); BT.animateOut(ov, () => charts.forEach((c) => c.dispose())); };
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    ov.addEventListener('click', (e) => { if (e.target === ov || e.target.closest('[data-close]')) close(); });
    document.addEventListener('keydown', onKey);
    document.body.appendChild(ov);
    if (onMount) onMount(ov, close, charts);
    return { el: ov, close };
  }
  function confirmDlg(title, text, okLabel = 'Confirm', danger) {
    return new Promise((resolve) => {
      modal({
        title, body: `<p style="margin:0">${text}</p>`,
        foot: `<button class="btn" data-close>Cancel</button><button class="btn ${danger ? 'bad solid' : 'primary'}" id="cf-ok">${esc(okLabel)}</button>`,
        onMount: (ov, close) => { $('#cf-ok', ov).onclick = () => { close(); resolve(true); }; ov.addEventListener('click', (e) => { if (e.target.closest('[data-close]') || e.target === ov) resolve(false); }); },
      });
    });
  }
  // Sensitive actions ask for the password again (re-authentication). Prefilled in the prototype.
  function stepUp(reason) {
    return new Promise((resolve) => {
      modal({
        title: 'Confirm it is you',
        body: `<div class="row" style="flex-wrap:nowrap;align-items:flex-start">${BT.navIcon('accounts')}<p style="margin:0"><b>${esc(reason)}</b> is a sensitive action. Confirm with your password — the change is recorded in the audit log.</p></div>
          <label class="field">Password<input class="input" id="su-pw" type="password" value="demo-password" autocomplete="current-password"></label>
          <div class="err" id="su-err"></div><div class="small muted">Prototype: the demo password is prefilled.</div>`,
        foot: '<button class="btn" data-close>Cancel</button><button class="btn primary" id="su-ok">Confirm</button>',
        onMount: (ov, close) => {
          let done = false;
          const ok = () => {
            if (D.hash($('#su-pw', ov).value) === S.auth.pw) { done = true; close(); audit('owner', 'Re-authenticated', reason); resolve(true); }
            else { $('#su-err', ov).textContent = 'Wrong password.'; audit('owner', 'Re-authentication failed', reason); save(); }
          };
          $('#su-ok', ov).onclick = ok;
          $('#su-pw', ov).addEventListener('keydown', (e) => { if (e.key === 'Enter') ok(); });
          ov.addEventListener('click', (e) => { if (!done && (e.target === ov || e.target.closest('[data-close]'))) resolve(false); });
          setTimeout(() => $('#su-ok', ov).focus(), 30);
        },
      });
    });
  }
  Object.assign(BT, { toast, modal, drawer, confirmDlg, stepUp });

  /* ---------------- charts ---------------- */
  BT.charts = [];
  const isDark = () => document.documentElement.dataset.theme === 'dark';
  const css = (v) => getComputedStyle(document.documentElement).getPropertyValue(v).trim();
  BT.palette = ['#6366f1', '#10b981', '#f59e0b', '#ec4899', '#06b6d4', '#8b5cf6', '#f97316', '#64748b', '#ef4444', '#84cc16'];
  BT.echart = function (el, option, registry = BT.charts) {
    if (!el || !window.echarts) return null;
    const c = echarts.init(el, isDark() ? 'dark' : null, { renderer: 'canvas' });
    c.setOption(Object.assign({ backgroundColor: 'transparent', color: BT.palette, textStyle: { fontFamily: css('--font') } }, option));
    const ro = new ResizeObserver(() => c.resize());
    ro.observe(el);
    const h = { dispose: () => { ro.disconnect(); c.dispose(); }, chart: c };
    registry.push(h);
    return h;
  };
  const lwTime = (t) => Math.floor(t / 1000) - new Date(t).getTimezoneOffset() * 60;
  BT.candles = function (el, bars, { lines = [], markers = [], daily = false, height } = {}, registry = BT.charts) {
    if (!el || !window.LightweightCharts) return null;
    const L = window.LightweightCharts;
    const chart = L.createChart(el, {
      height: height || el.clientHeight || 260, autoSize: true,
      layout: { background: { type: 'solid', color: 'transparent' }, textColor: css('--text-2'), fontFamily: css('--font') },
      grid: { vertLines: { color: css('--border') }, horzLines: { color: css('--border') } },
      rightPriceScale: { borderColor: css('--border') },
      timeScale: { borderColor: css('--border'), timeVisible: !daily, secondsVisible: false },
      crosshair: { mode: 1 },
    });
    const s = chart.addSeries(L.CandlestickSeries, { upColor: '#10b981', downColor: '#f43f5e', borderVisible: false, wickUpColor: '#10b981', wickDownColor: '#f43f5e' });
    s.setData(bars.map((b) => ({ time: daily ? b.date : lwTime(b.t), open: b.open, high: b.high, low: b.low, close: b.close })));
    lines.forEach((l) => s.createPriceLine({ price: l.price, color: l.color, lineWidth: 1, lineStyle: l.style ?? 2, axisLabelVisible: true, title: l.title }));
    if (markers.length && L.createSeriesMarkers) L.createSeriesMarkers(s, markers.map((m) => ({ ...m, time: daily ? m.time : lwTime(m.time) })));
    chart.timeScale().fitContent();
    const h = { dispose: () => chart.remove(), chart, series: s };
    registry.push(h);
    return h;
  };
  BT.disposeCharts = () => { BT.charts.forEach((c) => { try { c.dispose(); } catch (e) { /* ignore */ } }); BT.charts = []; };

  /* ---------------- auth & session ---------------- */
  function session() { try { return JSON.parse(sessionStorage.getItem(SKEY)); } catch (e) { return null; } }
  function setSession(v) { try { v ? sessionStorage.setItem(SKEY, JSON.stringify(v)) : sessionStorage.removeItem(SKEY); } catch (e) { /* ignore */ } }
  BT.isAuthed = () => { const s = session(); return !!(s && s.ok && Date.now() - s.last < S.settings.idleTimeoutMin * 60000); };
  BT.touch = () => { const s = session(); if (s && s.ok) { s.last = Date.now(); setSession(s); } };
  // mode: 'signin' (fresh) or 'lock' (session locked — same user, unlock with password)
  function showLogin(message, mode = 'signin') {
    BT.disposeCharts();
    document.body.classList.remove('nav-open');
    document.title = (mode === 'lock' ? 'Locked' : 'Sign in') + ' · xDrishti';
    const lock = mode === 'lock';
    const feat = [['learning', 'Learns from every trade', 'Graded on your real fills; setups, exits and probabilities improve'], ['review', 'Next-day plan', 'Conditional tickets you approve — or auto-approve within limits'],
      ['portfolio', 'Portfolio & XIRR', 'All Dhan accounts, grouped by basket'], ['services', 'Runs as services', 'Local on Podman — keeps working while you are signed out']];
    // non-sensitive status only (no balances or positions before sign-in)
    const pre = S.services.find((x) => x.id === 'preopen'), nightly = S.services.find((x) => x.id === 'nightly');
    const healthy = S.services.filter((x) => x.enabled && x.lastOutcome !== 'failed').length;
    $('#app').innerHTML = `<div class="login-wrap"><main class="login-shell">
      <section class="login-hero" aria-label="About xDrishti">
        <div class="brand-row">${BT.brandMark('lg')}<div><b>xDrishti</b><div class="brand-sub">दृष्टि · vision, insight</div></div></div>
        <h1>See your edge clearly. Trade it with discipline.</h1>
        <p>Your personal, self-learning NSE trading desk — plans tomorrow, learns from what actually happened, and keeps you in control.</p>
        <ul class="hero-feats">${feat.map(([id, t, d], i) => `<li class="hero-feat" style="animation-delay:${0.1 + i * 0.06}s">${BT.navIcon(id)}<div><b>${t}</b><span>${d}</span></div></li>`).join('')}</ul>
        <div class="hero-status" title="Background services run on this machine whether or not you are signed in">
          <span class="dot green pulse"></span><span><b>${healthy}/${S.services.length}</b> services healthy</span>
          <span class="sep"></span><span>Next: ${esc(pre.name.split(' &')[0])} ${esc(BT.nextOccurrence(pre))}</span>
          <span class="sep hide-sm"></span><span class="hide-sm">Plan for ${fmtDate(new Date(S.plan.date))} ${S.plan.items.length ? 'ready' : 'pending'} · nightly ${esc(nightly.sched.time || '')}</span>
        </div>
      </section>
      <section class="login-panel" aria-label="${lock ? 'Unlock' : 'Sign in'}">
        <div class="login-card">
          ${lock ? `<div class="who"><span class="avatar lg">O</span><div><h2>Welcome back</h2><div class="muted">Signed in as <b>${esc(S.auth.user)}</b> · session locked</div></div></div>`
            : `<h2>Sign in</h2><div class="muted">Single owner · this machine and your home network only</div>`}
          ${message ? `<div class="banner ${lock ? 'info' : 'warn'}" style="margin:16px 0 0" role="status">${BT.icon(lock ? 'lock' : 'alert')}<div>${esc(message)}</div></div>` : ''}
          <form id="login-form" autocomplete="on" novalidate>
            <label class="field ${lock ? 'hidden' : ''}">Username<input class="input" id="lg-user" name="username" autocomplete="username" value="${esc(S.auth.user)}" required></label>
            <label class="field">Password
              <span class="pw-wrap"><input class="input" id="lg-pw" name="password" type="password" autocomplete="current-password" value="demo-password" required aria-describedby="lg-caps lg-err">
              <button type="button" class="pw-eye" id="lg-eye" aria-label="Show password" title="Show password">${BT.icon('eye')}</button></span></label>
            <div class="caps hidden" id="lg-caps">${BT.icon('alert')} Caps Lock is on</div>
            <div class="err" id="lg-err" role="alert" aria-live="assertive"></div>
            <button class="btn primary lg" id="lg-btn" type="submit"><span class="lbl">${lock ? 'Unlock' : 'Sign in'}</span>${BT.icon('chevronDown', 'rot-270')}</button>
            ${lock ? `<button type="button" class="btn ghost" id="lg-switch">Not you? Sign out completely</button>` : ''}
          </form>
          <details class="login-hint"><summary>${BT.icon('lightbulb')} About this prototype</summary>
            <p>Demo credentials are prefilled — just press <b>${lock ? 'Unlock' : 'Sign in'}</b> (or Enter).</p>
            <p>Real system: hashed password, lockout for 15 minutes after 5 failed attempts, idle lock after ${S.settings.idleTimeoutMin} minutes, password re-entry for sensitive actions. Signing out never stops the services.</p>
            <p><a href="#" id="lg-reset">Reset demo data</a></p></details>
        </div>
        <footer class="login-foot"><span>Personal use · LAN only · v0.1 prototype</span><button type="button" class="btn ghost sm" id="lg-theme">${BT.icon(isDark() ? 'sun' : 'moon')} ${isDark() ? 'Light' : 'Dark'} mode</button></footer>
      </section></main></div>`;
    const err = $('#lg-err'), btn = $('#lg-btn'), pw = $('#lg-pw');
    // lockout with a live countdown
    let tmr;
    const lockTick = () => {
      const left = S.auth.lockUntil - Date.now();
      if (left > 0) { btn.disabled = true; err.textContent = `Too many failed attempts. Try again in ${Math.floor(left / 60000)}:${String(Math.floor(left / 1000) % 60).padStart(2, '0')}.`; tmr = setTimeout(lockTick, 1000); }
      else if (btn.disabled) { btn.disabled = false; err.textContent = ''; }
    };
    lockTick();
    $('#lg-reset').onclick = (e) => { e.preventDefault(); reset(); };
    $('#lg-theme').onclick = () => { const t = isDark() ? 'light' : 'dark'; document.documentElement.dataset.theme = t; try { localStorage.setItem('xd-proto-theme', t); } catch (e) { /* ignore */ } showLogin(message, mode); };
    $('#lg-eye').onclick = () => { const show = pw.type === 'password'; pw.type = show ? 'text' : 'password'; $('#lg-eye').innerHTML = BT.icon(show ? 'lock' : 'eye'); $('#lg-eye').setAttribute('aria-label', show ? 'Hide password' : 'Show password'); pw.focus(); };
    const caps = (e) => { if (e.getModifierState) $('#lg-caps').classList.toggle('hidden', !e.getModifierState('CapsLock')); };
    pw.addEventListener('keydown', caps); pw.addEventListener('keyup', caps);
    [pw, $('#lg-user')].forEach((i) => i.addEventListener('input', () => { if (!btn.disabled) err.textContent = ''; i.classList.remove('invalid'); }));
    if ($('#lg-switch')) $('#lg-switch').onclick = () => { audit('owner', 'Logout', 'Signed out from the lock screen'); setSession(null); showLogin('You have signed out. Background services keep running.'); };
    $('#login-form').onsubmit = (e) => {
      e.preventDefault();
      if (S.auth.lockUntil > Date.now() || btn.classList.contains('loading')) return;
      if (!pw.value) { err.textContent = 'Enter your password.'; pw.classList.add('invalid'); pw.focus(); return; }
      btn.classList.add('loading'); btn.querySelector('.lbl').textContent = lock ? 'Unlocking…' : 'Signing in…';
      setTimeout(() => {
        const ok = $('#lg-user').value.trim() === S.auth.user && D.hash(pw.value) === S.auth.pw;
        if (!ok) {
          btn.classList.remove('loading'); btn.querySelector('.lbl').textContent = lock ? 'Unlock' : 'Sign in';
          S.auth.failed++; audit('anonymous', 'Login failed', 'Wrong username or password (attempt ' + S.auth.failed + ')');
          if (S.auth.failed >= 5) { S.auth.lockUntil = Date.now() + 15 * 60000; S.auth.failed = 0; audit('system', 'Lockout', 'Login locked for 15 minutes'); }
          save();
          if (S.auth.lockUntil > Date.now()) lockTick();
          else err.textContent = `Wrong ${lock ? 'password' : 'username or password'}. ${5 - S.auth.failed} attempt(s) left before a 15-minute lock.`;
          pw.classList.add('invalid'); pw.select();
          $('.login-card').classList.remove('shake'); void $('.login-card').offsetWidth; $('.login-card').classList.add('shake');
          return;
        }
        clearTimeout(tmr);
        S.auth.failed = 0;
        setSession({ ok: true, at: Date.now(), last: Date.now() });
        audit('owner', lock ? 'Unlock' : 'Login', 'Signed in with password');
        const ran = catchUp();
        save();
        start();
        if (ran.length) toast(`While you were away, services ran: ${ran.join(', ')}`, 'good');
      }, 380);
    };
    (lock ? pw : btn).focus();
  }
  BT.logout = (reason) => { audit('owner', reason ? 'Session locked' : 'Logout', reason || 'Signed out'); S.lastSeen = Date.now(); save(); setSession(reason ? { ok: false, locked: true } : null); showLogin(reason ? reason : 'You have signed out. Background services keep running.', reason ? 'lock' : 'signin'); };

  /* ---------------- services (simulated) — schedules are configurable on the Services page ---------------- */
  const TD = [1, 2, 3, 4, 5]; // trading days Mon–Fri (holidays come from the exchange calendar in the real system)
  const DAYN = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const SERVICE_DEFS = [
    { id: 'live-feed', name: 'Live feed (active set)', desc: 'Dhan WebSocket for open positions, armed tickets, context indices, pinned baskets', runsAs: 'xd-feed', sched: { type: 'window', start: '09:00', end: '15:35', days: TD }, params: { maxInstruments: 200, mode: 'quote' } },
    { id: 'holdings-refresh', name: 'Holdings quote refresh', desc: 'Dhan quote API for long-term holdings and alert symbols (≤1,000 per request)', runsAs: 'xd-feed', sched: { type: 'interval', every: 5, start: '09:15', end: '15:30', days: TD }, params: {} },
    { id: 'preopen', name: 'Pre-open check & arming', desc: 'After the review cutoff and NSE pre-open price discovery: arms approved tickets, invalidates gaps, lapses unreviewed tickets', runsAs: 'xd-worker', sched: { type: 'daily', time: '09:10', days: TD }, params: { gapInvalidateAtr: 0.5 } },
    { id: 'alerts', name: 'Price alerts & action center', desc: 'Evaluates your price alerts and portfolio action rules', runsAs: 'xd-worker', sched: { type: 'interval', every: 1, start: '09:15', end: '15:30', days: TD }, params: {} },
    { id: 'eod-fetch', name: 'End-of-day fetch & aggregation', desc: "Today's 1-minute + daily for all tracked instruments, gap backfill, aggregates refresh", runsAs: 'xd-worker', sched: { type: 'daily', time: '15:50', days: TD }, params: { throttlePerSec: 5, gapLookbackDays: 5 } },
    { id: 'validation', name: 'Data validation', desc: 'Re-aggregates from 1-minute and compares with stored aggregates and official daily; gaps, spikes, freshness', runsAs: 'xd-worker', sched: { type: 'after', after: 'eod-fetch' }, params: { daysToCheck: 5, tolerancePct: 0.05, spikeMultiple: 8 } },
    { id: 'portfolio-sync', name: 'Account & portfolio sync', desc: 'Holdings, positions, trade history, ledger → portfolio, auto-journal, daily valuation', runsAs: 'xd-worker', sched: { type: 'daily', time: '16:30', days: TD }, params: {} },
    { id: 'nightly', name: 'Nightly pipeline → proposed plan', desc: 'Grade, learn, scan baskets, score, select, allocate → plan for your review', runsAs: 'xd-worker', sched: { type: 'daily', time: '18:30', days: TD }, params: {} },
    { id: 'suggestions', name: 'AI suggestions & briefing', desc: 'Instrument / basket suggestions and the briefing, posted to the inbox', runsAs: 'xd-worker + xd-llm', sched: { type: 'after', after: 'nightly' }, params: {} },
    { id: 'csv-watcher', name: 'CSV inbox watcher', desc: 'Imports files dropped into data/inbox', runsAs: 'xd-worker', sched: { type: 'continuous' }, params: {} },
    { id: 'retrain', name: 'Weekly retrain & exit re-selection', desc: 'ML.NET retrain, calibration, hypothesis loop', runsAs: 'xd-worker', sched: { type: 'weekly', weekday: 6, time: '10:00' }, params: {} },
    { id: 'backup', name: 'Backup', desc: 'pg_dump + models + config → restic on external SSD', runsAs: 'xd-backup', sched: { type: 'daily', time: '23:30', days: [0, 1, 2, 3, 4, 5, 6] }, params: { retentionDaily: 30 } },
  ];
  BT.SERVICE_DEFS = SERVICE_DEFS; BT.DAYN = DAYN;
  function defaultServices() { return SERVICE_DEFS.map((d) => normalizeService({ id: d.id })); }
  // fills missing fields from the definition (also migrates older saved state)
  function normalizeService(svc) {
    const d = SERVICE_DEFS.find((x) => x.id === svc.id);
    if (!d) return svc;
    const out = Object.assign({ enabled: true, status: 'idle', history: [], retries: 2, timeoutMin: 30, catchUp: true, notifyOnFailure: true }, svc);
    out.name = d.name; out.desc = d.desc; out.runsAs = d.runsAs;
    if (!out.sched || !out.sched.type) out.sched = JSON.parse(JSON.stringify(d.sched));
    out.params = Object.assign({}, d.params, svc.params || {});
    if (!out.lastRun) out.lastRun = lastOccurrence(out.sched) || Date.now() - 3600000;
    delete out.at; delete out.schedule;
    if (out.status === 'running') out.status = 'idle';
    return out;
  }
  function normalizeServices() {
    const have = Object.fromEntries((S.services || []).map((x) => [x.id, x]));
    S.services = SERVICE_DEFS.map((d) => normalizeService(have[d.id] || { id: d.id }));
  }
  const hm = (t) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
  function lastOccurrence(sched, now = new Date()) {
    if (!sched || (sched.type !== 'daily' && sched.type !== 'weekly')) return null;
    const [h, m] = sched.time.split(':').map(Number);
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate(), h, m);
    const ok = (x) => (sched.type === 'weekly' ? x.getDay() === sched.weekday : (sched.days || TD).includes(x.getDay()));
    if (d > now) d.setDate(d.getDate() - 1);
    for (let i = 0; i < 8 && !ok(d); i++) d.setDate(d.getDate() - 1);
    return d.getTime();
  }
  function nextRun(sched, now = new Date()) {
    if (!sched || (sched.type !== 'daily' && sched.type !== 'weekly')) return null;
    const [h, m] = sched.time.split(':').map(Number);
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate(), h, m);
    const ok = (x) => (sched.type === 'weekly' ? x.getDay() === sched.weekday : (sched.days || TD).includes(x.getDay()));
    if (d <= now) d.setDate(d.getDate() + 1);
    for (let i = 0; i < 8 && !ok(d); i++) d.setDate(d.getDate() + 1);
    return d.getTime();
  }
  const daysText = (days) => (!days || days.length === 7 ? 'every day' : days.join() === TD.join() ? 'trading days' : days.map((x) => DAYN[x]).join(', '));
  BT.schedText = function (svc) {
    const s = svc.sched;
    if (s.type === 'daily') return `${daysText(s.days)} at ${s.time}`;
    if (s.type === 'weekly') return `${DAYN[s.weekday]} at ${s.time}`;
    if (s.type === 'window') return `${s.start}–${s.end}, ${daysText(s.days)}`;
    if (s.type === 'interval') return `every ${s.every} min, ${s.start}–${s.end}, ${daysText(s.days)}`;
    if (s.type === 'after') return `after “${(S.services.find((x) => x.id === s.after) || {}).name || s.after}”`;
    return 'continuous';
  };
  BT.nextOccurrence = function (svc) {
    const s = svc.sched;
    if (s.type === 'continuous') return 'continuous';
    if (s.type === 'after') return 'when “' + ((S.services.find((x) => x.id === s.after) || {}).name || s.after) + '” succeeds';
    if (s.type === 'window') return svc.id === 'live-feed' && S.settings.demoMarketOpen ? 'running now (demo)' : `next window ${s.start}`;
    if (s.type === 'interval') return S.settings.demoMarketOpen ? `every ${s.every} min (running)` : `from ${s.start} next trading day`;
    return fmtDT(nextRun(s));
  };
  BT.lastOccurrence = lastOccurrence;
  // Dependency & timing rules for schedules — shown on the Services page and enforced on save
  BT.scheduleIssues = function (services = S.services, settings = S.settings) {
    const get = (id) => services.find((x) => x.id === id);
    const t = (id) => { const x = get(id); return x && x.enabled && x.sched.type === 'daily' ? hm(x.sched.time) : null; };
    const out = [];
    const add = (id, level, msg) => out.push({ id, level, msg });
    const pre = t('preopen'), cut = hm(settings.reviewCutoff);
    if (pre != null && pre <= cut) add('preopen', 'error', `Pre-open check (${get('preopen').sched.time}) must run after the review cutoff (${settings.reviewCutoff}), otherwise late approvals are never armed.`);
    if (pre != null && pre >= hm('09:15')) add('preopen', 'error', 'Pre-open check must finish before the market opens at 09:15.');
    if (pre != null && pre < hm('09:08')) add('preopen', 'warn', 'NSE pre-open price discovery ends ~09:08 — running earlier means gap checks use stale prices.');
    const eod = t('eod-fetch');
    if (eod != null && eod < hm('15:35')) add('eod-fetch', 'error', 'End-of-day fetch must run after the close (15:30) and after the broker publishes minute data (~15:35+).');
    const sync = t('portfolio-sync'), nightly = t('nightly');
    if (sync != null && sync < hm('15:40')) add('portfolio-sync', 'warn', 'Portfolio sync before 15:40 may miss the final trades of the day.');
    if (nightly != null && eod != null && nightly <= eod) add('nightly', 'error', 'Nightly pipeline must run after the end-of-day fetch (it needs today\'s data).');
    if (nightly != null && sync != null && nightly <= sync) add('nightly', 'error', 'Nightly pipeline must run after the portfolio sync (it needs today\'s fills and positions).');
    const lf = get('live-feed');
    if (lf && lf.enabled && (hm(lf.sched.start) > hm('09:15') || hm(lf.sched.end) < hm('15:30'))) add('live-feed', 'warn', 'Live-feed window does not cover the whole session 09:15–15:30.');
    const bk = t('backup');
    if (bk != null && bk >= hm('09:00') && bk <= hm('15:35')) add('backup', 'warn', 'Backup during market hours competes with the live feed for resources.');
    for (const x of services.filter((y) => y.sched.type === 'after')) {
      let cur = x, seen = new Set([x.id]);
      while (cur && cur.sched.type === 'after') { cur = get(cur.sched.after); if (!cur) { add(x.id, 'error', 'Runs after a service that does not exist.'); break; } if (seen.has(cur.id)) { add(x.id, 'error', 'Circular “run after” dependency.'); break; } seen.add(cur.id); }
      const parent = get(x.sched.after); if (parent && !parent.enabled) add(x.id, 'warn', `Runs after “${parent.name}”, which is paused — it will not run.`);
    }
    for (const x of services.filter((y) => y.sched.type === 'interval' || y.sched.type === 'window')) if (hm(x.sched.start) >= hm(x.sched.end)) add(x.id, 'error', 'Window start must be before end.');
    return out;
  };

  const EFFECTS = {
    'preopen': (log) => {
      const r = D.rng('preopen:' + Date.now());
      let armed = 0, lapsed = 0, inval = 0, auto = 0;
      const au = S.settings.autonomy || {};
      for (const it of S.plan.items) {
        if (BT.autoEligible && BT.autoEligible(it)) {
          const used = S.plan.items.filter((x) => ['approved', 'modified', 'armed', 'triggered', 'closed'].includes(x.status)).length;
          if (used < S.settings.maxPlanItems) { it.status = 'approved'; it.autoApproved = true; it.decision = { at: Date.now(), note: `Auto-approved: P(win) ${Math.round(it.pWin * 100)}% ≥ ${Math.round(au.autoApproveMinP * 100)}%, all checks pass` }; auto++; log(`⚡ ${it.symbol}: auto-approved by autonomy rule`); audit('system', 'Ticket auto-approved', `${it.symbol} ${it.side} ${it.strategy} ${it.tf} (P ${it.pWin})`); }
        }
      }
      if (auto) log(`${auto} ticket(s) auto-approved (autonomy: ${au.level})`);
      for (const it of S.plan.items) {
        if (it.status === 'approved' || it.status === 'modified') {
          if (r() < 0.12) { it.status = 'invalidated'; it.statusNote = 'Gap beyond 0.5 ATR at pre-open'; inval++; log(`✗ ${it.symbol}: invalidated (gap at open)`); }
          else { it.status = 'armed'; it.armedAt = Date.now(); armed++; BT.ltpMap[it.symbol] = BT.ltp(it.symbol); log(`✓ ${it.symbol}: armed — watching trigger ${it.trigger}`); }
        } else if (it.status === 'proposed' && !it.reserve) {
          if (S.settings.unreviewedPolicy === 'lapse') { it.status = 'lapsed'; lapsed++; log(`… ${it.symbol}: not reviewed → lapsed (not armed)`); }
        }
      }
      log(`Armed ${armed}, invalidated ${inval}, lapsed ${lapsed}. Live feed subscriptions updated.`);
      if (armed) notify(`${armed} ticket(s) armed — watching triggers live`, 'good', '#/today');
      if (auto) notify(`${auto} ticket(s) were auto-approved by your autonomy rule`, 'info', '#/review');
      if (lapsed) notify(`${lapsed} unreviewed ticket(s) lapsed at cutoff`, 'warn', '#/review');
    },
    'eod-fetch': (log) => {
      const syms = Object.keys(S.tracked);
      log(`Data account: ${BT.dataAccount() ? BT.dataAccount().name : 'NONE'} — fetching 1-minute + daily for ${syms.length} tracked instruments`);
      for (const s of syms) { const t = S.tracked[s]; t.dataTo = D.DAYS[D.DAYS.length - 1]; if (t.status !== 'ready') { t.status = 'ready'; t.progress = 100; log(`Backfill completed: ${s}`); } }
      log('Aggregates refreshed: 5m, 10m, 15m, 30m, 1h, 1D, 1W, 1M');
      refreshRuleBaskets();
      log('Rule-based baskets re-evaluated');
    },
    'portfolio-sync': (log) => {
      for (const a of BT.accountsWith('portfolio')) { a.lastSync = Date.now(); if (a.token.status !== 'valid') { a.token = { status: 'valid', expiresAt: Date.now() + 24 * 3600000 }; log(`${a.name}: token refreshed (API key & secret flow)`); } log(`${a.name}: holdings, positions, trade history, ledger synced`); }
      log('Fills matched to plan tickets (auto-journal); daily valuation snapshot stored');
    },
    'nightly': (log) => {
      const forDate = D.iso(D.nextTradingDay());
      log('Grading previous plan · updating cell statistics · drift checks');
      log('Scanning trading baskets: ' + S.baskets.filter((b) => b.purpose === 'trading').map((b) => b.name).join(', '));
      S.plan = D.proposePlan(S, forDate);
      const g = S.plan.gate;
      log(`Smart rules: market gate ${g.status} · risk ×${S.plan.throttle.riskMult} · ${S.plan.items.filter((i) => i.filtered).length} filtered (meta-model / correlation) · learned entry windows applied`);
      log(`Proposed plan for ${forDate}: ${S.plan.items.filter((i) => !i.reserve).length} tickets + ${S.plan.items.filter((i) => i.reserve).length} reserve — awaiting your review`);
      if (g.status === 'no-trade') notify(`No-trade day tomorrow (${g.factors.filter((f) => f.level === 'no-trade').map((f) => f.label).join(', ')}) — no tickets proposed`, 'warn', '#/review');
      notify(`New proposed plan for ${forDate} is ready for review`, 'info', '#/review');
    },
    'suggestions': (log) => {
      const fresh = D.seedSuggestions(S).filter((s) => !S.suggestions.some((x) => x.status === 'pending' && x.type === s.type && x.symbol === s.symbol));
      S.suggestions.unshift(...fresh);
      log(`${fresh.length} new suggestion(s) posted to the inbox`);
      if (fresh.length) notify(`${fresh.length} new AI suggestion(s) to review`, 'info', '#/suggestions');
    },
    'live-feed': (log) => { S.settings.demoMarketOpen = !S.settings.demoMarketOpen; log(S.settings.demoMarketOpen ? 'Connected to Dhan WebSocket (demo), subscribed active set' : 'Disconnected (market closed)'); },
    'holdings-refresh': (log) => { holdingsTick(true); log('Quotes refreshed for ' + liveSets().poll.length + ' holdings'); },
    'csv-watcher': (log) => log('Inbox empty — nothing to import'),
    'retrain': (log) => {
      log('Training ML.NET LightGBM per style × side … calibration (PAV) … OOS AUC 0.61');
      log('Meta-model retrained on graded trades (fills) · entry windows re-learned · loss tags refreshed');
      log('Exit re-selection: 2 cells changed (see learning changelog)');
      log('Weekly learning digest prepared');
      notify('Your weekly learning digest is ready', 'info', '#/learning?digest=1');
    },
    'backup': (log) => log('pg_dump 1.8 GB · restic snapshot OK · retention 30 daily / 12 monthly'),
    'validation': (log) => { const r = BT.runValidation ? BT.runValidation() : null; if (r) { log(`Checked ${r.n} instruments × ${r.days} days: ${r.pass} pass, ${r.warn} warning, ${r.fail} fail`); if (r.fail) notify(`Data validation: ${r.fail} instrument(s) failed — see Data quality`, 'warn', '#/quality'); } else log('Validation module not loaded'); },
    'alerts': (log) => { const n = BT.evaluateAlerts ? BT.evaluateAlerts(true) : 0; log(`Alerts evaluated · ${n} triggered`); },
  };
  BT.runService = function (id, trigger = 'manual') {
    const svc = S.services.find((s) => s.id === id);
    if (!svc || svc.status === 'running') return;
    svc.status = 'running';
    const lines = [];
    const log = (m) => lines.push(`[${fmtTime(Date.now())}] ${m}`);
    log(`Started (${trigger}) as service identity on ${svc.runsAs} · retries ${svc.retries} · timeout ${svc.timeoutMin} min`);
    if (trigger !== 'catch-up') renderIfRoute('services');
    const finish = () => {
      try { (EFFECTS[id] || (() => log('Done')))(log); svc.lastOutcome = 'success'; }
      catch (e) { log('ERROR ' + e.message); svc.lastOutcome = 'failed'; if (svc.notifyOnFailure) notify(`${svc.name} failed: ${e.message}`, 'warn', '#/services'); }
      log('Finished');
      svc.status = 'idle'; svc.lastRun = Date.now();
      svc.history.unshift({ at: Date.now(), trigger, outcome: svc.lastOutcome, log: lines.slice() });
      if (svc.history.length > 30) svc.history.length = 30;
      audit('system', `Service run: ${svc.name}`, `${trigger} · ${svc.lastOutcome}`);
      save();
      renderTopStatus();
      if (trigger !== 'catch-up') { toast(`${svc.name}: ${svc.lastOutcome}`, svc.lastOutcome === 'success' ? 'good' : 'bad'); BT.rerender(); }
      if (svc.lastOutcome === 'success') S.services.filter((x) => x.enabled && x.sched.type === 'after' && x.sched.after === id).forEach((x) => BT.runService(x.id, trigger === 'catch-up' ? 'catch-up' : 'after ' + svc.id));
    };
    if (trigger === 'catch-up') finish(); else setTimeout(finish, 1200 + Math.random() * 1000);
  };
  // Runs scheduled services whose time passed while the machine/app was not running
  function catchUp() {
    const ran = [];
    const since = S.lastSeen || Date.now();
    for (const svc of S.services) {
      if (!svc.enabled || !svc.catchUp) continue;
      const occ = lastOccurrence(svc.sched);
      if (occ && occ > svc.lastRun && occ > since - 7 * 86400000) { BT.runService(svc.id, 'catch-up'); ran.push(svc.name); }
    }
    S.lastSeen = Date.now();
    return ran;
  }
  function scheduler() {
    for (const svc of S.services) {
      if (!svc.enabled) continue;
      const occ = lastOccurrence(svc.sched);
      if (occ && occ > svc.lastRun && Date.now() - occ < 120000) BT.runService(svc.id, 'schedule');
    }
  }

  /* ---------------- live engine (simulated Dhan feed) ---------------- */
  function liveSets() {
    const pos = BT.positions().filter((p) => p.status === 'open');
    const tickets = S.plan.items.filter((i) => i.status === 'armed' || i.status === 'triggered');
    const ws = new Set(['NIFTY 50', 'NIFTY BANK', 'INDIA VIX']);
    tickets.forEach((t) => ws.add(t.symbol));
    pos.filter((p) => p.bucket === 'Trading').forEach((p) => ws.add(p.symbol));
    (S.settings.pinnedBaskets || []).forEach((id) => { const b = S.baskets.find((x) => x.id === id); if (b) b.members.forEach((m) => ws.add(m)); });
    const poll = [...new Set(pos.filter((p) => p.bucket !== 'Trading').map((p) => p.symbol).concat(S.alerts.filter((a) => a.active).map((a) => a.symbol)))].filter((s) => !ws.has(s));
    return { ws: [...ws], poll, tickets };
  }
  BT.liveSets = liveSets;
  BT.lastTick = 0;
  function liveTick() {
    if (!S.settings.demoMarketOpen) return;
    const { ws, tickets } = liveSets();
    const speed = S.settings.simSpeed || 1;
    for (const sym of ws) {
      const p = BT.ltp(sym);
      const t = tickets.find((x) => x.symbol === sym && (x.status === 'armed' || x.status === 'triggered'));
      let bias = 0;
      if (t) { const goal = t.status === 'armed' ? t.trigger : (Math.random() < 0.5 ? t.target : t.stop); bias = (goal - p) / p * 0.06 * speed; }
      const vol = sym === 'INDIA VIX' ? 0.004 : 0.0011;
      const next = p * (1 + bias + vol * Math.sqrt(speed) * (Math.random() * 2 - 1));
      BT.ltpMap[sym] = Math.round(next * (sym === 'INDIA VIX' || D.BY_SYMBOL[sym].type === 'IDX' ? 100 : 20)) / (sym === 'INDIA VIX' || D.BY_SYMBOL[sym].type === 'IDX' ? 100 : 20);
    }
    for (const t of tickets) handleTicket(t);
    BT.evaluateAlerts();
    BT.lastTick = Date.now();
    BT.onTick && BT.onTick();
  }
  // simulated broker fill: plan price ± slippage (larger on stop exits and on less liquid names)
  function fillPrice(t, side, price, kind) {
    const inst = D.BY_SYMBOL[t.symbol];
    const ticks = (kind === 'stop' ? 3 : 1) + ((inst.turnoverCr || 100) < 150 ? 2 : 0) + Math.floor(Math.random() * 2);
    return Math.round((price + (side === 'BUY' ? 1 : -1) * ticks * 0.05) * 20) / 20;
  }
  function placeTx(t, side, price, note) {
    for (const a of t.alloc) {
      const acc = S.accounts.find((x) => x.id === a.account);
      if (!acc || !acc.roles.trading.enabled) continue;
      S.transactions.push({ id: uid('TX'), account: a.account, symbol: t.symbol, side, date: D.iso(new Date()), t: Date.now(), qty: a.qty, price, charges: Math.max(20, Math.round(a.qty * price * 0.0006)), bucket: 'Trading', source: acc.roles.trading.mode === 'paper' ? 'paper' : 'broker', planItem: t.id, note });
    }
  }
  function handleTicket(t) {
    const p = BT.ltp(t.symbol);
    const long = t.side === 'BUY';
    if (t.status === 'armed') {
      if ((long && p >= t.trigger) || (!long && p <= t.trigger)) {
        t.status = 'triggered'; t.entry = t.trigger; t.triggeredAt = Date.now();
        t.fillEntry = fillPrice(t, t.side, t.trigger, 'entry');
        placeTx(t, t.side, t.fillEntry, 'entry');
        audit('system', 'Ticket triggered', `${t.symbol} ${t.side} @ ${t.trigger} (${t.strategy} ${t.tf})`);
        notify(`${t.symbol} ${t.side} triggered at ${num(t.trigger)} — position opened`, 'good', '#/today');
        save();
      }
    } else if (t.status === 'triggered') {
      const hitStop = long ? p <= t.stop : p >= t.stop;
      const hitTarget = long ? p >= t.target : p <= t.target;
      if (hitStop || hitTarget) {
        const exit = hitTarget ? t.target : t.stop;
        t.status = 'closed'; t.exit = exit; t.closedAt = Date.now();
        const risk = Math.abs(t.entry - t.stop);
        t.fillExit = fillPrice(t, long ? 'SELL' : 'BUY', exit, hitTarget ? 'target' : 'stop');
        t.planR = Math.round(((exit - t.entry) * (long ? 1 : -1) / risk) * 100) / 100;
        t.resultR = Math.round(((t.fillExit - (t.fillEntry ?? t.entry)) * (long ? 1 : -1) / risk) * 100) / 100; // graded on fills
        if (!hitTarget) t.tags = [Date.now() - t.triggeredAt < 5 * 60000 ? 'Stop inside noise' : 'Normal loss (variance)'].concat(t.resultR - t.planR < -0.08 ? ['High slippage'] : []);
        placeTx(t, long ? 'SELL' : 'BUY', t.fillExit, hitTarget ? 'target' : 'stop');
        audit('system', 'Ticket closed', `${t.symbol} ${hitTarget ? 'target' : 'stop'} @ ${exit}, filled ${t.fillExit} → ${t.resultR}R on fills (plan ${t.planR}R)`);
        notify(`${t.symbol} ${hitTarget ? 'target hit' : 'stopped out'} at ${num(exit)} (${t.resultR > 0 ? '+' : ''}${t.resultR}R)`, hitTarget ? 'good' : 'warn', '#/today');
        save();
      }
    }
  }
  function holdingsTick(force) {
    if (!S.settings.demoMarketOpen && !force) return;
    for (const sym of liveSets().poll) { const p = BT.ltp(sym); BT.ltpMap[sym] = Math.round(p * (1 + 0.003 * (Math.random() * 2 - 1)) * 20) / 20; }
    BT.lastPoll = Date.now();
    BT.evaluateAlerts();
  }

  /* ---------------- shell & router ---------------- */
  const NAV = [
    ['Overview', [['today', 'Today']]],
    ['Decisions', [['review', 'Plan review'], ['suggestions', 'Suggestions']]],
    ['Portfolio', [['portfolio', 'Portfolio']]],
    ['Market', [['instruments', 'Instruments'], ['baskets', 'Baskets'], ['data', 'Data & import'], ['quality', 'Data quality']]],
    ['Research', [['learning', 'Learning'], ['reports', 'Reports'], ['lab', 'Strategy Lab']]],
    ['System', [['accounts', 'Accounts'], ['services', 'Services'], ['settings', 'Settings'], ['audit', 'Audit log']]],
  ];
  const TITLES = Object.fromEntries(NAV.flatMap(([, items]) => items.map(([id, t]) => [id, t])));
  BT.NAV = NAV; BT.TITLES = TITLES;
  function badges() {
    return {
      review: S.plan.items.filter((i) => i.status === 'proposed' && !i.reserve).length,
      suggestions: S.suggestions.filter((s) => s.status === 'pending').length,
    };
  }
  function shell() {
    const b = badges();
    $('#app').innerHTML = `<div class="shell">
      <nav class="side" id="side" aria-label="Main">
        <div class="brand" title="xDrishti — दृष्टि, vision">${BT.brandMark()}<b class="lbl"><span>x</span>Drishti</b></div>
        ${NAV.map(([sec, items]) => `<div class="sec">${sec}</div>${items.map(([id, t]) => `<a href="#/${id}" data-nav="${id}" title="${t}">${BT.navIcon(id)}<span class="lbl">${t}</span>${b[id] ? `<span class="badge">${b[id]}</span>` : ''}</a>`).join('')}`).join('')}
        <button class="btn ghost sm side-toggle" id="side-toggle" title="Collapse / expand sidebar">${BT.icon('chevronsLeft')} <span class="lbl">Collapse</span></button>
        <div class="foot lbl">Prototype · demo data · no backend<br>Services are simulated in this browser.</div>
      </nav>
      <div class="main">
        <header class="top">
          <button class="btn icon" id="menu-toggle" aria-label="Menu">${BT.icon('menu')}</button>
          <h2 id="page-title"></h2>
          <button class="search-btn" id="cmdk" aria-label="Search or jump to (Ctrl/⌘ K)" title="Search or jump to">${BT.icon('search')}<span class="hide-sm">Search or jump to…</span><kbd class="hide-sm">⌘K</kbd></button>
          <div class="status" id="top-status"></div>
          <button class="btn icon" id="bell" aria-label="Notifications" title="Notifications">${BT.icon('bell')}</button>
          <button class="btn icon" id="theme" aria-label="Toggle dark mode" title="Toggle dark mode">${BT.icon(isDark() ? 'sun' : 'moon')}</button>
          <button class="btn user-btn" id="user-btn"><span class="avatar">O</span><span class="hide-sm">owner</span>${BT.icon('chevronDown')}</button>
        </header>
        <div id="page" class="page"></div>
      </div>
    </div>`;
    $('#menu-toggle').onclick = () => document.body.classList.toggle('nav-open');
    $('#side-toggle').onclick = () => {
      if (innerWidth <= 1200) { document.body.classList.toggle('side-expanded'); BT.rerender(); return; } // medium screens auto-compact; this expands temporarily
      const c = !document.body.classList.contains('side-compact'); document.body.classList.toggle('side-compact', c);
      try { localStorage.setItem('xd-proto-side', c ? 'compact' : 'full'); } catch (e) { /* ignore */ } BT.rerender();
    };
    $('#theme').onclick = () => { const t = isDark() ? 'light' : 'dark'; document.documentElement.dataset.theme = t; $('#theme').innerHTML = BT.icon(t === 'dark' ? 'sun' : 'moon'); try { localStorage.setItem('xd-proto-theme', t); } catch (e) { /* ignore */ } BT.rerender(); };
    $('#bell').onclick = openNotifications;
    $('#cmdk').onclick = () => BT.palette && BT.palette();
    $('#user-btn').onclick = openUserMenu;
    renderTopStatus();
  }
  function renderTopStatus() {
    const el = $('#top-status');
    if (!el) return;
    const da = BT.dataAccount();
    const unread = S.notifications.filter((n) => !n.read).length;
    el.innerHTML = `<span class="pill hide-sm"><span class="dot ${S.settings.demoMarketOpen ? 'green pulse' : ''}"></span> Market <b>${S.settings.demoMarketOpen ? 'open (demo)' : 'closed'}</b></span>
      <span class="pill hide-sm hide-md">${BT.icon('database')} <b>${da ? esc(da.name) : '⚠ none'}</b></span>
      <span class="pill hide-sm">NIFTY <b class="num">${num(BT.ltp('NIFTY 50'))}</b> <span class="${sgn(BT.dayChange('NIFTY 50'))}">${pct(BT.dayChange('NIFTY 50'), 2)}</span></span>`;
    const bell = $('#bell'); if (bell) bell.innerHTML = BT.icon('bell') + (unread ? `<span class="badge bell-badge">${unread}</span>` : '');
    const b = badges();
    $$('[data-nav]').forEach((a) => { const id = a.dataset.nav; const old = a.querySelector('.badge'); if (old) old.remove(); if (b[id]) a.insertAdjacentHTML('beforeend', `<span class="badge">${b[id]}</span>`); });
  }
  BT.renderTopStatus = renderTopStatus;
  function openNotifications() {
    const list = S.notifications.slice(0, 30);
    drawer({ title: 'Notifications', body: list.length ? `<div class="card"><div class="card-b flush">${list.map((n) => `<div class="sugg" style="grid-template-columns:1fr auto"><div><div>${esc(n.text)}</div><div class="small muted">${fmtDT(n.at)}</div></div>${n.link ? `<a class="btn sm" href="${n.link}" data-close>Open</a>` : ''}</div>`).join('')}</div></div>` : '<div class="empty">No notifications yet.</div>' });
    S.notifications.forEach((n) => (n.read = true)); save(); renderTopStatus();
  }
  function openUserMenu() {
    modal({
      title: 'Signed in as owner',
      body: `<div class="kv"><dt>Session</dt><dd>Idle timeout ${S.settings.idleTimeoutMin} min · this browser tab only</dd><dt>Last activity</dt><dd>${fmtTime((session() || {}).last || Date.now())}</dd></div>
        <div class="banner info">Locking or signing out does not stop anything: services keep running on the machine under the service identity.</div>`,
      foot: `<button class="btn bad" id="um-reset">Reset demo data</button><span class="spacer"></span><button class="btn" id="um-lock">Lock</button><button class="btn primary" id="um-out">Sign out</button>`,
      onMount: (ov, close) => {
        $('#um-lock', ov).onclick = () => { close(); BT.logout('You locked the session. Services keep running in the background.'); };
        $('#um-out', ov).onclick = () => { close(); BT.logout(); };
        $('#um-reset', ov).onclick = async () => { close(); if (await confirmDlg('Reset demo data', 'This restores the original demo accounts, baskets, plan and portfolio in this browser.', 'Reset', true)) reset(); };
      },
    });
  }

  let current = null;
  function route() {
    if (!BT.isAuthed()) { const ss = session(); ss && ss.locked ? showLogin('Session locked. Services keep running in the background.', 'lock') : ss ? showLogin('Your session was locked after ' + S.settings.idleTimeoutMin + ' minutes of inactivity.', 'lock') : showLogin(''); return; }
    if (!$('.shell')) shell();
    const id = (location.hash.replace(/^#\//, '').split('?')[0]) || 'today';
    const page = BT.pages[id] ? id : 'today';
    current = page;
    BT.disposeCharts();
    BT.onTick = null;
    document.body.classList.remove('nav-open');
    $$('[data-nav]').forEach((a) => a.classList.toggle('active', a.dataset.nav === page));
    $('#page-title').textContent = TITLES[page] || '';
    document.title = (TITLES[page] || 'xDrishti') + ' · xDrishti';
    const el = $('#page');
    BT._enterAt = performance.now();
    el.classList.remove('enter'); void el.offsetWidth; el.classList.add('enter');
    clearTimeout(BT._enterT); BT._enterT = setTimeout(() => el.classList.remove('enter'), 900);
    BT.pages[page](el);
    renderTopStatus();
    window.scrollTo(0, 0);
  }
  BT.rerender = () => { if (current && BT.isAuthed() && $('#page')) { const y = window.scrollY; BT.disposeCharts(); BT.onTick = null; BT.pages[current]($('#page')); renderTopStatus(); window.scrollTo(0, y); } };
  function renderIfRoute(id) { if (current === id) BT.rerender(); }
  BT.go = (h) => { location.hash = h; };

  function start() { route(); }

  /* ---------------- boot ---------------- */
  function boot() {
    try { const t = localStorage.getItem('xd-proto-theme'); document.documentElement.dataset.theme = t || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'); } catch (e) { /* ignore */ }
    try { if (localStorage.getItem('xd-proto-side') === 'compact') document.body.classList.add('side-compact'); } catch (e) { /* ignore */ }
    load();
    addEventListener('hashchange', route);
    ['click', 'keydown', 'pointermove'].forEach((ev) => addEventListener(ev, () => { const s = session(); if (s && s.ok && Date.now() - s.last > 5000) BT.touch(); }, { passive: true }));
    setInterval(() => { if (session() && session().ok && !BT.isAuthed()) BT.logout('Session locked after ' + S.settings.idleTimeoutMin + ' minutes of inactivity.'); }, 15000);
    setInterval(() => { liveTick(); }, 1500);
    setInterval(() => { holdingsTick(); }, 15000); // demo: compressed from the configured interval
    setInterval(() => { scheduler(); progressBackfills(); }, 1000);
    setInterval(() => { save(); }, 10000);
    addEventListener('beforeunload', () => { S.lastSeen = Date.now(); save(); });
    route();
  }
  function progressBackfills() {
    let changed = false;
    for (const [sym, t] of Object.entries(S.tracked)) {
      if (t.status === 'backfilling') {
        t.progress = Math.min(100, (t.progress || 0) + 9 + Math.random() * 8);
        if (t.progress >= 100) { t.status = 'ready'; t.dataFrom = D.DAYS[0]; t.dataTo = D.DAYS[D.DAYS.length - 1]; notify(`Backfill finished: ${sym} — all timeframes ready`, 'good', '#/instruments'); audit('system', 'Backfill completed', `${sym}: 1-minute history + aggregates`); refreshRuleBaskets(); save(); }
        changed = true;
      }
    }
    if (changed && (current === 'instruments' || current === 'baskets' || current === 'data')) BT.onBackfill && BT.onBackfill();
  }
  BT.track = function (sym, actor = 'owner') {
    if (S.tracked[sym]) return false;
    S.tracked[sym] = { since: D.iso(new Date()), dataFrom: null, dataTo: null, status: 'backfilling', progress: 0 };
    audit(actor, 'Track instrument', `${sym} → backfill job queued (CSV first, then Dhan gaps)`);
    save();
    return true;
  };
  BT.untrack = function (sym) {
    delete S.tracked[sym];
    audit('owner', 'Untrack instrument', `${sym} — collection stopped; history kept`);
    refreshRuleBaskets(); save();
  };
  document.addEventListener('DOMContentLoaded', boot);
})();
