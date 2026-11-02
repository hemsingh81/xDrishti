import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildCalendar, buildMeeting } from '@/test/eventsFixtures';
import { pendingResponse } from '@/test/gate';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import ResultsTile from './components/ResultsTile';

const id = (n: number) => `00000000-0000-4000-8000-00000000010${String(n)}`;
const meeting = (n: number, symbol: string, days: number, eventSession: string) =>
  buildMeeting({ symbol, instrumentId: id(n), days, eventSession });

const serve = (calendar: ReturnType<typeof buildCalendar>) => {
  server.use(http.get('/api/calendar/results', () => HttpResponse.json(calendar)));
};

describe('Today: Results this week', () => {
  it('lists the meetings of the next 5 sessions by event session and counts the instruments without an entry', async () => {
    serve(
      buildCalendar({
        meetings: [
          meeting(3, 'WIPRO', 4, '2026-11-06'),
          meeting(1, 'INFY', 0, '2026-11-02'),
          meeting(2, 'TCS', 2, '2026-11-04'),
          meeting(4, 'LATER', 9, '2026-11-13'),
        ],
        instrumentsWithoutEntry: { count: 27, symbols: [] },
      }),
    );
    renderWithProviders(<ResultsTile />);
    const list = await screen.findByRole('list', { name: 'Results meetings in the next 5 sessions' });
    const items = within(list).getAllByRole('listitem');
    expect(items.map((li) => li.textContent)).toEqual([
      'INFY: Mon 2 Nov 2026 (today)',
      'TCS: Wed 4 Nov 2026 (in 2 sessions)',
      'WIPRO: Fri 6 Nov 2026 (in 4 sessions)',
    ]);
    expect(screen.getByText(/27 without a calendar entry/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Filings tab' })).toHaveAttribute('href', '/data?tab=filings');
  });

  it('says no meetings are known, plus the count without an entry', async () => {
    serve(buildCalendar({ instrumentsWithoutEntry: { count: 27, symbols: [] } }));
    renderWithProviders(<ResultsTile />);
    expect(await screen.findByText('No results meetings known for the next 5 sessions.')).toBeInTheDocument();
    expect(screen.getByText(/27 without a calendar entry/)).toBeInTheDocument();
  });

  it('shows loading and then an error with the problem title', async () => {
    server.use(http.get('/api/calendar/results', () => pendingResponse()));
    const { unmount } = renderWithProviders(<ResultsTile />);
    expect(screen.getByText('Loading results calendar…')).toBeInTheDocument();
    unmount();
    server.use(http.get('/api/calendar/results', () => problemResponse({ title: 'Calendar is down.' }, 500)));
    renderWithProviders(<ResultsTile />);
    expect(await screen.findByText('Calendar is down.')).toBeInTheDocument();
  });

  it('includes a meeting 4 sessions away and leaves out one 5 sessions away (days 0 to 4)', async () => {
    serve(
      buildCalendar({
        meetings: [meeting(1, 'FOUR', 4, '2026-11-06'), meeting(2, 'FIVE', 5, '2026-11-09')],
      }),
    );
    renderWithProviders(<ResultsTile />);
    const list = await screen.findByRole('list', { name: 'Results meetings in the next 5 sessions' });
    expect(within(list).getByText('FOUR')).toBeInTheDocument();
    expect(within(list).queryByText('FIVE')).not.toBeInTheDocument();
  });
});
