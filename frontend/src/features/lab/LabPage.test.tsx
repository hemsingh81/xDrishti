import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { LabPage } from './components/LabPage';

const hash = 'abcdef0123456789abcdef';
const yaml = 'strategy:\n  id: MY_ORB\n';

const strategies: Schemas['StoredStrategy'][] = [
  { id: 'ORB', style: 'intraday', builtIn: true, createdAt: '2026-10-01T00:00:00Z', latestVersion: 1, versionCount: 1 },
  {
    id: 'MY_ORB',
    style: 'intraday',
    builtIn: false,
    createdAt: '2026-10-02T00:00:00Z',
    latestVersion: 2,
    versionCount: 2,
  },
];

const validation = (over: Partial<Schemas['StrategyValidation']> = {}): Schemas['StrategyValidation'] => ({
  valid: true,
  hash,
  id: 'MY_ORB',
  style: 'intraday',
  version: 1,
  timeframes: ['15m'],
  sides: ['BUY'],
  problems: [],
  ...over,
});

function useLibrary() {
  server.use(
    http.get('/api/lab/schema', () =>
      HttpResponse.text('{"type":"object"}', { headers: { 'content-type': 'application/schema+json' } }),
    ),
    http.get('/api/lab/strategies', () => HttpResponse.json(strategies)),
    http.post('/api/lab/validate', () => HttpResponse.json(validation())),
    http.get('/api/lab/strategies/:id', ({ params }) =>
      HttpResponse.json({
        id: params.id,
        style: 'intraday',
        builtIn: false,
        createdAt: '2026-10-02T00:00:00Z',
        latestVersion: 2,
        versions: [
          { version: 1, hash, parentHash: null, note: null, createdAt: '2026-10-02T00:00:00Z' },
          { version: 2, hash, parentHash: hash, note: 'tighter stop', createdAt: '2026-10-03T00:00:00Z' },
        ],
      }),
    ),
    http.get('/api/lab/strategies/:id/versions/:version', ({ params }) =>
      HttpResponse.json({
        id: params.id,
        version: Number(params.version),
        hash,
        parentHash: null,
        note: null,
        createdAt: '2026-10-02T00:00:00Z',
        text: yaml,
        problemsFree: true,
      }),
    ),
  );
}

async function openVersion(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /MY_ORB/ }));
  await user.click(await screen.findByRole('button', { name: /Version 2/ }));
  await waitFor(() => expect(screen.getByLabelText('Strategy text')).toHaveValue(yaml));
}

describe('LabPage', () => {
  it('lists the library with built-ins marked', async () => {
    useLibrary();
    renderWithProviders(<LabPage />);

    const list = await screen.findByRole('list', { name: 'Strategies' });
    expect(list).toHaveTextContent('ORB');
    expect(list).toHaveTextContent('Built-in');
    expect(list).toHaveTextContent('latest v2 · 2 versions');
  });

  it('shows an error when the library cannot be read', async () => {
    useLibrary();
    server.use(http.get('/api/lab/strategies', () => problemResponse({ title: 'Library unavailable.' }, 503)));
    renderWithProviders(<LabPage />);

    expect(await screen.findByText('Library unavailable.')).toBeInTheDocument();
  });

  it('opens a version into the editor and offers its download', async () => {
    useLibrary();
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openVersion(user);

    expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute(
      'href',
      '/api/lab/strategies/MY_ORB/versions/2/file',
    );
    expect(await screen.findByText(/Valid - hash abcdef012345/)).toBeInTheDocument();
  });

  it('lists the located problems of an invalid text and keeps Save off', async () => {
    useLibrary();
    server.use(
      http.post('/api/lab/validate', () =>
        HttpResponse.json(
          validation({
            valid: false,
            hash: null,
            problems: [
              {
                path: 'strategy.stop',
                code: 'strategy.unknown_key',
                message: 'Unknown key.',
                severity: 'error',
                line: 3,
                column: 5,
                endLine: 3,
                endColumn: 9,
                start: null,
                length: null,
                suggestion: null,
              },
            ],
          }),
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openVersion(user);

    expect(await screen.findByText('error: Unknown key.')).toBeInTheDocument();
    expect(screen.getByText('strategy.unknown_key · strategy.stop · line 3, column 5')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('validates what is typed after a pause', async () => {
    useLibrary();
    const texts: string[] = [];
    server.use(
      http.post('/api/lab/validate', async ({ request }) => {
        texts.push(((await request.json()) as { text: string }).text);
        return HttpResponse.json(validation());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await user.type(await screen.findByLabelText('Strategy text'), 'a');

    await waitFor(() => {
      expect(texts).toEqual(['a']);
    });
  });

  it('saves a valid text and says so', async () => {
    useLibrary();
    let body: unknown;
    server.use(
      http.post('/api/lab/validate', () => HttpResponse.json(validation())),
      http.post('/api/lab/strategies', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ id: 'MY_ORB', version: 3, hash, parentHash: hash, created: true }, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openVersion(user);
    const save = screen.getByRole('button', { name: 'Save' });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    expect(await screen.findByText('Saved MY_ORB version 3.')).toBeInTheDocument();
    expect(body).toMatchObject({ text: yaml, parentHash: null });
  });

  it('shows the refusal of a save and keeps the text', async () => {
    useLibrary();
    server.use(
      http.post('/api/lab/validate', () => HttpResponse.json(validation())),
      http.post('/api/lab/strategies', () =>
        problemResponse({ title: 'Built-in strategies cannot be changed.', code: 'lab.builtin_readonly' }, 409),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openVersion(user);
    const save = screen.getByRole('button', { name: 'Save' });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    expect(await screen.findByText('Built-in strategies cannot be changed.')).toBeInTheDocument();
    expect(screen.getByLabelText('Strategy text')).toHaveValue(yaml);
  });

  it('clones into a draft and saves it with the parent hash', async () => {
    useLibrary();
    let body: unknown;
    server.use(
      http.post('/api/lab/validate', () => HttpResponse.json(validation())),
      http.post('/api/lab/strategies/:id/versions/:version/clone', () =>
        HttpResponse.json({
          id: 'MY_ORB_COPY',
          text: 'strategy:\n  id: MY_ORB_COPY\n',
          parentHash: hash,
          sourceId: 'MY_ORB',
          sourceVersion: 2,
        }),
      ),
      http.post('/api/lab/strategies', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          { id: 'MY_ORB_COPY', version: 1, hash, parentHash: hash, created: true },
          { status: 201 },
        );
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openVersion(user);
    await user.click(screen.getByRole('button', { name: 'Clone' }));
    await waitFor(() => expect(screen.getByLabelText('Strategy text')).toHaveValue('strategy:\n  id: MY_ORB_COPY\n'));
    expect(screen.queryByRole('link', { name: 'Download' })).not.toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'Save' });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() => {
      expect(body).toMatchObject({ parentHash: hash });
    });
  });

  it('puts an uploaded file into the editor and refuses an oversize one', async () => {
    useLibrary();
    server.use(http.post('/api/lab/validate', () => HttpResponse.json(validation())));
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    const input = await screen.findByLabelText('Upload a strategy file');
    await user.upload(input, new File([yaml], 'orb.yaml', { type: 'text/yaml' }));
    await waitFor(() => expect(screen.getByLabelText('Strategy text')).toHaveValue(yaml));

    await user.upload(input, new File(['x'.repeat(256 * 1024 + 1)], 'big.yaml', { type: 'text/yaml' }));
    expect(await screen.findByText(/over 256 KB/)).toBeInTheDocument();
    expect(screen.getByLabelText('Strategy text')).toHaveValue(yaml);
  });
});
