import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { beforeCutoff, buildPlan, planItems } from '@/test/planFixtures';
import { mount } from '@/test/planScreen';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { PlansPage } from './components/PlansPage';

pinClock(beforeCutoff);

const line = (over: Record<string, unknown> = {}) => ({
  noticeId: 'sizing:7',
  source: 'sizing',
  key: 'sizing.vol_target.floor',
  basketId: null,
  oldValue: '0.5',
  newValue: '0.7',
  actor: 'owner:me@x.test',
  changedAt: '2026-10-25T10:00:00Z',
  sentence: 'Loosened since 2026-10-25: sizing.vol_target.floor 0.5 → 0.7 (by owner:me@x.test).',
  ...over,
});

function open(over: Record<string, unknown> = {}, autoDismiss = false) {
  mount();
  server.use(
    http.get('/api/plans', () => HttpResponse.json([buildPlan()])),
    http.get('/api/plans/7', () => HttpResponse.json({ ...buildPlan({ items: planItems() }), ...over })),
    http.get('/api/plans/loosening-settings', () =>
      HttpResponse.json({ autoDismiss, version: autoDismiss ? 3 : 0, hash: null, notice: 'n', history: [] }),
    ),
  );
  renderWithProviders(<PlansPage />);
}

describe('Plan screen: the loosening banner', () => {
  it('shows one line per loosened limit with its sentence as text and a Dismiss button, above the plan', async () => {
    open({
      loosenings: [
        line(),
        line({ noticeId: 'basket:2', key: 'gate.breadth_source', sentence: '<b>Loosened</b> gate.breadth_source' }),
      ],
    });

    const banner = await screen.findByLabelText('Limits loosened since the last plan');
    expect(banner).toHaveTextContent('Limits loosened since the last plan');
    expect(banner).toHaveTextContent(
      'Loosened since 2026-10-25: sizing.vol_target.floor 0.5 → 0.7 (by owner:me@x.test).',
    );
    expect(banner).toHaveTextContent('<b>Loosened</b> gate.breadth_source');
    expect(banner.querySelector('b')).toBeNull();
    expect(screen.getAllByRole('button', { name: /^Dismiss the loosening of/ })).toHaveLength(2);
    expect(screen.queryByText(/Automatic dismissal is ON/)).not.toBeInTheDocument();
  });

  it('renders nothing when there is no loosening', async () => {
    open({ loosenings: [] });

    await screen.findByText(/Undecided tickets lapse at the cutoff/);
    expect(screen.queryByLabelText('Limits loosened since the last plan')).not.toBeInTheDocument();
    expect(screen.queryByText(/could not be read/)).not.toBeInTheDocument();
  });

  it('says that lines hide by themselves while the automatic dismissal is ON', async () => {
    open({ loosenings: [line()] }, true);

    expect(await screen.findByText('Automatic dismissal is ON: each line hides after one plan.')).toBeInTheDocument();
  });

  it('says the loosenings could not be read and still draws the plan', async () => {
    open({ loosenings: [], looseningsUnavailable: true });

    expect(await screen.findByText('Loosenings could not be read.')).toBeInTheDocument();
    expect(screen.getAllByText(/Undecided tickets lapse at the cutoff/).length).toBeGreaterThan(0);
  });

  it('dismisses a line with no password and reads the plan again', async () => {
    let dismissed = false;
    open({ loosenings: [line()] });
    server.use(
      http.get('/api/plans/7', () =>
        HttpResponse.json({ ...buildPlan({ items: planItems() }), loosenings: dismissed ? [] : [line()] }),
      ),
      http.post('/api/plans/loosenings/sizing%3A7/dismiss', () => {
        dismissed = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await userEvent.click(
      await screen.findByRole('button', { name: 'Dismiss the loosening of sizing.vol_target.floor' }),
    );

    await waitFor(() => {
      expect(screen.queryByLabelText('Limits loosened since the last plan')).not.toBeInTheDocument();
    });
    expect(dismissed).toBe(true);
  });

  it('keeps the line and says so when the dismissal fails', async () => {
    open({ loosenings: [line()] });
    server.use(http.post('/api/plans/loosenings/sizing%3A7/dismiss', () => problemResponse({ title: 'Down.' }, 500)));

    await userEvent.click(
      await screen.findByRole('button', { name: 'Dismiss the loosening of sizing.vol_target.floor' }),
    );

    expect(await screen.findByText(/could not be dismissed; it stays shown/)).toBeInTheDocument();
    expect(screen.getByLabelText('Limits loosened since the last plan')).toHaveTextContent('sizing.vol_target.floor');
  });
});
