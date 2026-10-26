import { existsSync, readFileSync } from 'node:fs';

const pub = (path: string) => `public/${path}`;
const manifest = JSON.parse(readFileSync(pub('manifest.webmanifest'), 'utf8')) as Record<string, unknown> & {
  icons: { src: string; sizes: string; type: string; purpose?: string }[];
};
const pngSize = (path: string) => {
  const bytes = readFileSync(pub(path));
  expect(bytes.subarray(1, 4).toString()).toBe('PNG');
  return `${bytes.readUInt32BE(16)}x${bytes.readUInt32BE(20)}`;
};

describe('web app manifest', () => {
  it('has the installable fields and nothing that reaches beyond the shell', () => {
    expect(manifest).toMatchObject({
      id: '/',
      name: 'xDrishti',
      short_name: 'xDrishti',
      start_url: '/',
      scope: '/',
      display: 'standalone',
      theme_color: '#4338ca',
      prefer_related_applications: false,
    });
    for (const key of ['shortcuts', 'share_target', 'protocol_handlers', 'related_applications']) {
      expect(manifest).not.toHaveProperty(key);
    }
  });

  it('lists 192 and 512 icons plus a maskable one, each file present with the declared size', () => {
    const sizes = manifest.icons.map((icon) => icon.sizes);
    expect(sizes).toEqual(expect.arrayContaining(['192x192', '512x512']));
    expect(manifest.icons.some((icon) => icon.purpose === 'maskable')).toBe(true);
    for (const icon of manifest.icons) {
      expect(icon.src.startsWith('/icons/')).toBe(true);
      expect(pngSize(icon.src.slice(1))).toBe(icon.sizes);
    }
    expect(pngSize('icons/apple-touch-icon.png')).toBe('180x180');
  });

  it('is linked from index.html together with the apple touch icon', () => {
    const html = readFileSync('index.html', 'utf8');
    expect(html).toContain('<link rel="manifest" href="/manifest.webmanifest" />');
    expect(html).toContain('href="/icons/apple-touch-icon.png"');
  });

  it('the worker carries exactly one quoted build placeholder for the build to stamp', () => {
    expect(readFileSync(pub('sw.js'), 'utf8').match(/'__XD_BUILD__'/g)).toHaveLength(1);
  });

  it('every file the worker caches exists, and the offline page has no script and no external resource', () => {
    const sw = readFileSync(pub('sw.js'), 'utf8');
    const listed = [...(/XD_PRECACHE = \[([^\]]*)\]/.exec(sw)?.[1] ?? '').matchAll(/'(\/[^']+)'/g)].map(
      (m) => m[1] ?? '',
    );
    for (const path of listed.filter((p) => p !== '/offline.html')) expect(existsSync(pub(path.slice(1)))).toBe(true);
    expect(existsSync(pub('offline.html'))).toBe(true);
    const page = readFileSync(pub('offline.html'), 'utf8');
    expect(page).toContain(
      'You are offline. xDrishti shows no data without a connection and keeps none on this device.',
    );
    expect(page).not.toMatch(/<script|src=|https?:\/\//);
  });
});
