import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildCalendar, buildEventFilter, buildMeeting } from '@/test/eventsFixtures';
import { pendingResponse } from '@/test/gate';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { TicketEventsLine } from './components/TicketEventsLine';

// 09:30 IST on Monday 2 Nov 2026; the fixtures' calendar was read at 09:00 IST the same day.
pinClock('2026-11-02T04:00:00Z');

describe('TicketEventsLine', () => {
  it('shows loading, then the calendar fact as of its day and what the filter would do', async () => {
    server.use(
      http.get('/api/calendar/results', () =>
        HttpResponse.json(buildCalendar({ meetings: [buildMeeting({ days: 2 })] })),
      ),
      http.get('/api/selection/event-filter', () =>
        HttpResponse.json(buildEventFilter({ enabled: true, resultsWithinDays: 2 })),
      ),
    );
    renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-11-03" />);
    expect(screen.getByText('Events: loading…')).toBeInTheDocument();
    expect(
      await screen.findByText(
        /results in 2 sessions as of 2 Nov \(Wed 4 Nov 2026, intimated 28 Oct 2026\); the filter would exclude it today \(N=2\)/,
      ),
    ).toBeInTheDocument();
  });

  it('says the filter state is unavailable when that read fails', async () => {
    server.use(
      http.get('/api/calendar/results', () => HttpResponse.json(buildCalendar({ meetings: [buildMeeting()] }))),
      http.get('/api/selection/event-filter', () => problemResponse({ title: 'Down.' }, 500)),
    );
    renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-11-03" />);
    expect(await screen.findByText(/event filter state unavailable/)).toBeInTheDocument();
  });

  it('says not known from the list for a symbol in a cut-short calendar', async () => {
    server.use(http.get('/api/calendar/results', () => HttpResponse.json(buildCalendar({ truncated: true }))));
    renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-11-03" />);
    expect(await screen.findByText(/not known from the calendar list/)).toBeInTheDocument();
  });

  it('says unavailable when the calendar cannot be read', async () => {
    server.use(http.get('/api/calendar/results', () => problemResponse({ title: 'Down.' }, 500)));
    renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-11-03" />);
    expect(await screen.findByText(/unavailable — the results calendar could not be read/)).toBeInTheDocument();
  });

  it('shows nothing, and asks for nothing, on a plan for a session that is not the current or next one', () => {
    server.use(http.get('/api/calendar/results', () => pendingResponse()));
    const { container } = renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-10-20" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('keeps showing loading while the calendar has not answered', () => {
    server.use(http.get('/api/calendar/results', () => pendingResponse()));
    renderWithProviders(<TicketEventsLine symbol="INFY" forDate="2026-11-03" />);
    expect(screen.getByText('Events: loading…')).toBeInTheDocument();
  });
});
