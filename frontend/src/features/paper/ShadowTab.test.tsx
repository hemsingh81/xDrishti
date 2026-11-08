import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildPhase, mountPhase } from '@/test/phaseFixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { buildShadowSummary, mountShadow } from '@/test/shadowFixtures';
import { PaperPage } from './components/PaperPage';

const open = (tab: string) =>
  renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: `/paper?tab=${tab}` });

describe('Paper Shadow tab', () => {
  it('shows the record, the states as words, the counts and the chain, with no write control', async () => {
    mountShadow();
    open('shadow');
    expect(await screen.findByText('Edge: caution')).toBeInTheDocument();
    expect(screen.getByText('Behavioural: monitoring')).toBeInTheDocument();
    expect(screen.getByText('Edge caution, behavioural monitoring.')).toBeInTheDocument();
    expect(screen.getByText(/Gross R \(before charges\)/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Shadow groups' })).toBeInTheDocument();
    expect(screen.getByText('Last digest: abc123digest')).toBeInTheDocument();
    expect(screen.getByText(/separate count/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /switch|turn|enable|save|run/i })).not.toBeInTheDocument();
  });

  it('shows the note and no state when the sequential test is not available', async () => {
    mountShadow(
      buildShadowSummary({
        sequential: {
          available: false,
          note: 'No shadow edge series yet.',
          edgeState: null,
          behaviouralState: null,
          line: null,
        },
      }),
    );
    open('shadow');
    expect(await screen.findByText('Unavailable: No shadow edge series yet.')).toBeInTheDocument();
    expect(screen.queryByText(/Edge:/)).not.toBeInTheDocument();
  });

  it('reads an unknown state as unavailable', async () => {
    mountShadow(
      buildShadowSummary({
        sequential: { available: true, note: null, edgeState: 'weird', behaviouralState: null, line: 'x' },
      }),
    );
    open('shadow');
    expect(await screen.findByText('Edge: unavailable')).toBeInTheDocument();
    expect(screen.getByText('Behavioural: unavailable')).toBeInTheDocument();
  });

  it('says so when there are no rows and never prints a non-finite number', async () => {
    const base = buildShadowSummary();
    mountShadow(
      buildShadowSummary({
        hasRows: false,
        note: 'No rows: the switch is OFF.',
        counts: { ...base.counts, edgeEffective: Number.NaN },
      }),
    );
    open('shadow');
    expect(await screen.findByText('No rows: the switch is OFF.')).toBeInTheDocument();
    expect(screen.queryByText(/NaN|Infinity/)).not.toBeInTheDocument();
  });

  it('shows an error state', async () => {
    server.use(http.get('/api/paper/shadow/summary', () => HttpResponse.json({ title: 'boom' }, { status: 500 })));
    open('shadow');
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});

describe('Sequential state chip', () => {
  it('shows on track as a neutral flag: no pass colour, no check icon', async () => {
    mountShadow(
      buildShadowSummary({
        sequential: { available: true, note: null, edgeState: 'on_track', behaviouralState: 'on_track', line: 'ok' },
      }),
    );
    open('shadow');
    const chip = (await screen.findAllByText('Edge: on track'))[0]?.closest('.MuiChip-root');
    expect(chip).not.toHaveClass('MuiChip-colorSuccess');
    expect(chip?.querySelector('[data-testid="CheckCircleOutlinedIcon"]')).toBeNull();
    expect(screen.getByText(/never a pass/)).toBeInTheDocument();
  });
});

describe('Phase tab sequential line', () => {
  it('shows the CF-09 line as a flag, never a pass', async () => {
    mountPhase(buildPhase());
    server.use(
      http.get('/api/paper/sequential', () =>
        HttpResponse.json({
          edge: { state: 'failure' },
          behavioural: { state: 'on_track' },
          line: 'Edge failure latched.',
        }),
      ),
    );
    open('phase');
    expect(await screen.findByText('Edge: failure')).toBeInTheDocument();
    expect(screen.getByText('Behavioural: on track')).toBeInTheDocument();
    expect(screen.getByText('Edge failure latched.')).toBeInTheDocument();
    expect(screen.getByText(/never a pass/)).toBeInTheDocument();
  });

  it('says unavailable on 404', async () => {
    mountPhase(buildPhase());
    open('phase');
    expect(await screen.findByText(/Sequential failure test: unavailable/)).toBeInTheDocument();
  });
});
