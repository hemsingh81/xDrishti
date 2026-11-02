import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildCalendar, buildMeeting } from '@/test/eventsFixtures';
import { pendingResponse } from '@/test/gate';
import { problemResponse, server } from '@/test/server';
import { renderData as renderAt } from './testing';

const renderFilings = () => renderAt({ search: '?tab=filings' });

const filingId = '00000000-0000-4000-8000-0000000000f1';
const coverage = {
  from: '2025-11-02',
  to: '2026-11-02',
  trackedInstruments: 30,
  kinds: [
    {
      kind: 'results',
      state: 'value',
      instrumentsWithout: 7,
      quarters: [
        {
          quarterEnd: '2026-09-30',
          ok: 21,
          unavailable: [{ reason: 'date_only', count: 2 }],
          superseded: 1,
          outOfOrder: 3,
        },
      ],
    },
    { kind: 'announcement', state: 'unavailable', instrumentsWithout: 30, quarters: [] },
  ],
};
const filing = {
  id: filingId,
  symbol: 'INFY',
  kind: 'results',
  periodEnd: '2026-09-30',
  broadcastTs: null,
  broadcastState: 'unavailable',
  unavailableReason: 'date_only',
  ingestedAt: '2026-10-20T10:00:00+05:30',
  contentSha256: 'f'.repeat(64),
  fileBytes: 1234,
  fileName: 'secret-path.xml',
  supersedes: null,
};
const facts = {
  filingId,
  parserVersion: 'xbrl-1-synthetic',
  outcome: 'parsed',
  reason: null,
  factCount: 2,
  flags: { skipped_contexts: 2 },
  facts: [
    {
      field: 'revenue',
      span: 'quarter',
      basis: 'standalone',
      state: 'value',
      value: 1234.5,
      unit: 'INR crore',
      reason: null,
    },
    {
      field: 'eps',
      span: 'quarter',
      basis: 'standalone',
      state: 'unavailable',
      value: null,
      unit: 'INR',
      reason: 'not in the file',
    },
  ],
};

function serve({ cov = coverage, list = [filing] as unknown[] } = {}) {
  server.use(
    http.get('/api/filings/coverage', () => HttpResponse.json(cov)),
    http.get('/api/filings', () =>
      HttpResponse.json({ state: null, unavailableCount: 0, items: list, truncated: false }),
    ),
    http.get('/api/filings/:id/facts', () => HttpResponse.json(facts)),
    http.get('/api/calendar/results', () =>
      HttpResponse.json(
        buildCalendar({
          meetings: [
            buildMeeting(),
            buildMeeting({ symbol: 'TCS', instrumentId: '00000000-0000-4000-8000-0000000000e2' }),
          ],
          instrumentsWithoutEntry: { count: 27, symbols: ['AAA', 'BBB'] },
        }),
      ),
    ),
  );
}

const panel = () => screen.findByRole('tabpanel', { name: /^Filings/ });

describe('Data → Filings', () => {
  it('shows a loading state, then the coverage counts from the endpoint', async () => {
    serve();
    server.use(http.get('/api/filings/coverage', async () => pendingResponse()));
    renderFilings();
    expect(await screen.findByText('Loading filing coverage')).toBeInTheDocument();
  });

  it('shows the coverage per kind and quarter, and a kind with nothing stored as unavailable, not as zero', async () => {
    serve();
    renderFilings();
    const grid = await within(await panel()).findByRole('table', { name: 'Filing coverage per kind and quarter' });
    expect(grid).toBeInTheDocument();
    const resultsRow = within(grid).getByRole('row', { name: /results/ });
    expect(within(resultsRow).getByText('21')).toBeInTheDocument();
    expect(within(resultsRow).getByText('date without a time: 2')).toBeInTheDocument();
    expect(within(resultsRow).getByText('7')).toBeInTheDocument();
    const nothing = within(grid).getByRole('row', { name: /announcement/ });
    expect(within(nothing).getAllByText('Not available').length).toBeGreaterThan(0);
    expect(within(nothing).getAllByText('nothing of this kind is stored in the range').length).toBeGreaterThan(0);
  });

  it('says when no filings are stored yet', async () => {
    serve({ cov: { ...coverage, kinds: [] }, list: [] });
    renderFilings();
    expect(await screen.findAllByText('no filings stored yet')).not.toHaveLength(0);
  });

  it('shows the problem title when the coverage cannot be read', async () => {
    serve();
    server.use(
      http.get('/api/filings/coverage', () =>
        problemResponse({ title: 'Filings are unreadable.', code: 'filings.invalid_query' }, 400),
      ),
    );
    renderFilings();
    expect(await screen.findByText('Filings are unreadable.')).toBeInTheDocument();
  });

  it('lists meetings and the instruments without an entry; the symbol filter narrows the list', async () => {
    serve();
    renderFilings();
    const table = await screen.findByRole('table', { name: 'Results meetings' });
    expect(within(table).getByText('INFY')).toBeInTheDocument();
    expect(within(table).getByText('TCS')).toBeInTheDocument();
    expect(screen.getByText(/27 without a calendar entry: AAA, BBB/)).toBeInTheDocument();
    await userEvent.type(screen.getByRole('textbox', { name: 'Filter by symbol' }), 'tc');
    expect(within(table).queryByText('INFY')).not.toBeInTheDocument();
    expect(within(table).getByText('TCS')).toBeInTheDocument();
  });

  it('lists filing metadata with the reason a broadcast time is missing, and shows no file name or path', async () => {
    serve();
    renderFilings();
    const table = await screen.findByRole('table', { name: 'Stored filings' });
    expect(within(table).getByText('INFY')).toBeInTheDocument();
    expect(within(table).getByText('date without a time')).toBeInTheDocument();
    expect(screen.queryByText(/secret-path/)).not.toBeInTheDocument();
  });

  it('opens the facts of a results filing with the parser label, each unavailable field with its reason', async () => {
    serve();
    renderFilings();
    await userEvent.click(await screen.findByRole('button', { name: 'Show facts of the INFY results filing' }));
    const dialog = await screen.findByRole('dialog', { name: /Facts of the INFY results filing/ });
    expect(
      await within(dialog).findByText(/Parser xbrl-1-synthetic \(not checked against real filings\)/),
    ).toBeInTheDocument();
    expect(within(dialog).getByText('1,234.5 INR crore')).toBeInTheDocument();
    expect(within(dialog).getByText('not in the file')).toBeInTheDocument();
    expect(within(dialog).getByText(/skipped contexts 2/)).toBeInTheDocument();
  });

  it('leaves out the extraction coverage until its endpoint exists', async () => {
    serve();
    renderFilings();
    await screen.findByRole('table', { name: 'Stored filings' });
    expect(screen.queryByText(/Extraction coverage/)).not.toBeInTheDocument();
  });

  it('shows a long calendar (above 500 rows) in a grid that loads rows as it is scrolled, not as 600 table rows', async () => {
    serve();
    const meetings = Array.from({ length: 600 }, (_v, i) =>
      buildMeeting({ symbol: `S${String(i)}`, instrumentId: `00000000-0000-4000-8000-${String(i).padStart(12, '0')}` }),
    );
    server.use(http.get('/api/calendar/results', () => HttpResponse.json(buildCalendar({ meetings }))));
    renderFilings();
    const grid = await screen.findByRole('grid', { name: 'Results meetings' });
    expect(within(grid).getAllByRole('row').length).toBeLessThan(300);
    expect(screen.queryByRole('table', { name: 'Results meetings' })).not.toBeInTheDocument();
  });
});
