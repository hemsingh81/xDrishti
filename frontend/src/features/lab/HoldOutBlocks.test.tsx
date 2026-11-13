import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { HoldOutBlocksCard } from './components/HoldOutBlocksCard';

type Block = Schemas['HoldOutBlockView'];
type Overview = Schemas['HoldOutView'];

const open = { state: 'available' } as const;
const none = { state: 'no_subject' } as const;
const fresh = { state: 'fresh', subject: 'ORB v3', passed: true, status: 'pass', mode: 'auto' } as const;

const block = (over: Partial<Block> = {}): Block => ({
  start: '2026-05-05',
  historyFirst: '2024-10-01',
  historyLast: '2026-11-04',
  lengthMonths: 6,
  settingHash: 'a'.repeat(64),
  createdAt: '2026-11-04T05:00:00Z',
  state: 'looked',
  looks: { strategy: fresh, model: none, system: none },
  ...over,
});

const overview = (blocks: Block[]): Overview => ({
  enforce: true,
  autoUnlock: true,
  block: null,
  proposed: null,
  unlocks: [],
  blocks,
  autoRelease: true,
});

function serve(blocks: Block[]) {
  server.use(http.get('/api/lab/holdout', () => HttpResponse.json(overview(blocks))));
}

describe('HoldOutBlocksCard (HO-01b)', () => {
  it('lists the blocks with their states and the three looks, and what blocks the release', async () => {
    serve([
      block({ state: 'released', releasedAt: '2026-11-20T05:00:00Z', mode: 'owner' }),
      block({
        start: '2026-11-05',
        historyLast: '2027-05-04',
        state: 'looked',
        looks: { strategy: fresh, model: open, system: none },
        blockedBy: 'look_available',
      }),
    ]);
    renderWithProviders(<HoldOutBlocksCard />);

    const card = await screen.findByRole('region', { name: 'Hold-out blocks' });
    expect(within(card).getByText('Released')).toBeInTheDocument();
    expect(within(card).getByText(/released .*\(owner\)/)).toBeInTheDocument();
    expect(within(card).getByText('Looked')).toBeInTheDocument();
    expect(within(card).getAllByText('Strategy: Taken, pass').length).toBe(2);
    expect(within(card).getByText('Model: Still available')).toBeInTheDocument();
    expect(within(card).getAllByText('System: No subject').length).toBeGreaterThan(0);
    expect(within(card).getByText(/A look is still available/)).toBeInTheDocument();
    expect(within(card).getAllByRole('button', { name: 'Release this block' })).toHaveLength(1);
  });

  it('says no block is fixed yet and offers no release', async () => {
    serve([]);
    renderWithProviders(<HoldOutBlocksCard />);

    expect(await screen.findByText(/No block is fixed yet/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Release this block' })).toBeNull();
  });

  it('offers no release while the block is filling', async () => {
    serve([block({ state: 'filling', looks: { strategy: none, model: none, system: none } })]);
    renderWithProviders(<HoldOutBlocksCard />);

    expect(await screen.findByText('Filling')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Release this block' })).toBeNull();
  });

  it('releases a looked block with the password, sends no forfeit, clears the field and closes', async () => {
    let body: unknown;
    server.use(
      http.post('/api/lab/holdout/blocks/:start/release', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(block({ state: 'released' }));
      }),
    );
    serve([block()]);
    const user = userEvent.setup();
    renderWithProviders(<HoldOutBlocksCard />);

    await user.click(await screen.findByRole('button', { name: 'Release this block' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByText(/forfeits its looks/)).toBeNull();
    expect(within(dialog).getByRole('button', { name: 'Release' })).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Release' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });
    expect(body).toEqual({ password: 'secret', forfeitLook: false });
  });

  it('states the forfeit for a block with no look and needs the tick before it sends forfeitLook', async () => {
    let body: unknown;
    server.use(
      http.post('/api/lab/holdout/blocks/:start/release', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(block({ state: 'released' }));
      }),
    );
    serve([block({ state: 'sealed', looks: { strategy: none, model: none, system: none }, blockedBy: 'unlooked' })]);
    const user = userEvent.setup();
    renderWithProviders(<HoldOutBlocksCard />);

    await user.click(await screen.findByRole('button', { name: 'Release this block' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/forfeits its looks for good/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    expect(within(dialog).getByRole('button', { name: 'Release' })).toBeDisabled();
    await user.click(within(dialog).getByRole('checkbox', { name: /forfeit its looks/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Release' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });
    expect(body).toEqual({ password: 'secret', forfeitLook: true });
  });

  it('shows a wrong password in plain words and keeps the dialog open with the field cleared', async () => {
    server.use(
      http.post('/api/lab/holdout/blocks/:start/release', () =>
        problemResponse({ title: 'Wrong password.', code: 'auth.invalid_credentials' }, 401),
      ),
    );
    serve([block()]);
    const user = userEvent.setup();
    renderWithProviders(<HoldOutBlocksCard />);

    await user.click(await screen.findByRole('button', { name: 'Release this block' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'nope');
    await user.click(within(dialog).getByRole('button', { name: 'Release' }));

    expect(await within(dialog).findByText(/The password was not accepted/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Your password for this act/)).toHaveValue('');
  });

  it('warns in the dialog that releasing forfeits a look that is still available', async () => {
    serve([block({ looks: { strategy: fresh, model: open, system: none }, blockedBy: 'look_available' })]);
    const user = userEvent.setup();
    renderWithProviders(<HoldOutBlocksCard />);

    await user.click(await screen.findByRole('button', { name: 'Release this block' }));
    const dialog = await screen.findByRole('dialog');

    expect(within(dialog).getByText(/Releasing it now forfeits that look for good/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox')).toBeNull();
  });
});
