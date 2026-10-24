import { BUDGET_BYTES, firstLoadAssets, gzipBytes, kb, measureFirstLoad, report } from '../../scripts/firstLoad.mjs';

/** Bytes that do not compress (a fixed pseudo-random sequence), so a file of N bytes is about N bytes gzipped. */
function incompressible(size: number, seed = 1): Uint8Array {
  const bytes = new Uint8Array(size);
  let x = seed;
  for (let i = 0; i < size; i++) {
    x = (Math.imul(x, 1103515245) + 12345) >>> 0;
    bytes[i] = x >>> 24;
  }
  return bytes;
}

const html = `<!doctype html><html><head>
  <link rel="preload" href="/assets/font.woff2" as="font" />
  <script type="module" crossorigin src="/assets/index-AAA.js"></script>
  <link rel="modulepreload" crossorigin href="/assets/vendor-BBB.js">
  <link href="/assets/ui-CCC.js" rel="modulepreload">
  <link rel="modulepreload" href="/assets/vendor-BBB.js">
  <link rel="stylesheet" href="/assets/index-DDD.css">
</head><body><div id="root"></div></body></html>`;

function dist(sizes: Record<string, number>) {
  const files = new Map(Object.entries(sizes).map(([href, size], i) => [href, incompressible(size, i + 1)]));
  return (href: string) => {
    const file = files.get(href);
    if (!file) throw new Error(`missing asset ${href}`);
    return file;
  };
}

describe('the bundle guard', () => {
  it('finds the entry script and every modulepreload chunk once, in order, whatever the attribute order', () => {
    expect(firstLoadAssets(html)).toEqual(['/assets/index-AAA.js', '/assets/vendor-BBB.js', '/assets/ui-CCC.js']);
  });

  it('does not count fonts, stylesheets or lazy chunks', () => {
    const assets = firstLoadAssets(html);
    expect(assets.some((a) => a.endsWith('.woff2') || a.endsWith('.css'))).toBe(false);
  });

  it('measures the gzip size of each chunk and passes under the budget', () => {
    const result = measureFirstLoad(
      html,
      dist({ '/assets/index-AAA.js': 120_000, '/assets/vendor-BBB.js': 50_000, '/assets/ui-CCC.js': 20_000 }),
    );
    expect(result.chunks.map((c) => c.href)).toEqual([
      '/assets/index-AAA.js',
      '/assets/vendor-BBB.js',
      '/assets/ui-CCC.js',
    ]);
    expect(result.total).toBe(result.chunks.reduce((sum, c) => sum + c.gzip, 0));
    expect(result.total).toBeGreaterThan(185_000);
    expect(result.total).toBeLessThan(195_000);
    expect(result.budget).toBe(BUDGET_BYTES);
    expect(result.withinBudget).toBe(true);
    expect(result.headroom).toBe(BUDGET_BYTES - result.total);
  });

  it('fails when a 10 KB import is planted into the entry chunk of a build that was 4.7 KB under', () => {
    const base = { '/assets/index-AAA.js': 125_000, '/assets/vendor-BBB.js': 50_000, '/assets/ui-CCC.js': 20_300 };
    const before = measureFirstLoad(html, dist(base));
    expect(before.withinBudget).toBe(true);
    expect(before.headroom).toBeLessThan(5_000);

    const after = measureFirstLoad(html, dist({ ...base, '/assets/index-AAA.js': 135_000 }));
    expect(after.total - before.total).toBeGreaterThan(9_900);
    expect(after.withinBudget).toBe(false);
    expect(after.headroom).toBeLessThan(0);
  });

  it('is exactly at the budget still within it, and one byte over is not', () => {
    const readAsset = dist({ '/assets/index-AAA.js': 1000, '/assets/vendor-BBB.js': 1000, '/assets/ui-CCC.js': 1000 });
    const { total } = measureFirstLoad(html, readAsset);
    expect(measureFirstLoad(html, readAsset, total).withinBudget).toBe(true);
    expect(measureFirstLoad(html, readAsset, total - 1).withinBudget).toBe(false);
  });

  it('fails loudly, never with a smaller number, when a chunk the page loads is missing from the build', () => {
    expect(() => measureFirstLoad(html, dist({ '/assets/index-AAA.js': 1000 }))).toThrow(/missing asset/);
  });

  it('fails when index.html has no module entry script', () => {
    expect(() => measureFirstLoad('<html><body></body></html>', dist({}))).toThrow(/no module entry script/);
  });

  it('prints every chunk, the total, the budget and the verdict', () => {
    const under = report(
      measureFirstLoad(
        html,
        dist({ '/assets/index-AAA.js': 1000, '/assets/vendor-BBB.js': 1, '/assets/ui-CCC.js': 1 }),
      ),
    ).join('\n');
    expect(under).toContain('/assets/index-AAA.js');
    expect(under).toMatch(/First-load JavaScript .*: \d+\.\d KB gzip of 200\.0 KB budget, \d+\.\d KB to spare\./);

    const over = report(
      measureFirstLoad(
        html,
        dist({ '/assets/index-AAA.js': 210_000, '/assets/vendor-BBB.js': 1, '/assets/ui-CCC.js': 1 }),
      ),
    );
    expect(over.join('\n')).toMatch(/OVER by \d+\.\d KB/);
  });

  it('compresses the way gzip -c does and writes KB as 1000 bytes', () => {
    expect(gzipBytes('a'.repeat(10_000))).toBeLessThan(100);
    expect(kb(195_250)).toBe('195.3 KB');
    expect(BUDGET_BYTES).toBe(200_000);
  });
});
