import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildDiscipline, buildReview, mountReview } from '@/test/paperReviewFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { PaperPage } from './components/PaperPage';

pinClock('2026-10-26T09:00:00+05:30');

const open = () => renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: '/paper?tab=review' });

describe('Paper review tab', () => {
  it('shows the sections, the closed-trades table, the limits and the pinned sentences', async () => {
    mountReview();
    open();

    expect(await screen.findByRole('heading', { name: 'Where it stands' })).toBeInTheDocument();
    expect(screen.getByText(/Built now for 2026-W43 from stored records; nothing is stored/)).toBeInTheDocument();
    expect(screen.getAllByText(/Paper only: no order was placed and none exists/).length).toBeGreaterThan(0);
    const table = screen.getByRole('table', { name: 'This week' });
    expect(within(table).getByText('TCS')).toBeInTheDocument();
    // The table rows are not repeated as list lines.
    expect(screen.queryByText('- 2026-10-21 TCS SELL 5 +1.50R')).not.toBeInTheDocument();
    expect(screen.getByText(/Limits: An ETA is a straight-line guess/)).toBeInTheDocument();
    expect(screen.getByText(/unavailable: the backtest comparison \(P10-04\) is not built/)).toBeInTheDocument();
  });

  it('shows the discipline lines with n and states, and a flag line without R', async () => {
    mountReview();
    open();

    const table = await screen.findByRole('table', { name: 'Discipline lines' });
    const followed = within(table)
      .getByText(/Followed: approved at full size/)
      .closest('tr');
    if (!followed) throw new Error('no row');
    expect(within(followed).getByText('+1.00 R')).toBeInTheDocument();
    expect(within(followed).getByText(/Insufficient data: n 6 < 15/)).toBeInTheDocument();
    const flag = within(table).getByText('Approved automatically').closest('tr');
    if (!flag) throw new Error('no row');
    expect(within(flag).queryByText(/R$/)).not.toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Paper execution gap' })).toBeInTheDocument();
    expect(screen.getByText('paper has no real fills (P11)')).toBeInTheDocument();
    expect(screen.getByText(/There is no composite discipline score/)).toBeInTheDocument();
  });

  it('shows a stored week chosen from the picker', async () => {
    mountReview({
      stored: [
        buildReview({
          id: 3,
          stored: true,
          week: '2026-W42',
          builtBy: 'system:paper-review',
          sections: [
            { id: 'standing', title: 'Stored heading', lines: ['stored line'], metrics: [], limits: 'l', table: null },
          ],
        }),
      ],
    });
    open();

    await userEvent.click(await screen.findByRole('combobox', { name: 'Review week' }));
    await userEvent.click(await screen.findByRole('option', { name: '2026-W42 (stored)' }));

    expect(await screen.findByRole('heading', { name: 'Stored heading' })).toBeInTheDocument();
    expect(screen.getByText(/Stored review of 2026-W42/)).toBeInTheDocument();
  });

  it('says so when the discipline report is unavailable', async () => {
    mountReview({
      discipline: buildDiscipline({ lines: [], execution: [], unavailable: 'no paper epoch exists yet' }),
    });
    open();

    expect(await screen.findByText(/Unavailable: no paper epoch exists yet/)).toBeInTheDocument();
  });

  it('shows a loading state, then the error of a failed review, while the rest still loads', async () => {
    mountReview();
    server.use(
      http.get('/api/paper/review', () =>
        problemResponse({ title: 'paper.invalid', detail: 'The review failed.' }, 500),
      ),
    );
    open();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: 'Discipline lines' })).toBeInTheDocument();
  });

  it('has no write control', async () => {
    mountReview();
    open();

    await screen.findByRole('heading', { name: 'Where it stands' });
    expect(screen.queryByRole('button', { name: /store|save|mark|approve|apply|delete/i })).not.toBeInTheDocument();
  });
});
