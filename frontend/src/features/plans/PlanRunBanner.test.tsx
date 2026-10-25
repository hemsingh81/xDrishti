import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { buildPlan, buildRun } from '@/test/planFixtures';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { PlansPage } from './components/PlansPage';
import { unpublishedRun } from './lib/unpublishedRun';

pinClock('2026-10-27T14:00:00Z');

describe('unpublishedRun', () => {
  it('is the newest failed attempt only while nothing was published since', () => {
    const failed = buildRun();
    expect(unpublishedRun([failed], [])).toBe(failed);
    expect(unpublishedRun([buildRun({ outcome: 'published', planId: 9 }), buildRun({ attempt: 1 })], [])).toBeNull();
    expect(unpublishedRun([failed], [{ forDate: '2026-10-28' }])).toBeNull();
    expect(unpublishedRun([failed], [{ forDate: '2026-10-26' }])).toBe(failed);
    expect(unpublishedRun([], [])).toBeNull();
  });
});

describe('Plan run banner', () => {
  it('says why no plan was published, with the gate id, the attempt and who started it', async () => {
    server.use(http.get('/api/plan-runs', () => HttpResponse.json([buildRun({ manual: true })])));
    server.use(http.get('/api/plans', () => HttpResponse.json([buildPlan({ forDate: '2026-10-26', id: 6 })])));
    server.use(http.get('/api/plans/6', () => HttpResponse.json(buildPlan({ id: 6, forDate: '2026-10-26' }))));
    renderWithProviders(<PlansPage />);

    expect(
      await screen.findByText(
        /No plan was published for 28 Oct 2026: the data_quality gate refused \(attempt 2 at 18:42 IST, started manually\): Bars of the last session are missing\./,
      ),
    ).toBeInTheDocument();
  });

  it('is gone when a plan exists for that session, and with no attempt the screen says no plan yet', async () => {
    server.use(http.get('/api/plan-runs', () => HttpResponse.json([buildRun()])));
    server.use(http.get('/api/plans', () => HttpResponse.json([buildPlan({ forDate: '2026-10-28' })])));
    server.use(http.get('/api/plans/7', () => HttpResponse.json(buildPlan({ forDate: '2026-10-28' }))));
    const first = renderWithProviders(<PlansPage />);
    await screen.findByRole('list', { name: 'Plans by date' });
    expect(screen.queryByText(/No plan was published/)).not.toBeInTheDocument();
    first.unmount();

    server.use(http.get('/api/plan-runs', () => HttpResponse.json([])));
    server.use(http.get('/api/plans', () => HttpResponse.json([])));
    renderWithProviders(<PlansPage />);
    expect(await screen.findByText(/No plan yet/)).toBeInTheDocument();
  });

  it('shows the reason as text, markup included', async () => {
    server.use(http.get('/api/plan-runs', () => HttpResponse.json([buildRun({ reason: '<i>bad</i> data' })])));
    renderWithProviders(<PlansPage />);

    expect(await screen.findByText(/<i>bad<\/i> data/)).toBeInTheDocument();
  });
});
