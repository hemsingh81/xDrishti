import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildBlotterRow, buildPosition } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const open = (tab: string) =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: `/execution?tab=${tab}` });

describe('Execution accessibility and layout', () => {
  it('has a labelled tablist whose tab owns the panel, and a heading', async () => {
    server.use(http.get('/api/execution/blotter', () => HttpResponse.json([])));
    open('orders');
    const tab = screen.getByRole('tab', { selected: true });
    const panel = await screen.findByRole('tabpanel');
    expect(panel).toHaveAttribute('aria-labelledby', tab.id);
    expect(screen.getByRole('tablist', { name: 'Execution views' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Execution' })).toBeInTheDocument();
  });

  it('gives each table cell its header as a label and a stacking hook for narrow screens', async () => {
    server.use(http.get('/api/execution/blotter', () => HttpResponse.json([buildBlotterRow()])));
    open('orders');
    const table = await screen.findByRole('table', { name: 'Orders' });
    expect(table).toHaveClass('xd-stacked');
    const row = within(table).getByRole('row', { name: /TATAPOWER/ });
    expect(within(row).getAllByRole('rowheader')).toHaveLength(1);
    expect(within(table).getAllByRole('columnheader').length).toBeGreaterThan(8);
  });

  it('states protection and status in words, with the error banner announced', async () => {
    server.use(
      http.get('/api/execution/positions', () => HttpResponse.json([buildPosition({ protection: 'missing' })])),
    );
    open('positions');
    expect(await screen.findByRole('alert')).toHaveTextContent('Protection is missing');
    expect(screen.getByText('Protection missing')).toBeInTheDocument();
  });
});
