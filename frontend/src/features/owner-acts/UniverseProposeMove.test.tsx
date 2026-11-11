import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { createCapabilitiesFake } from '@/test/capabilitiesFake';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { MovePage } from './components/MovePage';

const key = 'ORB|15m|BUY|intraday|FIXED_RR|*|liq_high';
const baseline = 'a'.repeat(64);

function mount(refuse?: { status: number; code: string; title: string }) {
  const bodies: Record<string, unknown>[] = [];
  const caps = createCapabilitiesFake();
  caps.state.items = caps.state.items.map((i) => (i.name === 'learning_moves_ui' ? { ...i, enabled: true } : i));
  server.use(
    ...caps.handlers,
    http.post('/api/cells/universe-links/:key/propose', async ({ request }) => {
      bodies.push((await request.json()) as Record<string, unknown>);
      return refuse
        ? problemResponse({ title: refuse.title, code: refuse.code, status: refuse.status }, refuse.status)
        : HttpResponse.json({ id: 7, state: 'proposed', text: 'Proposed. Nothing is run.' });
    }),
  );
  renderRoutes([{ path: '/owner-acts/:kind/:id', element: <MovePage /> }], {
    initialPath: `/owner-acts/universe/${encodeURIComponent(key)}`,
  });
  return bodies;
}

async function fill() {
  await userEvent.type(await screen.findByLabelText(/^Baseline run key/), baseline);
  await userEvent.type(screen.getByLabelText(/^Reason/), 'looks real');
  await userEvent.type(screen.getByLabelText(/^Your password/), 'correct-horse');
}

describe('Propose a tier as a hypothesis (BK-11b)', () => {
  it('sends the run key, the reason and the password once, clears the password and says nothing is applied', async () => {
    const bodies = mount();
    await fill();
    await userEvent.click(screen.getByRole('button', { name: 'Propose' }));

    expect(await screen.findByText(/hypothesis 7 is proposed/)).toBeInTheDocument();
    expect(bodies).toEqual([
      { baselineRunKey: baseline, reason: 'looks real', window: null, password: 'correct-horse' },
    ]);
    expect(screen.getByLabelText(/^Your password/)).toHaveValue('');
    expect(screen.getAllByText(/Nothing is applied/).length).toBeGreaterThan(0);
  });

  it.each([
    [422, 'learning.not_an_observation', 'Nothing is proposed: the link is not an observation (thin: 30 trades).'],
    [409, 'learning.hypothesis_exists', 'Hypothesis 7 already proposes this tier for this cell and is still open.'],
  ])('shows the sentence of a %i refusal as sent', async (status, code, title) => {
    mount({ status, code, title });
    await fill();
    await userEvent.click(screen.getByRole('button', { name: 'Propose' }));

    expect(await screen.findByText(new RegExp(code.replace('.', '\\.')))).toBeInTheDocument();
    expect(screen.getByText(/Nothing was changed/)).toBeInTheDocument();
  });

  it('keeps the button off until the baseline run key is 64 hex characters', async () => {
    mount();
    await userEvent.type(await screen.findByLabelText(/^Baseline run key/), 'abc');
    expect(screen.getByRole('button', { name: 'Propose' })).toBeDisabled();
  });
});
