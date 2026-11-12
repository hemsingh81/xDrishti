import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildPhase, mountPhase } from '@/test/phaseFixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { buildShadowSummary, mountShadow } from '@/test/shadowFixtures';
import { PaperPage } from './components/PaperPage';

const open = (tab: string) =>
  renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: `/paper?tab=${tab}` });

describe('Paper Shadow tab', () => {
  it('shows the record, the states as words, the counts and the chain, with only the switch as a control', async () => {
    mountShadow();
    open('shadow');
    expect(await screen.findByText('Edge: caution')).toBeInTheDocument();
    expect(screen.getByText('Behavioural: monitoring')).toBeInTheDocument();
    expect(screen.getByText('Edge caution, behavioural monitoring.')).toBeInTheDocument();
    expect(screen.getByText(/Gross R \(before charges\)/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Shadow groups' })).toBeInTheDocument();
    expect(screen.getByText('Last digest: abc123digest')).toBeInTheDocument();
    expect(screen.getByText(/separate count/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Switch off…' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save|run/i })).not.toBeInTheDocument();
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

  describe('the owner switch', () => {
    const fill = async (user: ReturnType<typeof userEvent.setup>) => {
      await user.click(await screen.findByRole('button', { name: 'Switch on…' }));
      await user.type(await screen.findByLabelText(/reason/i), 'start the forward count');
      await user.type(screen.getByLabelText(/your password/i), 'secret');
    };

    it('sends enabled, reason and password, and clears the password after a refusal', async () => {
      mountShadow(buildShadowSummary({ enabled: false }));
      let body: unknown = null;
      server.use(
        http.put('/api/paper/shadow/settings', async ({ request }) => {
          body = await request.json();
          return problemResponse({ title: 'Nope.', code: 'auth.invalid_credentials' }, 401);
        }),
      );
      const user = userEvent.setup();
      open('shadow');
      await fill(user);
      await user.click(screen.getByRole('button', { name: 'Switch on' }));
      expect(await screen.findByText(/password was not accepted/i)).toBeInTheDocument();
      expect(body).toMatchObject({ enabled: true, reason: 'start the forward count', password: 'secret' });
      expect(screen.getByLabelText(/your password/i)).toHaveValue('');
    });

    it('asks to acknowledge a gap and then sends acknowledgeGap', async () => {
      mountShadow(buildShadowSummary({ enabled: false }));
      const bodies: { acknowledgeGap?: boolean }[] = [];
      server.use(
        http.put('/api/paper/shadow/settings', async ({ request }) => {
          const body = (await request.json()) as { acknowledgeGap?: boolean };
          bodies.push(body);
          return body.acknowledgeGap
            ? HttpResponse.json({ id: 1, enabled: true })
            : problemResponse({ title: 'Gap.', detail: '7 sessions were missed.', code: 'paper.shadow_gap' }, 409);
        }),
      );
      const user = userEvent.setup();
      open('shadow');
      await fill(user);
      await user.click(screen.getByRole('button', { name: 'Switch on' }));
      expect(await screen.findByText(/7 sessions were missed/)).toBeInTheDocument();
      await user.click(screen.getByRole('checkbox', { name: 'I acknowledge the gap' }));
      await user.type(screen.getByLabelText(/your password/i), 'secret');
      await user.click(screen.getByRole('button', { name: 'Switch on' }));
      await waitFor(() => {
        expect(bodies[1]?.acknowledgeGap).toBe(true);
      });
    });

    it.each([
      [423, 'auth.locked', /locked/i],
      [429, 'auth.rate_limited', /too many attempts/i],
      [409, 'paper.shadow_digest_unwritable', /digest folder cannot be written/i],
    ])('says in words what a %i refusal means', async (status, code, text) => {
      mountShadow(buildShadowSummary({ enabled: false }));
      server.use(http.put('/api/paper/shadow/settings', () => problemResponse({ title: 'x', code }, status)));
      const user = userEvent.setup();
      open('shadow');
      await fill(user);
      await user.click(screen.getByRole('button', { name: 'Switch on' }));
      expect(await screen.findByText(text)).toBeInTheDocument();
    });
  });
});
