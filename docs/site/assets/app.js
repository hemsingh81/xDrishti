/* xDrishti docs — interactivity: theme, search, collapsible sections, TOC scroll-spy,
   sortable/filterable tables, copy buttons, Mermaid diagrams with zoom, glossary tooltips. */
(function () {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const root = document.documentElement;
  const IDX = window.BT_INDEX || { docs: [], sections: [], glossary: [] };
  const article = $('article.doc');
  const isDark = () => root.dataset.theme === 'dark';
  const store = {
    get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* ignore */ } },
  };

  /* ---------- theme ---------- */
  function syncHighlightTheme() {
    const l = $('#hl-light'), d = $('#hl-dark');
    if (l && d) { l.disabled = isDark(); d.disabled = !isDark(); }
  }
  syncHighlightTheme();
  $('#theme-btn')?.addEventListener('click', () => {
    root.dataset.theme = isDark() ? 'light' : 'dark';
    store.set('xd-theme', root.dataset.theme);
    syncHighlightTheme();
    renderDiagrams(true);
  });

  /* ---------- mobile nav ---------- */
  $('#menu-btn')?.addEventListener('click', () => document.body.classList.toggle('nav-open'));
  $('#content')?.addEventListener('click', () => document.body.classList.remove('nav-open'));

  /* ---------- reading progress ---------- */
  const bar = $('#progress');
  const onScroll = () => {
    const h = document.documentElement.scrollHeight - innerHeight;
    if (bar) bar.style.width = (h > 0 ? (scrollY / h) * 100 : 0) + '%';
  };
  addEventListener('scroll', onScroll, { passive: true });

  /* ---------- collapsible sections ---------- */
  if (article) {
    let body = null;
    for (const el of Array.from(article.children)) {
      if (el.tagName === 'H2') {
        body = document.createElement('div');
        body.className = 'sec-body';
        el.after(body);
        el.classList.add('sec-head');
        const b = document.createElement('button');
        b.className = 'sec-toggle';
        b.type = 'button';
        b.title = 'Collapse / expand section';
        b.setAttribute('aria-expanded', 'true');
        b.textContent = '▾';
        b.addEventListener('click', () => setCollapsed(el, !el.classList.contains('collapsed')));
        el.prepend(b);
      } else if (body && !el.classList.contains('sec-body')) {
        body.appendChild(el);
      }
    }
  }
  function setCollapsed(h2, collapsed) {
    h2.classList.toggle('collapsed', collapsed);
    h2.querySelector('.sec-toggle')?.setAttribute('aria-expanded', String(!collapsed));
  }
  function revealTarget(el) {
    if (!el) return;
    const sec = el.classList?.contains('sec-head') ? el : el.closest?.('.sec-body')?.previousElementSibling;
    if (sec && sec.classList.contains('collapsed')) setCollapsed(sec, false);
  }
  $('#expand-all')?.addEventListener('click', () => $$('.sec-head').forEach((h) => setCollapsed(h, false)));
  $('#collapse-all')?.addEventListener('click', () => $$('.sec-head').forEach((h) => setCollapsed(h, true)));
  function openHash() {
    if (!location.hash) return;
    const el = document.getElementById(decodeURIComponent(location.hash.slice(1)));
    if (el) { revealTarget(el); requestAnimationFrame(() => el.scrollIntoView()); }
  }
  addEventListener('hashchange', openHash);

  /* ---------- TOC scroll-spy ---------- */
  const tocLinks = $$('.toc a');
  if (tocLinks.length && 'IntersectionObserver' in window) {
    const map = new Map(tocLinks.map((a) => [decodeURIComponent(a.getAttribute('href').slice(1)), a]));
    const visible = new Set();
    const io = new IntersectionObserver((entries) => {
      entries.forEach((e) => (e.isIntersecting ? visible.add(e.target.id) : visible.delete(e.target.id)));
      const heads = $$('.doc h2[id], .doc h3[id]');
      const first = heads.find((h) => visible.has(h.id));
      if (first) { tocLinks.forEach((a) => a.classList.remove('active')); map.get(first.id)?.classList.add('active'); }
    }, { rootMargin: '-60px 0px -70% 0px' });
    $$('.doc h2[id], .doc h3[id]').forEach((h) => io.observe(h));
    tocLinks.forEach((a) => a.addEventListener('click', () => revealTarget(document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1))))));
  }

  /* ---------- tables: sort + filter ---------- */
  $$('.doc table').forEach((table) => {
    const wrap = document.createElement('div');
    wrap.className = 'table-wrap';
    table.before(wrap);
    wrap.appendChild(table);
    const rows = $$('tbody tr', table);
    if (rows.length > 5) {
      const tools = document.createElement('div');
      tools.className = 'table-tools';
      tools.innerHTML = `<input type="search" placeholder="Filter ${rows.length} rows…" aria-label="Filter table rows"><span class="count"></span>`;
      wrap.before(tools);
      const input = tools.querySelector('input'), count = tools.querySelector('.count');
      input.addEventListener('input', () => {
        const q = input.value.trim().toLowerCase();
        let n = 0;
        rows.forEach((r) => { const ok = !q || r.textContent.toLowerCase().includes(q); r.hidden = !ok; if (ok) n++; });
        count.textContent = q ? `${n} of ${rows.length}` : '';
      });
    }
    $$('thead th', table).forEach((th, col) => {
      th.title = 'Click to sort';
      const ind = document.createElement('span');
      ind.className = 'sort';
      th.appendChild(ind);
      let dir = 0;
      th.addEventListener('click', () => {
        dir = dir === 1 ? -1 : 1;
        $$('thead th .sort', table).forEach((s) => (s.textContent = ''));
        ind.textContent = dir === 1 ? '▲' : '▼';
        const tbody = table.tBodies[0];
        const cells = Array.from(tbody.rows).map((r) => ({ r, v: (r.cells[col]?.textContent || '').trim() }));
        const num = (s) => { const m = s.replace(/[,₹%]/g, '').match(/-?\d+(\.\d+)?/); return m ? parseFloat(m[0]) : NaN; };
        const numeric = cells.every((c) => c.v === '' || !isNaN(num(c.v)));
        cells.sort((a, b) => (numeric ? (num(a.v) || 0) - (num(b.v) || 0) : a.v.localeCompare(b.v, undefined, { numeric: true })) * dir);
        cells.forEach((c) => tbody.appendChild(c.r));
      });
    });
  });

  /* ---------- code: highlight + copy ---------- */
  if (window.hljs) $$('.code pre code:not(.nohighlight)').forEach((c) => { try { hljs.highlightElement(c); } catch (e) { /* unknown language */ } });
  $$('.code').forEach((box) => {
    const b = document.createElement('button');
    b.className = 'btn copy-btn';
    b.type = 'button';
    b.textContent = 'Copy';
    b.addEventListener('click', async () => {
      const text = box.querySelector('code').innerText;
      try { await navigator.clipboard.writeText(text); b.textContent = 'Copied ✓'; }
      catch (e) { b.textContent = 'Select & copy'; }
      setTimeout(() => (b.textContent = 'Copy'), 1500);
    });
    box.appendChild(b);
  });

  /* ---------- mermaid diagrams ---------- */
  const diagrams = $$('pre.mermaid');
  diagrams.forEach((el) => (el.dataset.src = el.textContent));
  async function renderDiagrams(rerender) {
    if (!window.mermaid || !diagrams.length) return;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: isDark() ? 'dark' : 'default',
      flowchart: { useMaxWidth: true, htmlLabels: true },
      gantt: { useMaxWidth: true },
    });
    if (rerender) diagrams.forEach((el) => { el.removeAttribute('data-processed'); el.innerHTML = ''; el.textContent = el.dataset.src; });
    try { await mermaid.run({ nodes: diagrams, suppressErrors: true }); } catch (e) { console.warn('Mermaid:', e); }
  }
  renderDiagrams(false);

  const modal = $('#modal');
  let zoom = 1;
  function applyZoom() {
    const svg = modal?.querySelector('.modal-body svg');
    if (!svg) return;
    const vb = svg.viewBox?.baseVal;
    const fit = Math.min(modal.querySelector('.modal-body').clientWidth - 48, (vb?.width || 1200) * 2);
    svg.style.width = Math.round(fit * zoom) + 'px';
  }
  $$('.diagram').forEach((fig) => {
    const b = document.createElement('button');
    b.className = 'btn expand-btn';
    b.type = 'button';
    b.textContent = '⤢ Expand';
    b.title = 'Open diagram full screen';
    b.addEventListener('click', () => {
      const svg = fig.querySelector('svg');
      if (!svg || !modal) return;
      const body = modal.querySelector('.modal-body');
      body.innerHTML = '';
      const clone = svg.cloneNode(true);
      clone.removeAttribute('style');
      clone.removeAttribute('width');
      clone.removeAttribute('height');
      body.appendChild(clone);
      modal.hidden = false;
      zoom = 1;
      applyZoom();
    });
    fig.appendChild(b);
  });
  modal?.addEventListener('click', (e) => {
    const z = e.target.closest('[data-zoom]');
    if (z) { const v = +z.dataset.zoom; zoom = v === 0 ? 1 : Math.max(0.3, Math.min(4, zoom * (v > 0 ? 1.25 : 0.8))); applyZoom(); }
    if (e.target.id === 'modal-close' || e.target === modal) modal.hidden = true;
  });

  /* ---------- glossary tooltips (first occurrence per page) ---------- */
  if (article && document.body.dataset.page !== '21-glossary.md' && IDX.glossary.length) {
    const terms = [];
    IDX.glossary.forEach((g) => g.alts.forEach((alt) => terms.push({ alt, def: `${g.term}: ${g.def}`, cs: alt === alt.toUpperCase() })));
    terms.sort((a, b) => b.alt.length - a.alt.length);
    const reEsc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const done = new Set();
    const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT, {
      acceptNode(n) {
        const p = n.parentElement;
        if (!n.nodeValue.trim() || !p || p.closest('pre, code, a, h1, h2, h3, h4, abbr, th, .mermaid, button')) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      },
    });
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    for (const t of terms) {
      if (done.has(t.def)) continue;
      const re = new RegExp(`(^|[^\\p{L}\\p{N}_-])(${reEsc(t.alt)})(?![\\p{L}\\p{N}_-])`, t.cs ? 'u' : 'iu');
      for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        const m = re.exec(n.nodeValue);
        if (!m) continue;
        const start = m.index + m[1].length;
        const after = n.splitText(start);
        const rest = after.splitText(m[2].length);
        const ab = document.createElement('abbr');
        ab.className = 'gl';
        ab.title = t.def;
        ab.tabIndex = 0;
        ab.textContent = after.nodeValue;
        after.replaceWith(ab);
        nodes.splice(i, 1, n, rest);
        done.add(t.def);
        break;
      }
    }
  }

  /* ---------- highlight search hits on arrival (?q=) ---------- */
  const q = new URLSearchParams(location.search).get('q');
  if (q && article) {
    const words = q.toLowerCase().split(/\s+/).filter((w) => w.length > 1);
    if (words.length) {
      const re = new RegExp(`(${words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'gi');
      const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT, {
        acceptNode: (n) => (n.parentElement?.closest('pre.mermaid, svg, button') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
      });
      const reTest = new RegExp(re.source, 'i');
      const hits = [];
      while (walker.nextNode() && hits.length < 200) if (reTest.test(walker.currentNode.nodeValue)) hits.push(walker.currentNode);
      hits.forEach((n) => {
        const span = document.createElement('span');
        span.innerHTML = n.nodeValue.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(re, '<mark class="hl">$1</mark>');
        n.replaceWith(...span.childNodes);
      });
    }
  }

  /* ---------- global search ---------- */
  const input = $('#search'), box = $('#results');
  let sel = -1, current = [];
  function search(query) {
    const words = query.toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return [];
    const out = [];
    IDX.sections.forEach((s) => {
      const doc = IDX.docs[s.d];
      const hay = (s.h + ' ' + s.t).toLowerCase();
      if (!words.every((w) => hay.includes(w) || doc.title.toLowerCase().includes(w))) return;
      let score = 0;
      words.forEach((w) => {
        if (s.h.toLowerCase().includes(w)) score += 10;
        if (doc.title.toLowerCase().includes(w)) score += 4;
        score += Math.min(10, hay.split(w).length - 1);
      });
      out.push({ s, doc, score });
    });
    return out.sort((a, b) => b.score - a.score).slice(0, 14);
  }
  function snippet(text, words) {
    const lower = text.toLowerCase();
    let i = -1;
    for (const w of words) { i = lower.indexOf(w); if (i >= 0) break; }
    const start = Math.max(0, i - 60);
    let snip = (start ? '… ' : '') + text.slice(start, start + 170) + (text.length > start + 170 ? ' …' : '');
    snip = snip.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    words.forEach((w) => { if (w.length > 1) snip = snip.replace(new RegExp(`(${w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi'), '<mark>$1</mark>'); });
    return snip;
  }
  function renderResults() {
    const query = input.value.trim();
    if (!query) { box.hidden = true; return; }
    current = search(query);
    sel = current.length ? 0 : -1;
    const words = query.toLowerCase().split(/\s+/).filter(Boolean);
    box.innerHTML = current.length
      ? current.map((r, i) => `<a role="option" class="${i === sel ? 'sel' : ''}" href="${r.doc.file}?q=${encodeURIComponent(query)}${r.s.a ? '#' + r.s.a : ''}">
          <div class="r-doc">${r.doc.num} · ${r.doc.title}</div><div class="r-head">${r.s.h.replace(/</g, '&lt;')}</div><div class="r-snip">${snippet(r.s.t, words)}</div></a>`).join('')
      : '<div class="empty">No matches</div>';
    box.hidden = false;
  }
  function moveSel(d) {
    const links = $$('a', box);
    if (!links.length) return;
    sel = (sel + d + links.length) % links.length;
    links.forEach((a, i) => a.classList.toggle('sel', i === sel));
    links[sel].scrollIntoView({ block: 'nearest' });
  }
  if (input && box) {
    input.addEventListener('input', renderResults);
    input.addEventListener('focus', () => input.value && renderResults());
    input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown') { e.preventDefault(); moveSel(1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); moveSel(-1); }
      else if (e.key === 'Enter') { const a = $$('a', box)[sel]; if (a) location.href = a.href; }
      else if (e.key === 'Escape') { box.hidden = true; input.blur(); }
    });
    document.addEventListener('click', (e) => { if (!e.target.closest('.search')) box.hidden = true; });
  }

  /* ---------- keyboard shortcuts ---------- */
  document.addEventListener('keydown', (e) => {
    const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName);
    if ((e.key === '/' && !typing) || (e.key.toLowerCase() === 'k' && (e.metaKey || e.ctrlKey))) { e.preventDefault(); input?.focus(); input?.select(); }
    else if (e.key === 'Escape' && modal && !modal.hidden) modal.hidden = true;
    else if (!typing && e.key === '[') $('.pager .prev')?.click();
    else if (!typing && e.key === ']') $('.pager .next')?.click();
  });

  /* ---------- index page card filter ---------- */
  const cardFilter = $('#card-filter');
  cardFilter?.addEventListener('input', () => {
    const v = cardFilter.value.trim().toLowerCase();
    $$('.card').forEach((c) => (c.hidden = !!v && !c.textContent.toLowerCase().includes(v)));
    $$('.card-group').forEach((g) => (g.hidden = !$$('.card:not([hidden])', g).length));
  });

  openHash();
  onScroll();
})();
