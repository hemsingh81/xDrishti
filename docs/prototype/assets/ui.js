/* xDrishti prototype — UI kit: colourful SVG icons, automatic decoration (tiles, card headers, page heads),
   collapsible cards, count-up numbers and enter/exit animations. Loaded after app-core.js. */
(function () {
  'use strict';
  const BT = window.BT;

  /* ---------------- icon set (24×24 stroke icons, currentColor) ---------------- */
  const P = {
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
    checkCircle: '<circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
    sparkles: '<path d="M11 3l1.8 4.7L17.5 9.5l-4.7 1.8L11 16l-1.8-4.7L4.5 9.5l4.7-1.8z"/><path d="M18.5 14.5l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8z"/>',
    pie: '<path d="M21 12.5A9 9 0 1 1 11.5 3v9.5z"/><path d="M14.5 3.2A9 9 0 0 1 20.8 9.5h-6.3z"/>',
    list: '<path d="M8 6h13M8 12h13M8 18h13"/><circle cx="3.5" cy="6" r=".8"/><circle cx="3.5" cy="12" r=".8"/><circle cx="3.5" cy="18" r=".8"/>',
    basket: '<path d="M3 10h18l-2 10H5z"/><path d="m8 10 4-6 4 6M9 14v3M15 14v3M12 14v3"/>',
    database: '<ellipse cx="12" cy="5.5" rx="8" ry="2.8"/><path d="M4 5.5v6.5c0 1.6 3.6 2.8 8 2.8s8-1.2 8-2.8V5.5M4 12v6.5c0 1.6 3.6 2.8 8 2.8s8-1.2 8-2.8V12"/>',
    shieldCheck: '<path d="M12 3l8 3v6c0 4.8-3.4 8-8 9-4.6-1-8-4.2-8-9V6z"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
    barChart: '<path d="M3 21h18M6 17v-6M11 17V6M16 17v-9M21 17V3"/>',
    flask: '<path d="M9 3h6M10 3v6L4.6 18.4A1.8 1.8 0 0 0 6.2 21h11.6a1.8 1.8 0 0 0 1.6-2.6L14 9V3"/><path d="M7 15h10"/>',
    brain: '<path d="M9.5 3.5A3 3 0 0 0 6.6 6 3 3 0 0 0 4 9.5a3 3 0 0 0 .6 4.4A3 3 0 0 0 7 18.5a3 3 0 0 0 5 1.5V5a2.5 2.5 0 0 0-2.5-1.5z"/><path d="M14.5 3.5A3 3 0 0 1 17.4 6 3 3 0 0 1 20 9.5a3 3 0 0 1-.6 4.4 3 3 0 0 1-2.4 4.6 3 3 0 0 1-5 1.5"/><path d="M12 9h-2M12 13h2"/>',
    users: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M18 14.2a6.4 6.4 0 0 1 3.5 5.8"/>',
    server: '<rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01M11 7.5h6M11 16.5h6"/>',
    sliders: '<path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1"/><circle cx="15" cy="6" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="18" r="2"/>',
    history: '<path d="M3 12a9 9 0 1 0 2.7-6.4L3 8"/><path d="M3 3v5h5M12 7v5l3 2"/>',
    bell: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.9 1.9 0 0 0 3.4 0"/>',
    moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    user: '<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>',
    menu: '<path d="M3 6h18M3 12h18M3 18h18"/>',
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    chevronDown: '<path d="m6 9 6 6 6-6"/>',
    chevronsLeft: '<path d="m11 17-5-5 5-5M18 17l-5-5 5-5"/>',
    trendUp: '<path d="m3 17 6-6 4 4 8-8"/><path d="M15 7h6v6"/>',
    trendDown: '<path d="m3 7 6 6 4-4 8 8"/><path d="M15 17h6v-6"/>',
    wallet: '<path d="M20 7V5.5A1.5 1.5 0 0 0 18.5 4H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v3h-4a2 2 0 0 0 0 4h4v3a1 1 0 0 1-1 1H5a2 2 0 0 1-2-2V6"/>',
    percent: '<path d="M19 5 5 19"/><circle cx="6.5" cy="6.5" r="2.5"/><circle cx="17.5" cy="17.5" r="2.5"/>',
    activity: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
    target: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1"/>',
    alert: '<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>',
    xCircle: '<circle cx="12" cy="12" r="9"/><path d="m15 9-6 6M9 9l6 6"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    zap: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
    layers: '<path d="M12 2 2 7l10 5 10-5z"/><path d="m2 17 10 5 10-5M2 12l10 5 10-5"/>',
    coins: '<circle cx="9" cy="9" r="6"/><path d="M18.1 10.4A6 6 0 1 1 10.4 18.1M7 7h2v4"/>',
    clipboard: '<rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/>',
    lightbulb: '<path d="M9 18h6M10 22h4M12 2a7 7 0 0 0-4 12.7c.6.5 1 1.2 1 2V17h6v-.3c0-.8.4-1.5 1-2A7 7 0 0 0 12 2z"/>',
    lineChart: '<path d="M3 3v18h18"/><path d="m7 15 4-5 3 3 5-6"/>',
    gauge: '<path d="M12 14l4-4"/><path d="M3.3 19a10 10 0 1 1 17.4 0"/>',
    key: '<circle cx="7.5" cy="15.5" r="4.5"/><path d="m10.7 12.3 9.3-9.3M17 6l3 3M14.5 8.5l2 2"/>',
    calendar: '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
    upload: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12"/>',
    grid: '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
    shield: '<path d="M12 3l8 3v6c0 4.8-3.4 8-8 9-4.6-1-8-4.2-8-9V6z"/>',
    sigma: '<path d="M18 7V4H6l6 8-6 8h12v-3"/>',
    scale: '<path d="M12 3v18M7 21h10M5 7h14M5 7l-3 7a3 3 0 0 0 6 0zM19 7l-3 7a3 3 0 0 0 6 0z"/>',
    radio: '<circle cx="12" cy="12" r="2"/><path d="M16.2 7.8a6 6 0 0 1 0 8.4M7.8 16.2a6 6 0 0 1 0-8.4M19.1 4.9a10 10 0 0 1 0 14.2M4.9 19.1a10 10 0 0 1 0-14.2"/>',
    git: '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="8" r="2.5"/><path d="M6 8.5v7M18 10.5c0 4-6 3-10.5 6"/>',
    rocket: '<path d="M4.5 16.5c-1.5 1.3-2 5-2 5s3.7-.5 5-2c.7-.8.7-2.1-.1-2.9a2.2 2.2 0 0 0-2.9-.1z"/><path d="M12 15l-3-3a22 22 0 0 1 2-3.9A12.9 12.9 0 0 1 22 2c0 2.7-.8 7.5-6 11a22.4 22.4 0 0 1-4 2z"/><path d="M9 12H4s.6-3 2-4c1.6-1.1 5 0 5 0M12 15v5s3-.6 4-2c1.1-1.6 0-5 0-5"/>',
    play: '<path d="M6 4l14 8-14 8z"/>',
    refresh: '<path d="M21 12a9 9 0 0 1-15.5 6.2L3 16M3 12a9 9 0 0 1 15.5-6.2L21 8"/><path d="M21 3v5h-5M3 21v-5h5"/>',
    link: '<path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/>',
    lock: '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    award: '<circle cx="12" cy="9" r="6"/><path d="M8.2 13.8 7 22l5-3 5 3-1.2-8.2"/>',
    flag: '<path d="M4 22V4M4 15s1-1 4-1 5 2 8 2 4-1 4-1V4s-1 1-4 1-5-2-8-2-4 1-4 1"/>',
    eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    bolt: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
    dot: '<circle cx="12" cy="12" r="4"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
    command: '<path d="M15 6v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3V6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3"/>',
    keyboard: '<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10"/>',
  };
  BT.icon = (name, cls = '') => `<svg class="ic ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${P[name] || P.dot}</svg>`;

  /* ---------------- navigation colours ---------------- */
  const NAVSTYLE = {
    today: ['sun', '#f59e0b', '#f97316'], review: ['checkCircle', '#10b981', '#059669'], suggestions: ['sparkles', '#d946ef', '#8b5cf6'],
    portfolio: ['pie', '#38bdf8', '#3b82f6'], instruments: ['list', '#818cf8', '#4f46e5'], baskets: ['basket', '#fb923c', '#f43f5e'],
    data: ['database', '#22d3ee', '#0d9488'], quality: ['shieldCheck', '#a3e635', '#16a34a'], reports: ['barChart', '#c4b5fd', '#7c3aed'],
    lab: ['flask', '#f472b6', '#db2777'], learning: ['brain', '#2dd4bf', '#6366f1'], accounts: ['users', '#60a5fa', '#2563eb'],
    services: ['server', '#38bdf8', '#6366f1'], settings: ['sliders', '#a1a1aa', '#52525b'], audit: ['history', '#fbbf24', '#b45309'],
  };
  BT.NAVSTYLE = NAVSTYLE;
  // brand mark: an eye (drishti = vision) with an x-shaped glint
  BT.brandMark = (cls = '') => `<span class="brand-mark ${cls}" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3.2"/><path d="m17.5 3.5 2 2m0-2-2 2" stroke-width="1.6"/></svg></span>`;
  BT.navIcon = (id, cls = '') => { const [ic, a, b] = NAVSTYLE[id] || ['dot', '#94a3b8', '#64748b']; return `<span class="nav-ic ${cls}" style="--c1:${a};--c2:${b}">${BT.icon(ic)}</span>`; };

  /* ---------------- keyword → icon/colour for tiles and card headers ---------------- */
  const C = { indigo: ['#818cf8', '#4f46e5'], green: ['#34d399', '#059669'], amber: ['#fbbf24', '#d97706'], rose: ['#fb7185', '#e11d48'],
    sky: ['#38bdf8', '#0284c7'], violet: ['#c084fc', '#7c3aed'], teal: ['#2dd4bf', '#0d9488'], pink: ['#f472b6', '#db2777'], slate: ['#94a3b8', '#475569'] };
  const ROT = ['indigo', 'teal', 'amber', 'violet', 'sky', 'pink', 'green', 'rose'];
  const TILE_RULES = [
    [/fail|drawdown|loss|reject/, 'xCircle', 'rose'], [/warn/, 'alert', 'amber'], [/^pass|healthy/, 'checkCircle', 'green'],
    [/xirr|return|%|rate/, 'percent', 'violet'], [/expectancy|exp\b|edge/, 'sigma', 'violet'], [/win/, 'target', 'green'],
    [/p&l|pnl|profit|gain|unrealised|realised/, 'trendUp', 'green'], [/day change|day\b/, 'activity', 'sky'],
    [/value|worth|holdings/, 'wallet', 'indigo'], [/invested|cost|fund|capital|cash/, 'coins', 'amber'],
    [/calibration|brier|ece/, 'target', 'teal'], [/slippage|execution|gap/, 'gauge', 'amber'], [/model|learn|lesson|version/, 'brain', 'teal'],
    [/auto|autonom/, 'bolt', 'pink'], [/live/, 'radio', 'rose'], [/plan|ticket/, 'clipboard', 'amber'], [/service/, 'server', 'sky'],
    [/quality|score/, 'shieldCheck', 'green'], [/instrument|checked|symbol/, 'list', 'indigo'], [/trade/, 'layers', 'indigo'],
    [/factor/, 'scale', 'amber'], [/cell|setup|strateg/, 'flask', 'pink'], [/account/, 'users', 'sky'], [/alert/, 'bell', 'amber'],
  ];
  const HEAD_RULES = [
    [/attention|alert|action center|notification/, 'bell', 'amber'], [/live|feed|stream/, 'radio', 'rose'], [/briefing|ai\b|suggest/, 'sparkles', 'violet'],
    [/lesson|learn|journey|model|what the system/, 'brain', 'teal'], [/experiment|re-run|replay|tweak/, 'git', 'pink'], [/autonom|guardrail/, 'bolt', 'pink'],
    [/execution|slippage|plan vs|gap/, 'gauge', 'amber'], [/calibration|reliab/, 'target', 'teal'], [/curve|equity|trend|over time|evolution/, 'lineChart', 'indigo'],
    [/sector|allocation|mix|split/, 'pie', 'sky'], [/schedule|timeline|calendar|weekday|hour/, 'calendar', 'amber'], [/account|role/, 'users', 'sky'],
    [/risk|profile|limit/, 'shield', 'rose'], [/basket/, 'basket', 'amber'], [/import|csv|file|upload/, 'upload', 'teal'], [/coverage|data|aggregat|bars|timeframe/, 'database', 'teal'],
    [/histor|audit|log|recent/, 'history', 'slate'], [/insight|idea/, 'lightbulb', 'amber'], [/strateg|setup|cell|lab/, 'flask', 'pink'], [/exit/, 'target', 'green'],
    [/decision|approv|check/, 'checkCircle', 'green'], [/pivot|heat|matrix/, 'grid', 'violet'], [/trade|position|holding/, 'layers', 'indigo'],
    [/service|run/, 'server', 'sky'], [/score|instrument/, 'list', 'indigo'], [/chart|distribution|compare|mae|mfe|report/, 'barChart', 'violet'],
    [/connection|token|key|credential/, 'key', 'amber'], [/setting|config/, 'sliders', 'slate'],
  ];
  const pick = (rules, text, i) => { for (const [re, ic, c] of rules) if (re.test(text)) return [ic, c]; return ['sparkles', ROT[i % ROT.length]]; };
  const style = (c) => `--c1:${C[c][0]};--c2:${C[c][1]}`;

  /* ---------------- count-up for numbers on page entry ---------------- */
  function countUp(el) {
    const txt = el.textContent;
    const m = txt.match(/^([^\d]*?)(\d[\d,]*(?:\.\d+)?)(.*)$/s);
    if (!m) return;
    const target = parseFloat(m[2].replace(/,/g, ''));
    if (!isFinite(target) || target === 0) return;
    const dec = (m[2].split('.')[1] || '').length;
    const fmt = (v) => m[1] + v.toLocaleString('en-IN', { minimumFractionDigits: dec, maximumFractionDigits: dec }) + m[3];
    const t0 = performance.now(), dur = 750;
    let last = fmt(0);
    el.textContent = last;
    const step = (t) => {
      if (el.textContent !== last) return; // updated live meanwhile — stop
      const k = Math.min(1, (t - t0) / dur), e = 1 - Math.pow(1 - k, 3);
      last = k < 1 ? fmt(target * e) : txt;
      el.textContent = last;
      if (k < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  /* ---------------- collapsible cards ---------------- */
  BT.mem.collapsed = BT.mem.collapsed || new Set();
  const cardKey = (card) => (location.hash || '#/today').split('?')[0] + '|' + ((card.querySelector(':scope > .card-h h3') || {}).textContent || '').trim().slice(0, 60);
  function bodiesOf(card) { return Array.from(card.children).filter((c) => !c.classList.contains('card-h')); }
  function setCollapsed(card, on, animate) {
    const bodies = bodiesOf(card);
    card.classList.toggle('collapsed', on);
    for (const b of bodies) {
      if (!animate || !b.animate) { b.style.display = on ? 'none' : ''; continue; }
      if (on) {
        const h = b.offsetHeight;
        b.style.overflow = 'hidden';
        b.animate([{ height: h + 'px', opacity: 1 }, { height: '0px', opacity: 0 }], { duration: 260, easing: 'cubic-bezier(.4,0,.2,1)' }).onfinish = () => { b.style.display = 'none'; b.style.overflow = ''; };
      } else {
        b.style.display = '';
        const h = b.scrollHeight;
        b.style.overflow = 'hidden';
        b.animate([{ height: '0px', opacity: 0 }, { height: h + 'px', opacity: 1 }], { duration: 300, easing: 'cubic-bezier(.2,.8,.2,1)' }).onfinish = () => { b.style.overflow = ''; window.dispatchEvent(new Event('resize')); };
      }
    }
  }

  /* ---------------- decoration (runs after every render via MutationObserver) ---------------- */
  function decorate(root) {
    const entering = BT._enterAt && performance.now() - BT._enterAt < 900;
    // tiles
    const tileSets = new Map();
    root.querySelectorAll('.tile:not([data-dec])').forEach((t) => {
      t.dataset.dec = '1';
      const parent = t.parentElement; const i = tileSets.get(parent) || 0; tileSets.set(parent, i + 1);
      const label = ((t.querySelector('.l') || {}).textContent || '').toLowerCase();
      const v = t.querySelector('.v');
      let [ic, c] = pick(TILE_RULES, label, i);
      if (v && v.classList.contains('neg') && c === 'green') { ic = ic === 'trendUp' ? 'trendDown' : ic; c = 'rose'; }
      t.setAttribute('style', (t.getAttribute('style') || '') + ';' + style(c));
      t.insertAdjacentHTML('afterbegin', `<span class="t-ico">${BT.icon(ic)}</span>`);
      if (entering && v) countUp(v);
    });
    // card headers
    let hi = 0;
    root.querySelectorAll('.card-h > h3:not([data-dec])').forEach((h) => {
      h.dataset.dec = '1';
      const [ic, c] = pick(HEAD_RULES, h.textContent.toLowerCase(), hi++);
      h.insertAdjacentHTML('afterbegin', `<span class="h-ico" style="${style(c)}">${BT.icon(ic)}</span>`);
      const card = h.closest('.card');
      if (card && card.closest('#page') && !card.dataset.nocollapse && bodiesOf(card).length) {
        h.insertAdjacentHTML('afterbegin', `<button class="btn ghost icon sm collapse-btn" title="Collapse / expand" aria-label="Collapse or expand">${BT.icon('chevronDown')}</button>`);
        if (BT.mem.collapsed.has(cardKey(card))) setCollapsed(card, true, false);
      }
    });
    // page head icon
    root.querySelectorAll('.page-head h1:not([data-dec])').forEach((h) => {
      h.dataset.dec = '1';
      const id = (location.hash.replace(/^#\//, '').split('?')[0]) || 'today';
      h.insertAdjacentHTML('afterbegin', BT.navIcon(NAVSTYLE[id] ? id : 'today', 'lg'));
    });
    // meters / progress bars grow in
    if (entering) root.querySelectorAll('.meter > i, .progress > i').forEach((b) => b.classList.add('grow'));
  }
  document.addEventListener('click', (e) => {
    const btn = e.target.closest('.collapse-btn');
    if (!btn) return;
    const card = btn.closest('.card');
    const on = !card.classList.contains('collapsed');
    setCollapsed(card, on, true);
    const k = cardKey(card); on ? BT.mem.collapsed.add(k) : BT.mem.collapsed.delete(k);
  });

  let queued = false;
  const run = () => { queued = false; decorate(document.body); };
  const mo = new MutationObserver(() => { if (!queued) { queued = true; requestAnimationFrame(run); } });
  // app-core boots on the same event (registered earlier), so decorate whatever it rendered first, then watch for changes
document.addEventListener('DOMContentLoaded', () => { mo.observe(document.body, { childList: true, subtree: true }); decorate(document.body); });

  /* ---------------- animated removal of overlays / toasts ---------------- */
  BT.animateOut = function (el, done) {
    if (!el || el.dataset.closing) return;
    el.dataset.closing = '1';
    el.classList.add('closing');
    setTimeout(() => { el.remove(); done && done(); }, 200);
  };
})();

/* ---------------- command palette (⌘K / Ctrl K / "/") and keyboard shortcuts ---------------- */
(function () {
  'use strict';
  const BT = window.BT, D = window.BTData;
  const esc = (s) => BT.esc(s);
  const authed = () => BT.isAuthed && BT.isAuthed() && document.querySelector('.shell');
  const typing = (e) => /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName) || e.target.isContentEditable;
  const GO = { t: 'today', r: 'review', s: 'suggestions', p: 'portfolio', i: 'instruments', b: 'baskets', d: 'data', q: 'quality', l: 'learning', e: 'reports', x: 'lab', a: 'accounts', v: 'services', c: 'settings', u: 'audit' };
  const KEY_OF = Object.fromEntries(Object.entries(GO).map(([k, v]) => [v, k]));

  function items() {
    const out = [];
    for (const [sec, list] of BT.NAV) for (const [id, t] of list) out.push({ group: 'Pages', label: t, hint: sec + (KEY_OF[id] ? ` · g ${KEY_OF[id]}` : ''), icon: BT.navIcon(id), run: () => { location.hash = '#/' + id; } });
    [['journey', 'Journey'], ['lessons', 'Lessons'], ['execution', 'Plan vs actual'], ['cells', 'Setups'], ['smart', 'Smart rules'], ['experiments', 'Tweak & re-run'], ['autonomy', 'Autonomy']]
      .forEach(([k, l]) => out.push({ group: 'Learning', label: 'Learning → ' + l, icon: BT.navIcon('learning'), run: () => { location.hash = '#/learning?tab=' + k + '&t=' + Date.now(); } }));
    const act = (label, icon, run, hint) => out.push({ group: 'Actions', label, hint, icon: `<span class="nav-ic" style="--c1:#a5b4fc;--c2:#4f46e5">${BT.icon(icon)}</span>`, run });
    act('Open weekly learning digest', 'sparkles', () => BT.openDigest && BT.openDigest());
    act('Run nightly pipeline now', 'play', () => { BT.runService('nightly'); BT.toast('Nightly pipeline started'); });
    act('Run pre-open check & arming now', 'zap', () => { BT.runService('preopen'); BT.toast('Pre-open check started'); });
    act('Toggle dark mode', 'moon', () => document.getElementById('theme').click(), 'theme');
    act('Show keyboard shortcuts', 'keyboard', () => shortcuts(), '?');
    act('Lock session', 'lock', () => BT.logout('You locked the session. Services keep running in the background.'));
    for (const b of BT.S.baskets) out.push({ group: 'Baskets', label: b.name, hint: `${b.members.length} instruments · ${b.purpose}`, icon: `<span class="nav-ic" style="--c1:${b.color};--c2:${b.color}">${BT.icon('basket')}</span>`, run: () => { location.hash = '#/baskets'; } });
    for (const i of D.ALL) out.push({ group: 'Instruments', label: i.symbol, hint: `${i.name} · ${i.sector}${BT.S.tracked[i.symbol] ? ' · tracked' : ''}`, icon: `<span class="nav-ic" style="--c1:#94a3b8;--c2:#475569">${BT.icon('list')}</span>`, run: () => BT.openInstrument && BT.openInstrument(i.symbol) });
    return out;
  }
  // subsequence match with bonuses for word starts and prefixes
  function score(q, text) {
    if (!q) return 1;
    const t = text.toLowerCase(); q = q.toLowerCase();
    if (t.startsWith(q)) return 100 - t.length / 100;
    const at = t.indexOf(q); if (at >= 0) return 60 - at;
    let i = 0, sc = 0, last = -2;
    for (let k = 0; k < t.length && i < q.length; k++) if (t[k] === q[i]) { sc += k === last + 1 ? 3 : (k === 0 || /[\s→·]/.test(t[k - 1])) ? 2 : 1; last = k; i++; }
    return i === q.length ? sc : 0;
  }
  BT.palette = function () {
    if (!authed() || document.querySelector('.palette')) return;
    const all = items();
    let sel = 0, shown = [];
    const ov = document.createElement('div');
    ov.className = 'overlay center palette-ov';
    ov.innerHTML = `<div class="palette" role="dialog" aria-modal="true" aria-label="Search or jump to">
      <div class="pal-in">${BT.icon('search')}<input id="pal-q" placeholder="Search pages, actions, instruments, baskets…" autocomplete="off" spellcheck="false" aria-controls="pal-list"><kbd>Esc</kbd></div>
      <div class="pal-list" id="pal-list" role="listbox"></div>
      <div class="pal-foot"><span><kbd>↑</kbd><kbd>↓</kbd> move</span><span><kbd>Enter</kbd> open</span><span><kbd>g</kbd> then a letter jumps to a page</span><span><kbd>?</kbd> shortcuts</span></div></div>`;
    document.body.appendChild(ov);
    const q = ov.querySelector('#pal-q'), list = ov.querySelector('#pal-list');
    const close = () => { document.removeEventListener('keydown', onKey, true); BT.animateOut(ov); };
    const render = () => {
      const v = q.value.trim();
      shown = all.map((x) => ({ x, s: Math.max(score(v, x.label), score(v, (x.hint || '')) * 0.6) })).filter((r) => r.s > 0)
        .sort((a, b) => b.s - a.s).slice(0, v ? 40 : 30).map((r) => r.x);
      if (!v) shown = shown.filter((x) => x.group !== 'Instruments'); // instruments appear once you type
      sel = Math.min(sel, Math.max(0, shown.length - 1));
      let g = '';
      list.innerHTML = shown.map((x, i) => `${x.group !== g ? `<div class="pal-g">${(g = x.group)}</div>` : ''}<div class="pal-item ${i === sel ? 'on' : ''}" role="option" aria-selected="${i === sel}" data-i="${i}">${x.icon}<span class="pal-l">${esc(x.label)}</span><span class="pal-h">${esc(x.hint || '')}</span></div>`).join('') || '<div class="empty">No matches.</div>';
      const on = list.querySelector('.on'); if (on) on.scrollIntoView({ block: 'nearest' });
    };
    const run = (i) => { const x = shown[i]; if (!x) return; close(); setTimeout(x.run, 30); };
    const onKey = (e) => {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
      else if (e.key === 'ArrowDown') { e.preventDefault(); sel = Math.min(shown.length - 1, sel + 1); render(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); sel = Math.max(0, sel - 1); render(); }
      else if (e.key === 'Enter') { e.preventDefault(); run(sel); }
    };
    document.addEventListener('keydown', onKey, true);
    q.oninput = () => { sel = 0; render(); };
    list.onclick = (e) => { const it = e.target.closest('[data-i]'); if (it) run(+it.dataset.i); };
    list.onmousemove = (e) => { const it = e.target.closest('[data-i]'); if (it && +it.dataset.i !== sel) { sel = +it.dataset.i; list.querySelectorAll('.pal-item').forEach((n) => { n.classList.toggle('on', +n.dataset.i === sel); n.setAttribute('aria-selected', +n.dataset.i === sel); }); } };
    ov.addEventListener('mousedown', (e) => { if (e.target === ov) close(); });
    render(); q.focus();
  };
  function shortcuts() {
    const row = (k, d) => `<tr><td>${k.split(' ').map((x) => `<kbd>${x}</kbd>`).join(' ')}</td><td>${d}</td></tr>`;
    BT.modal({ title: 'Keyboard shortcuts', body: `<table class="tbl kbd-tbl"><tbody>
      ${row('⌘ K', 'Search or jump to anything (also Ctrl K or /)')}${row('?', 'This help')}${row('Esc', 'Close dialog, drawer or palette')}
      ${Object.entries(GO).map(([k, id]) => row('g ' + k, 'Go to ' + BT.TITLES[id])).join('')}</tbody></table>` });
  }
  BT.shortcuts = shortcuts;
  let gAt = 0;
  document.addEventListener('keydown', (e) => {
    if (!authed()) return;
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); BT.palette(); return; }
    if (typing(e) || e.metaKey || e.ctrlKey || e.altKey || document.querySelector('.overlay')) return;
    if (e.key === '/') { e.preventDefault(); BT.palette(); return; }
    if (e.key === '?') { e.preventDefault(); shortcuts(); return; }
    if (e.key === 'g') { gAt = Date.now(); return; }
    if (Date.now() - gAt < 1200 && GO[e.key]) { e.preventDefault(); gAt = 0; location.hash = '#/' + GO[e.key]; }
  });
})();
