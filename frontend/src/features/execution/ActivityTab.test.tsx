import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const row = (i: number, over = {}) => ({
  id: `log:${i}`,
  at: '2026-10-30T03:50:00Z',
  kind: 'auto_entry',
  by: 'auto',
  accountId: null,
  text: `Placed BUY 120 TATAPOWER because the plan item ${i} was armed`,
  codes: [],
  ...over,
});
const open = () =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: '/execution?tab=activity' });

describe('Activity tab', () => {
  it('lists the plain-language rows newest first, says the log is insert-only, and has no write control', async () => {
    server.use(
      http.get('/api/execution/activity', () =>
        HttpResponse.json([row(2), row(1, { kind: 'auto_refused', by: 'system', codes: ['P3.cap'] })]),
      ),
    );
    open();
    const list = await screen.findByRole('list', { name: 'Activity' });
    const items = within(list).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Placed BUY 120 TATAPOWER because the plan item 2 was armed');
    expect(items[1]).toHaveTextContent('codes P3.cap');
    expect(screen.getByText(/insert-only/)).toBeInTheDocument();
    expect(screen.getAllByText('Automatic').length).toBeGreaterThan(0);
  });

  it('pages 50 at a time and sends the kind filter', async () => {
    const seen: string[] = [];
    server.use(
      http.get('/api/execution/activity', ({ request }) => {
        const url = new URL(request.url);
        seen.push(url.search);
        return HttpResponse.json(
          url.searchParams.get('page') === '2' ? [row(51)] : Array.from({ length: 50 }, (_, i) => row(i)),
        );
      }),
    );
    open();
    await screen.findByRole('list', { name: 'Activity' });
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(await screen.findByText(/plan item 51 was armed/)).toBeInTheDocument();
    expect(seen.at(-1)).toBe('?page=2');
    await userEvent.click(screen.getByRole('button', { name: 'Previous page' }));
    await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }));
    await userEvent.click(await screen.findByRole('option', { name: 'halt' }));
    await screen.findByRole('list', { name: 'Activity' });
    expect(seen.at(-1)).toBe('?kind=halt');
  });

  it('says so when nothing happened', async () => {
    server.use(http.get('/api/execution/activity', () => HttpResponse.json([])));
    open();
    expect(await screen.findByText('Nothing has happened for this view.')).toBeInTheDocument();
  });
});
