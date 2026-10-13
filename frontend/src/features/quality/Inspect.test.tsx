import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildQualityDetail, buildQualityRow, qualityId, sampleQualityRows } from '@/test/qualityFixtures';
import { createQualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';
import { pendingResponse } from '@/test/gate';

const [reliance, tcs, infy, hdfc, newco] = sampleQualityRows as [
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
];

const inspect = (symbol: string) => async () => {
  await userEvent.click(await screen.findByRole('gridcell', { name: symbol }));
  return screen.findByRole('dialog', { name: 'Inspect' });
};
const openInfy = inspect('NSE:INFY');
const section = (drawer: HTMLElement, name: string) => within(drawer).getByRole('region', { name });
const bucketRows = (drawer: HTMLElement) =>
  within(within(drawer).getByRole('table', { name: 'Stored vs recomputed' }))
    .getAllByRole('row')
    .slice(1);

const messy = buildQualityRow({
  instrumentId: qualityId(30),
  symbol: 'NSE:MESSY',
  name: 'MESSY LTD',
  status: 'fail',
  score: 60,
  checks: {
    completeness: 'warning',
    sanity: 'fail',
    spikes: 'warning',
    aggregates: 'pass',
    freshness: 'pass',
    officialDaily: 'notAvailable',
  },
  missingMinutes: 1_200,
});
const messyDetail = buildQualityDetail(messy, {
  missing: {
    total: 1_200,
    truncated: true,
    items: [
      { date: '2026-10-08', time: '09:15' },
      { date: '2026-10-08', time: '09:16' },
      { date: '2026-10-09', time: '10:00' },
    ],
  },
});

describe('Inspect drawer: header and checks', () => {
  it('opens from a row (the id is in the URL) with the status, score, time of the check and source', async () => {
    const { router, fake } = renderQuality();

    const drawer = await openInfy();

    expect(await within(drawer).findByRole('heading', { name: 'NSE:INFY' })).toBeInTheDocument();
    expect(within(drawer).getByText('INFOSYS LTD')).toBeInTheDocument();
    expect(within(drawer).getByText('Score 75 of 100')).toBeInTheDocument();
    expect(within(drawer).getByText(/^Checked/)).toHaveTextContent(
      'Checked 12 Oct 2026, 15:52 IST · 1,875 one-minute bars · Source: Dhan',
    );
    expect(router.state.location.search).toBe(`?id=${infy.instrumentId}`);
    expect(fake.calls.detail[0]).toEqual({ id: infy.instrumentId, query: '' });
  });

  it('opens from a shared link and closes with the close button', async () => {
    const { router } = renderQuality({ search: `?id=${reliance.instrumentId}` });

    const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
    expect(await within(drawer).findByRole('heading', { name: 'NSE:RELIANCE' })).toBeInTheDocument();

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close inspect' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Inspect' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toBe('');
  });

  it('has one section per check, each with its result in words', async () => {
    renderQuality();
    const drawer = await openInfy();
    await within(drawer).findByRole('region', { name: 'Completeness' });

    for (const [name, result] of [
      ['Completeness', 'Pass'],
      ['Bar sanity', 'Pass'],
      ['Spikes', 'Pass'],
      ['Aggregates vs 1-minute', 'Fail'],
      ['Freshness', 'Pass'],
      ['Official daily candle', 'Not available'],
    ] as const) {
      expect(within(section(drawer, name)).getByText(result)).toBeInTheDocument();
    }
  });

  it('completeness: expected, found and missing, a table per session and the missing minutes with "showing N of total"', async () => {
    renderQuality({ fake: createQualityFake({ rows: [tcs] }) });
    const drawer = await inspect('NSE:TCS')();

    const completeness = await within(drawer).findByRole('region', { name: 'Completeness' });
    expect(completeness).toHaveTextContent(
      'Expected 1,875 one-minute bars in the window, found 1,845; 30 missing (1.60 %).',
    );
    const sessions = within(completeness).getByRole('table', { name: 'Sessions' });
    const [header, first, second] = within(sessions).getAllByRole('row');
    expect(header).toHaveTextContent('SessionExpectedFoundMissingUnfillable');
    expect(first).toHaveTextContent('9 Oct 2026');
    expect(first).toHaveTextContent('375345');
    expect(first).toHaveTextContent('30');
    expect(second).toHaveTextContent('12 Oct 2026');
    expect(second).toHaveTextContent('3753750—');
    expect(within(completeness).getByText('Missing minutes: showing 30 of 30')).toBeInTheDocument();
    expect(within(completeness).getByText(/09:15, 09:16, 09:17/)).toBeInTheDocument();
  });

  it('completeness: says how many of the missing minutes are listed when the list is cut', async () => {
    const fake = createQualityFake({ rows: [messy], details: { [messy.instrumentId]: messyDetail } });
    renderQuality({ fake });
    const drawer = await inspect('NSE:MESSY')();

    const completeness = await within(drawer).findByRole('region', { name: 'Completeness' });
    expect(within(completeness).getByText('Missing minutes: showing 3 of 1,200')).toBeInTheDocument();
    expect(within(completeness).getByText(/09:15, 09:16/)).toBeInTheDocument();
    expect(within(completeness).getByText(/10:00/)).toBeInTheDocument();
  });

  it('says no minutes are missing when none is', async () => {
    renderQuality();
    const drawer = await inspect('NSE:RELIANCE')();

    const completeness = await within(drawer).findByRole('region', { name: 'Completeness' });
    expect(within(completeness).getByText('No minutes are missing.')).toBeInTheDocument();
  });

  it('sanity: lists the bad bars with the problem; spikes: lists the examples and whether the next bar took them back', async () => {
    const fake = createQualityFake({ rows: [messy], details: { [messy.instrumentId]: messyDetail } });
    renderQuality({ fake });
    const drawer = await inspect('NSE:MESSY')();

    const sanity = await within(drawer).findByRole('region', { name: 'Bar sanity' });
    expect(within(sanity).getByText('Fail')).toBeInTheDocument();
    expect(within(sanity).getByRole('list', { name: 'Bad bars' })).toHaveTextContent(
      '12 Oct 2026, 10:00 IST: High is below the close.',
    );
    const spikes = within(drawer).getByRole('region', { name: 'Spikes' });
    expect(spikes).toHaveTextContent('1 spike and 0 large moves');
    expect(spikes).toHaveTextContent('a bar needs a move above 1.00 % to count');
    const row = within(within(spikes).getByRole('table', { name: 'Spike examples' })).getAllByRole('row')[1];
    expect(row).toHaveTextContent('12 Oct 2026, 11:00 IST');
    expect(row).toHaveTextContent('+2.50 %');
    expect(row).toHaveTextContent('Takes it back (reversed: a bad tick)');
  });

  it('freshness: the last bar against the last closed session, and how far behind', async () => {
    renderQuality();
    const drawer = await inspect('NSE:HDFCBANK')();

    const freshness = await within(drawer).findByRole('region', { name: 'Freshness' });
    expect(within(freshness).getByText('Fail')).toBeInTheDocument();
    expect(freshness).toHaveTextContent('The last bar is 8 Oct 2026, 15:29 IST');
    expect(freshness).toHaveTextContent('Behind by 2 trading days.');
    expect(hdfc.status).toBe('fail');
  });

  it('official daily: not available yet, said plainly', async () => {
    renderQuality();
    const drawer = await openInfy();

    const official = await within(drawer).findByRole('region', { name: 'Official daily candle' });
    expect(within(official).getByText('Not available')).toBeInTheDocument();
    expect(
      within(official).getByText('Not available yet — official daily candles are not fetched.'),
    ).toBeInTheDocument();
  });

  it('an instrument never checked says so and shows no check sections', async () => {
    renderQuality();
    const drawer = await inspect('NSE:NEWCO')();

    expect(await within(drawer).findByText('This instrument has not been checked yet.')).toBeInTheDocument();
    expect(within(drawer).getByText(/^Not checked yet/)).toBeInTheDocument();
    expect(within(drawer).queryByRole('region', { name: 'Completeness' })).not.toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Check now' })).toBeInTheDocument();
    expect(newco.status).toBe('notChecked');
  });

  it('says when the checks cannot be loaded, and Retry loads them', async () => {
    const fake = createQualityFake();
    fake.failures.detail = true;
    renderQuality({ fake, search: `?id=${infy.instrumentId}` });

    const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
    expect(await within(drawer).findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.detail = false;
    await userEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));

    expect(await within(drawer).findByRole('region', { name: 'Completeness' })).toBeInTheDocument();
  });

  it('shows a loading indicator while the checks load', async () => {
    renderQuality({
      search: `?id=${infy.instrumentId}`,
      handlers: [http.get('/api/data/quality/instruments/:id', () => pendingResponse())],
    });

    const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
    expect(within(drawer).getByRole('progressbar', { name: 'Loading the checks' })).toBeInTheDocument();
  });

  it('says an instrument that is gone is gone', async () => {
    renderQuality({
      search: `?id=${infy.instrumentId}`,
      handlers: [
        http.get('/api/data/quality/instruments/:id', () =>
          HttpResponse.json(
            { title: 'The instrument does not exist.', code: 'data.instrument_not_found' },
            { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        ),
      ],
    });

    const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
    expect(await within(drawer).findByText('The instrument does not exist.')).toBeInTheDocument();
  });

  it('is wide enough for its title and the tables (760 px from md, the whole screen on a phone)', async () => {
    renderQuality({ search: `?id=${infy.instrumentId}` });

    const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
    expect(drawer).toHaveStyle({ maxWidth: '100%' });
    await within(drawer).findByRole('heading', { name: 'NSE:INFY' });
  });
});

describe('Inspect drawer: stored against recomputed', () => {
  it('lists the buckets with the differing values highlighted: stored struck through, recomputed in bold, named in words', async () => {
    renderQuality();
    const drawer = await openInfy();

    const aggregates = await within(drawer).findByRole('region', { name: 'Aggregates vs 1-minute' });
    expect(aggregates).toHaveTextContent(
      '100 buckets compared with the ones recomputed from the 1-minute bars; 2 differ.',
    );
    const chips = within(within(aggregates).getByRole('list', { name: 'Aggregates by timeframe' })).getAllByRole(
      'listitem',
    );
    expect(chips.map((c) => c.textContent)).toEqual([
      '5m: matches',
      '10m: matches',
      '15m: 1 of 10 differ',
      '30m: matches',
      '1h: 1 of 10 differ',
      '1D: matches',
      '1W: matches',
      '1M: matches',
    ]);
    expect(aggregates).toHaveTextContent('2 of 6 buckets differ');
    const rows = bucketRows(drawer);
    expect(rows).toHaveLength(6);
    const tampered = rows[3]!;
    expect(tampered).toHaveTextContent('15m');
    expect(tampered).toHaveTextContent('stored 1,529.50recomputed 1,531.25');
    expect(tampered).toHaveTextContent('stored 80,000recomputed 81,250');
    expect(tampered).toHaveTextContent('Differs: close, volume');
    expect(tampered.querySelectorAll('del')).toHaveLength(2);
    expect(tampered.querySelectorAll('ins')).toHaveLength(2);
    // A bucket that matches shows each value once.
    expect(rows[2]).toHaveTextContent('Matches');
    expect(rows[2]!.querySelectorAll('del')).toHaveLength(0);
    // A bucket the bars give that is not stored.
    expect(rows[4]).toHaveTextContent('Missing from the store');
  });

  it('"Differences only" asks again and lists just the differing buckets', async () => {
    const { fake } = renderQuality();
    const drawer = await openInfy();
    await within(drawer).findByRole('table', { name: 'Stored vs recomputed' });

    await userEvent.click(within(drawer).getByRole('switch', { name: 'Differences only' }));

    await waitFor(() => {
      expect(bucketRows(drawer)).toHaveLength(2);
    });
    expect(fake.calls.detail.at(-1)?.query).toBe('?differencesOnly=true');
    for (const row of bucketRows(drawer)) expect(row).not.toHaveTextContent('Matches');
    expect(within(drawer).getByRole('switch', { name: 'Differences only' })).toBeChecked();

    await userEvent.click(within(drawer).getByRole('switch', { name: 'Differences only' }));
    await waitFor(() => {
      expect(bucketRows(drawer)).toHaveLength(6);
    });
  });

  it('narrows to one timeframe, and counts the differences of that timeframe', async () => {
    const { fake } = renderQuality();
    const drawer = await openInfy();
    await within(drawer).findByRole('table', { name: 'Stored vs recomputed' });

    await userEvent.click(within(drawer).getByRole('combobox', { name: 'Timeframe' }));
    await userEvent.click(await screen.findByRole('option', { name: '15m' }));

    await waitFor(() => {
      expect(bucketRows(drawer)).toHaveLength(2);
    });
    expect(fake.calls.detail.at(-1)?.query).toBe('?timeframe=15m');
    expect(within(drawer).getByText(/1 of 2 buckets differ/)).toBeInTheDocument();
  });

  it('says "No differences." when an instrument that matches is asked for the differences only', async () => {
    renderQuality();
    const drawer = await inspect('NSE:RELIANCE')();
    await within(drawer).findByRole('table', { name: 'Stored vs recomputed' });

    await userEvent.click(within(drawer).getByRole('switch', { name: 'Differences only' }));

    expect(await within(drawer).findByText('No differences.')).toBeInTheDocument();
  });

  it('starts fresh for the next instrument', async () => {
    renderQuality();
    const drawer = await openInfy();
    await userEvent.click(await within(drawer).findByRole('switch', { name: 'Differences only' }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Close inspect' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Inspect' })).not.toBeInTheDocument();
    });

    const next = await inspect('NSE:RELIANCE')();

    expect(await within(next).findByRole('switch', { name: 'Differences only' })).not.toBeChecked();
  });
});
