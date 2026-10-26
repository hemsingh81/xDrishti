import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, beforeEach, vi } from 'vitest';
import { createCapabilitiesFake } from '@/test/capabilitiesFake';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { ExportMenu } from '.';

function capability(enabled: boolean) {
  const fake = createCapabilitiesFake();
  fake.state.items = fake.state.items.map((i) => ({ ...i, enabled: i.name === 'exports' && enabled }));
  server.use(...fake.handlers);
}

const csv = '# basis: gross R\nrun_key,r\n';
const created: string[] = [];
const revoked: string[] = [];
const downloads: string[] = [];

beforeEach(() => {
  created.length = 0;
  revoked.length = 0;
  downloads.length = 0;
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    writable: true,
    value: (blob: Blob) => {
      created.push(`blob:${String(blob.size)}`);
      return 'blob:fake';
    },
  });
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    writable: true,
    value: (url: string) => {
      revoked.push(url);
    },
  });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function click(this: HTMLAnchorElement) {
    downloads.push(this.download);
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ExportMenu (P8-20)', () => {
  it('draws nothing while the switch is off, and nothing while it is unknown', async () => {
    capability(false);
    const { container } = renderWithProviders(<ExportMenu kind="report-groups" query={{ runs: 'a' }} />);
    await waitFor(() => {
      expect(container).toBeEmptyDOMElement();
    });
  });

  it('draws nothing when the list of switches has no exports row', async () => {
    server.use(http.get('/api/capabilities', () => HttpResponse.json({ items: [], history: [] })));
    const { container } = renderWithProviders(<ExportMenu kind="report-groups" query={{}} />);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(container).toBeEmptyDOMElement();
  });

  it('on: shows the button and the sentence, posts the screen query, saves the server name and revokes the address', async () => {
    capability(true);
    let seen: { kind: string; body: unknown; csrf: string | null } | undefined;
    server.use(
      http.post('/api/exports/:kind', async ({ request, params }) => {
        seen = { kind: String(params.kind), body: await request.json(), csrf: request.headers.get('x-csrf-token') };
        return new HttpResponse(csv, {
          headers: {
            'content-type': 'text/csv; charset=utf-8',
            'content-disposition': 'attachment; filename=xd-report-groups-20261012-abcd1234.csv',
          },
        });
      }),
    );
    const view = renderWithProviders(
      <ExportMenu kind="report-groups" query={{ runs: 'a', lens: 'basket', basis: 'gross' }} />,
    );

    expect(await screen.findByRole('button', { name: 'Export CSV' })).toBeInTheDocument();
    expect(
      screen.getByText(
        "A file will be saved on this device, outside the app's protection. It contains research tables only.",
      ),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => {
      expect(downloads).toEqual(['xd-report-groups-20261012-abcd1234.csv']);
    });
    expect(seen?.kind).toBe('report-groups');
    expect(seen?.body).toEqual({ format: 'csv', query: { runs: 'a', lens: 'basket', basis: 'gross' } });
    expect(created).toHaveLength(1);
    await waitFor(
      () => {
        expect(revoked).toEqual(['blob:fake']);
      },
      { timeout: 2000 },
    );
    expect(await screen.findByText('The file was saved.')).toBeInTheDocument();
    // nothing is kept: the mutation is dropped and no query holds the file
    expect(
      view.client
        .getMutationCache()
        .getAll()
        .map((m) => m.state.data),
    ).toEqual([{ name: 'xd-report-groups-20261012-abcd1234.csv' }]);
    view.unmount();
    await waitFor(() => {
      expect(view.client.getMutationCache().getAll()).toHaveLength(0);
    });
    expect(
      view.client
        .getQueryCache()
        .getAll()
        .filter((q) => JSON.stringify(q.queryKey).includes('export')),
    ).toEqual([]);
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0);
  });

  it.each([
    [403, 'capabilities.off', 'This is switched off.'],
    [413, 'exports.too_large', 'This table is larger than an export holds. Narrow the filters.'],
    [422, 'reports.invalid', 'lens is not valid <b>bold</b>'],
    [429, 'auth.rate_limited', 'At most 6 exports a minute.'],
  ])('a %i answer shows the problem text as text and saves nothing', async (status, code, title) => {
    capability(true);
    server.use(http.post('/api/exports/:kind', () => problemResponse({ title, code, status }, status)));
    renderWithProviders(<ExportMenu kind="report-trades" query={{}} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Export CSV' }));

    expect(await screen.findByText(title)).toBeInTheDocument();
    expect(document.querySelector('b')).toBeNull();
    expect(downloads).toEqual([]);
    expect(created).toEqual([]);
  });
});
