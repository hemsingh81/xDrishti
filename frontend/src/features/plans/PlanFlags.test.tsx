import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { beforeCutoff, buildFlags, buildPlan, planItems } from '@/test/planFixtures';
import { mount } from '@/test/planScreen';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { PlanFlags } from './components/PlanFlags';
import { PlansPage } from './components/PlansPage';
import { flagChips } from './lib/flagText';

pinClock(beforeCutoff);

const plan = { state: 'published', basis: 'x', provisional: true };

describe('PlanFlags from the stored fields', () => {
  it('draws every chip as text from the fields, never from the basis', () => {
    render(
      <PlanFlags
        plan={{
          ...plan,
          basis: 'market gate unavailable is not in this basis', // would trip the old reading
          flags: buildFlags({
            gate: 'unavailable',
            gateMissing: ['VIX', 'breadth'],
            throttle: 'cut',
            openBook: 'assumed_empty',
            riskMultiplier: 0.75,
            correlation: 'applied',
            correlationUnavailablePairs: 3,
            ruleRemovedHalf: true,
          }),
        }}
      />,
    );

    expect(screen.getByText('Market gate: unavailable (VIX, breadth not known)')).toBeInTheDocument();
    expect(screen.getByText('Throttle: cut')).toBeInTheDocument();
    expect(screen.getByText('Open book: assumed empty by owner setting')).toBeInTheDocument();
    expect(screen.getByText('Risk multiplier 0.75')).toBeInTheDocument();
    expect(screen.getByText('Correlation: 3 pair(s) not checked')).toBeInTheDocument();
    expect(screen.getByText('A rule removed half or more of its candidates')).toBeInTheDocument();
    expect(screen.getByText('Cost basis: costed, sheet not confirmed as yours')).toBeInTheDocument();
    expect(screen.queryByText(/predates structured flags/)).not.toBeInTheDocument();
    expect(screen.queryByText('Market gate unavailable')).not.toBeInTheDocument();
  });

  it('a plain plan has no risk, correlation or half-removed chip', () => {
    const labels = flagChips(buildFlags()).map((c) => c.label);
    expect(labels).toEqual([
      'Market gate: normal',
      'Throttle: clear',
      'Open book: known',
      'Cost basis: costed, sheet not confirmed as yours',
    ]);
  });

  it('a plan without flags is read from the basis text and says so', () => {
    render(<PlanFlags plan={{ ...plan, basis: 'a market gate unavailable (vix)', flags: null }} />);

    expect(screen.getByText('Market gate unavailable')).toBeInTheDocument();
    expect(screen.getByText('Read from the basis text; this plan predates structured flags')).toBeInTheDocument();
  });
});

describe('Plan banner', () => {
  const long = `A very long stored line ${'z'.repeat(700)}`;

  function open(state: string, gate: string) {
    mount();
    const flags = buildFlags({
      gate,
      banner: ['market gate unavailable (VIX not known): judge it yourself', long, '<b>bold</b> markup'],
    });
    server.use(
      http.get('/api/plans', () => HttpResponse.json([buildPlan({ state, flags })])),
      http.get('/api/plans/7', () => HttpResponse.json(buildPlan({ state, flags, items: planItems() }))),
    );
    renderWithProviders(<PlansPage />);
  }

  it('shows the full stored lines as text, markup included, and the gate sentence on a no-trade day', async () => {
    open('published', 'no_trade');

    const banner = await screen.findByLabelText('Smart rules');
    expect(banner).toHaveTextContent('Market gate: no-trade day, tickets moved to reserve');
    expect(banner).toHaveTextContent(long);
    expect(banner).toHaveTextContent('<b>bold</b> markup');
    expect(banner.querySelector('b')).toBeNull();
    expect(screen.getAllByText(/Undecided tickets lapse at the cutoff/).length).toBeGreaterThan(0);
  });

  it('keeps the stored lines but not the no-trade sentence once the plan is closed', async () => {
    open('lapsed', 'no_trade');

    const banner = await screen.findByLabelText('Smart rules');
    expect(banner).not.toHaveTextContent('tickets moved to reserve');
    expect(banner).toHaveTextContent('judge it yourself');
  });
});
