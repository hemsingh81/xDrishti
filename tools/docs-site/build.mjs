// Builds the interactive HTML documentation site from /docs/design/*.md into /docs/site.
// Usage: cd tools/docs-site && npm install && npm run build
import fs from 'node:fs';
import path from 'node:path';
import { Marked } from 'marked';

const here = import.meta.dirname;
const root = path.resolve(here, '../..');
const docsDir = path.join(root, 'docs', 'design');
const outDir = path.join(root, 'docs', 'site');
const assetsOut = path.join(outDir, 'assets');

const GROUPS = [
  { name: 'Foundation', nums: ['01', '02', '03', '04'] },
  { name: 'Data & instruments', nums: ['05', '06'] },
  { name: 'Trading engine', nums: ['07', '08', '09', '10'] },
  { name: 'Tools & experience', nums: ['11', '12', '13', '14', '15'] },
  { name: 'Delivery & operations', nums: ['16', '17', '18', '19', '20'] },
  { name: 'Reference', nums: ['21'] },
];

// ---------- helpers ----------
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const htmlName = (mdFile) => mdFile.replace(/\.md$/, '.html');
const stripMd = (s) => s
  .replace(/```mermaid[\s\S]*?```/g, ' ')
  .replace(/```\w*/g, ' ')
  .replace(/`([^`]*)`/g, '$1')
  .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
  .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
  .replace(/[*_~>#|]/g, ' ')
  .replace(/-{3,}/g, ' ')
  .replace(/\s+/g, ' ')
  .trim();
function slugger() {
  const seen = new Map();
  return (text) => {
    let base = text.toLowerCase().replace(/<[^>]+>/g, '').replace(/&[a-z]+;/g, '')
      .replace(/[^\p{L}\p{N}\s-]/gu, '').trim().replace(/\s+/g, '-') || 'section';
    const n = seen.get(base) || 0;
    seen.set(base, n + 1);
    return n ? `${base}-${n}` : base;
  };
}
const rewriteHref = (href) => {
  if (!href || /^(https?:|mailto:|#)/.test(href)) return href;
  return href.replace(/^docs\//, '').replace(/\.md(#|$)/, '.html$1');
};

// ---------- doc metadata from README ----------
const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');
const docs = [];
for (const m of readme.matchAll(/^\|\s*(\d{2})\s*\|\s*\[\**([^\]]+?)\**\]\(docs\/design\/([^)]+\.md)\)\s*\|\s*([^|]+?)\s*\|/gm)) {
  docs.push({ num: m[1], title: m[2].replace(/\*/g, ''), file: m[3], desc: m[4] });
}
if (!docs.length) throw new Error('No docs found in README table');
const intro = (readme.split('\n## ')[0].split('\n').slice(1).join('\n')).trim();

// ---------- glossary ----------
const glossary = [];
const glossMd = fs.readFileSync(path.join(docsDir, '21-glossary.md'), 'utf8');
for (const m of glossMd.matchAll(/^\|\s*\*\*([^*]+)\*\*\s*\|\s*([^|]+?)\s*\|/gm)) {
  const term = m[1].trim();
  const alts = term.replace(/\(([^)]*)\)/g, '/$1').split('/').map((s) => s.trim())
    .filter((s) => s.length >= 3 && !/[\[\]]/.test(s));
  glossary.push({ term, alts, def: m[2].trim() });
}

// ---------- render ----------
const searchSections = [];
const pages = [];

for (const [di, doc] of docs.entries()) {
  const md = fs.readFileSync(path.join(docsDir, doc.file), 'utf8');
  const slug = slugger();
  const toc = [];
  const marked = new Marked({ gfm: true });
  marked.use({
    renderer: {
      heading({ tokens, depth }) {
        const inner = this.parser.parseInline(tokens);
        const id = slug(inner);
        if (depth === 2 || depth === 3) toc.push({ depth, id, text: inner.replace(/<[^>]+>/g, '') });
        const anchor = depth > 1 ? `<a class="hash" href="#${id}" aria-label="Link to this section">#</a>` : '';
        return `<h${depth} id="${id}">${inner}${anchor}</h${depth}>\n`;
      },
      code({ text, lang }) {
        if (lang === 'mermaid') return `<figure class="diagram"><pre class="mermaid">${esc(text)}</pre></figure>\n`;
        const cls = lang ? `language-${esc(lang)}` : 'nohighlight';
        return `<div class="code"><pre><code class="${cls}">${esc(text)}</code></pre></div>\n`;
      },
      link({ href, title, tokens }) {
        const inner = this.parser.parseInline(tokens);
        const h = rewriteHref(href);
        const ext = /^https?:/.test(h) ? ' target="_blank" rel="noopener"' : '';
        return `<a href="${esc(h)}"${title ? ` title="${esc(title)}"` : ''}${ext}>${inner}</a>`;
      },
    },
  });
  const body = marked.parse(md);
  const h1 = (md.match(/^# (.+)$/m) || [, doc.title])[1];

  // search sections: split on ##/### headings, anchors via the same slug algorithm
  const s2 = slugger();
  let current = { h: h1, a: '', text: [] };
  const flush = () => { const t = stripMd(current.text.join(' ')); if (t || current.a) searchSections.push({ d: di, h: current.h, a: current.a, t: t.slice(0, 4000) }); };
  let inFence = false;
  for (const line of md.split('\n')) {
    if (/^\s*```/.test(line)) inFence = !inFence;
    const hm = inFence ? null : line.match(/^(#{1,6}) (.+)$/);
    if (hm) {
      const plain = hm[2];
      const inline = new Marked().parseInline(plain);
      const id = s2(inline);
      if (hm[1].length === 1 || hm[1].length > 3) { if (hm[1].length > 3) current.text.push(plain); continue; }
      flush();
      current = { h: stripMd(plain), a: id, text: [] };
    } else current.text.push(line);
  }
  flush();
  pages.push({ ...doc, h1, body, toc });
}

// ---------- templates ----------
const builtAt = new Date().toISOString().slice(0, 16).replace('T', ' ');
const headCommon = (title) => `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title>
<script>(function(){var t;try{t=localStorage.getItem('xd-theme')}catch(e){}document.documentElement.dataset.theme=t||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light')})();</script>
<link rel="stylesheet" href="assets/vendor/github.min.css" id="hl-light">
<link rel="stylesheet" href="assets/vendor/github-dark.min.css" id="hl-dark">
<link rel="stylesheet" href="assets/app.css">
</head>`;

const sidebar = (active) => `<nav class="sidebar" id="sidebar" aria-label="Documents">
  <a class="side-home${active === 'index' ? ' active' : ''}" href="index.html">Home</a>
  ${GROUPS.map((g) => `<div class="side-group"><div class="side-title">${g.name}</div>
    ${pages.filter((p) => g.nums.includes(p.num)).map((p) => `<a href="${htmlName(p.file)}" class="${p.file === active ? 'active' : ''}"><span class="num">${p.num}</span>${esc(p.title)}</a>`).join('\n    ')}
  </div>`).join('\n  ')}
</nav>`;

const topbar = (showDocTools) => `<div id="progress" aria-hidden="true"></div>
<header class="topbar">
  <button class="icon-btn" id="menu-btn" aria-label="Toggle navigation" title="Toggle navigation">☰</button>
  <a class="brand" href="index.html"><span class="logo">◉</span> xDrishti <span class="brand-sub">design docs</span></a>
  <div class="search">
    <input id="search" type="search" placeholder="Search all docs…  ( / )" autocomplete="off" aria-label="Search all documents">
    <div id="results" class="results" role="listbox" hidden></div>
  </div>
  <div class="tools">
    ${showDocTools ? `<button class="btn" id="expand-all" title="Expand all sections">Expand all</button>
    <button class="btn" id="collapse-all" title="Collapse all sections">Collapse all</button>` : ''}
    <button class="icon-btn" id="theme-btn" aria-label="Toggle dark mode" title="Toggle dark mode">◐</button>
  </div>
</header>`;

const scripts = `<script src="assets/search-index.js"></script>
<script src="assets/vendor/mermaid.min.js"></script>
<script src="assets/vendor/highlight.min.js"></script>
<script src="assets/app.js"></script>`;

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(path.join(assetsOut, 'vendor'), { recursive: true });

for (const [i, p] of pages.entries()) {
  const prev = pages[i - 1], next = pages[i + 1];
  const tocHtml = p.toc.map((t) => `<a href="#${t.id}" class="d${t.depth}">${esc(t.text)}</a>`).join('\n');
  const html = `${headCommon(`${p.num} · ${p.title} — xDrishti`)}
<body data-page="${p.file}">
${topbar(true)}
<div class="layout${p.toc.length ? '' : ' no-toc'}">
${sidebar(p.file)}
<main class="content" id="content">
  <div class="doc-meta"><span class="pill">${p.num}</span> ${esc(GROUPS.find((g) => g.nums.includes(p.num)).name)} · <a href="../design/${p.file}">Markdown source</a></div>
  <article class="doc">
${p.body}
  </article>
  <nav class="pager" aria-label="Previous and next document">
    ${prev ? `<a class="prev" href="${htmlName(prev.file)}"><small>← Previous  [</small>${prev.num} · ${esc(prev.title)}</a>` : '<span></span>'}
    ${next ? `<a class="next" href="${htmlName(next.file)}"><small>Next  ] →</small>${next.num} · ${esc(next.title)}</a>` : '<span></span>'}
  </nav>
  <footer class="foot">Generated from <code>docs/${p.file}</code> · ${builtAt}</footer>
</main>
${p.toc.length ? `<aside class="toc" aria-label="On this page"><div class="toc-title">On this page</div>${tocHtml}</aside>` : ''}
</div>
<div id="modal" class="modal" hidden><div class="modal-bar"><button class="btn" data-zoom="-1">−</button><button class="btn" data-zoom="0">Fit</button><button class="btn" data-zoom="1">+</button><button class="btn" id="modal-close">Close ✕</button></div><div class="modal-body"></div></div>
${scripts}
</body>
</html>`;
  fs.writeFileSync(path.join(outDir, htmlName(p.file)), html);
}

// ---------- index ----------
const tiles = [
  ['≈ ₹590', 'per month running cost'],
  ['0', 'cloud services'],
  ['3', 'max new entries / day (config)'],
  ['5', 'sessions max holding'],
  ['1m → 1M', 'all timeframes from 1-minute data'],
  ['Live', 'only for active trades & holdings'],
  ['XIRR', 'by trade, basket, account, total'],
  ['You', 'approve every next-day trade'],
  ['16', 'roadmap phases (P0–P15)'],
];
const stack = ['React 19 + TypeScript', '.NET 10 · ASP.NET Core', 'Hangfire · SignalR', 'PostgreSQL + TimescaleDB', 'ML.NET', 'Local LLM (llama.cpp)', 'Podman Desktop', 'CSV 1-minute + Dhan Data API'];
const introHtml = new Marked().parse(intro.replace(/\(docs\/([^)]+)\.md\)/g, '($1.html)'));
const indexHtml = `${headCommon('xDrishti — design documentation')}
<body data-page="index">
${topbar(false)}
<div class="layout no-toc">
${sidebar('index')}
<main class="content" id="content">
  <section class="hero">
    <h1>xDrishti</h1>
    <div class="lead">${introHtml}</div>
    <div class="chips">${stack.map((s) => `<span class="chip">${esc(s)}</span>`).join('')}</div>
    <p style="margin-top:14px"><a class="btn" href="../prototype/index.html" style="display:inline-flex;align-items:center;height:auto;padding:8px 14px">▶ Open the clickable prototype</a></p>
  </section>
  <section class="tiles">${tiles.map(([v, l]) => `<div class="tile"><div class="tile-v">${esc(v)}</div><div class="tile-l">${esc(l)}</div></div>`).join('')}</section>
  <div class="filter-row"><input id="card-filter" type="search" placeholder="Filter documents…" aria-label="Filter documents"></div>
  ${GROUPS.map((g) => `<section class="card-group"><h2>${g.name}</h2><div class="cards">
    ${pages.filter((p) => g.nums.includes(p.num)).map((p) => `<a class="card" href="${htmlName(p.file)}"><div class="card-num">${p.num}</div><div class="card-title">${esc(p.title)}</div><div class="card-desc">${esc(p.desc)}</div><div class="card-foot">${p.toc.filter((t) => t.depth === 2).length} sections</div></a>`).join('\n    ')}
  </div></section>`).join('\n  ')}
  <footer class="foot">Generated from <code>docs/*.md</code> · ${builtAt} · Rebuild: <code>cd tools/docs-site &amp;&amp; npm run build</code></footer>
</main>
</div>
${scripts}
</body>
</html>`;
fs.writeFileSync(path.join(outDir, 'index.html'), indexHtml);

// ---------- assets ----------
const index = {
  docs: pages.map((p) => ({ file: htmlName(p.file), num: p.num, title: p.title })),
  sections: searchSections,
  glossary: glossary.map((g) => ({ term: g.term, alts: g.alts, def: g.def })),
};
fs.writeFileSync(path.join(assetsOut, 'search-index.js'), `window.BT_INDEX=${JSON.stringify(index)};\n`);
for (const f of ['app.css', 'app.js']) fs.copyFileSync(path.join(here, 'assets', f), path.join(assetsOut, f));
const nm = path.join(here, 'node_modules');
const vendor = [
  ['mermaid/dist/mermaid.min.js', 'mermaid.min.js'],
  ['@highlightjs/cdn-assets/highlight.min.js', 'highlight.min.js'],
  ['@highlightjs/cdn-assets/styles/github.min.css', 'github.min.css'],
  ['@highlightjs/cdn-assets/styles/github-dark.min.css', 'github-dark.min.css'],
];
for (const [src, dst] of vendor) fs.copyFileSync(path.join(nm, src), path.join(assetsOut, 'vendor', dst));

console.log(`Built ${pages.length} pages + index into ${path.relative(root, outDir)}/ (${searchSections.length} search sections, ${glossary.length} glossary terms)`);
