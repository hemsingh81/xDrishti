import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { buildGroup, buildGroups, buildMaeMfe, nonValue, partsOf, value } from '@/test/reportFixtures';
import { renderReports, reportServer } from '@/test/reportScreen';
import { server } from '@/test/server';

describe('MAE and MFE', () => {
  it('prints the sentences from the counts and draws the grid with a name per cell', async () => {
    reportServer();
    renderReports('?view=maemfe');
    expect(await screen.findByText(/32 of 40 trades reached \+1 R at some point/)).toBeInTheDocument();
    expect(screen.getByText(/14 of 40 trades reached \+2 R/)).toBeInTheDocument();
    const grid = await screen.findByRole('grid', { name: 'Adverse excursion by result' });
    expect(
      within(grid).getByRole('gridcell', { name: /0\.25 to 0\.5 R, result 1 to 1\.5 R: 40 trades/ }),
    ).toBeInTheDocument();
  });

  it('prints only the reason for a group too small, no grid and no digit', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/mae-mfe', () =>
        HttpResponse.json(
          buildMaeMfe({
            grid: null,
            mfe: null,
            reachedMfe: [],
            excursionN: 0,
            winnersMaeP90: nonValue('insufficient', '4 trades, at least 30 needed'),
          }),
        ),
      ),
    );
    renderReports('?view=maemfe');
    expect(await screen.findByText(/4 trades, at least 30 needed/)).toBeInTheDocument();
    expect(screen.queryByRole('grid')).toBeNull();
  });

  it('prints no grid and no share when no trade has a recorded excursion', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/mae-mfe', () =>
        HttpResponse.json(
          buildMaeMfe({
            excursionN: 0,
            reachedMfe: [{ atR: 1, count: 0, share: 0 }],
            winnersMaeP90: nonValue('unavailable', 'no excursion is recorded'),
          }),
        ),
      ),
    );
    renderReports('?view=maemfe');
    expect(await screen.findByText(/no excursion is recorded/)).toBeInTheDocument();
    expect(screen.queryByRole('grid')).toBeNull();
    expect(screen.queryByText(/reached/)).toBeNull();
  });
});

describe('side, sector and calendar reports', () => {
  it('lists long and short side by side', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/groups', () =>
        HttpResponse.json(
          buildGroups('side', [
            buildGroup({ key: 'b', parts: partsOf('side', 'BUY'), n: 200 }),
            buildGroup({ key: 's', parts: partsOf('side', 'SELL'), n: 150 }),
          ]),
        ),
      ),
    );
    renderReports('?view=side');
    const table = await screen.findByRole('table', { name: 'Long against short' });
    expect(within(table).getByText('Long (BUY)')).toBeInTheDocument();
    expect(within(table).getByText('Short (SELL)')).toBeInTheDocument();
  });

  it('shows an unclassified sector as such, never a figure', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/groups', () =>
        HttpResponse.json(
          buildGroups('sector', [
            buildGroup({ key: 'a', parts: partsOf('sector', 'Banks'), expectancyLowerR: value(0.1) }),
            buildGroup({
              key: 'n',
              parts: partsOf('sector', null),
              n: 9,
              expectancyLowerR: nonValue('unavailable', 'sector not recorded for run x'),
            }),
          ]),
        ),
      ),
    );
    renderReports('?view=sectors');
    expect(await screen.findByText('Unclassified')).toBeInTheDocument();
    expect(screen.getAllByText('Banks').length).toBeGreaterThan(0);
  });

  it('opens the calendar with the hour caption', async () => {
    reportServer();
    renderReports('?view=calendar');
    expect(await screen.findByRole('region', { name: /intraday trades only/ })).toBeInTheDocument();
  });

  it('names an entry hour as the whole hour', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/groups', ({ request }) =>
        HttpResponse.json(
          new URL(request.url).searchParams.get('lens') === 'hour'
            ? buildGroups('hour', [buildGroup({ key: 'h', parts: partsOf('hour', '09'), expectancyR: value(0.2) })])
            : buildGroups('weekday', []),
        ),
      ),
    );
    renderReports('?view=calendar');
    expect(await screen.findByText('09:00 to 09:59')).toBeInTheDocument();
  });
});
