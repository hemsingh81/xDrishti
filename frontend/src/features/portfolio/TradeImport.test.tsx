import { act, fireEvent, screen, waitFor, within, type queries, type BoundFunctions } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { ids } from '@/test/fixtures';
import { holdStep, pendingResponse } from '@/test/gate';
import { appliedCss } from '@/test/css';
import { bigFile, csvFile, stubUploads } from '@/test/multipart';
import { problemResponse, server } from '@/test/server';
import { buildRebuild, buildReport, buildSecurity } from '@/test/tradeFixtures';
import { renderPortfolio, serveTrades, tradesPanel, twoPortfolioAccounts, warmTrades } from './testing';

stubUploads();

beforeAll(warmTrades, 60_000);

const user = () => userEvent.setup({ applyAccept: false });

async function openWizard() {
  const view = renderPortfolio('?trades=import');
  const panel = within(await tradesPanel());
  return { ...view, panel };
}

async function choose(panel: BoundFunctions<typeof queries>, file = csvFile('date,symbol\n')) {
  await user().upload(panel.getByLabelText('Tradebook file (CSV)'), file);
}

async function preview(panel: BoundFunctions<typeof queries>) {
  await choose(panel);
  await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
  return panel.findByRole('region', { name: /Preview of tradebook-fy26\.csv/ });
}

describe('Import CSV: the file', () => {
  it('names the account (the only one is chosen), the file input and the template link', async () => {
    serveTrades();
    const { panel } = await openWizard();

    expect(panel.getByLabelText('Account')).toHaveValue(ids.main);
    expect(panel.getByLabelText('Tradebook file (CSV)')).toHaveAttribute('type', 'file');
    expect(panel.getByRole('link', { name: 'CSV template' })).toHaveAttribute('href', '/api/portfolio/trades/template');
    expect(panel.getByRole('link', { name: 'CSV template' })).toHaveAttribute('download');
    expect(panel.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('chooses the account of the page scope when there are several, and none otherwise', async () => {
    server.use(twoPortfolioAccounts());
    serveTrades();
    const { panel, unmount } = await openWizard();
    expect(panel.getByLabelText('Account')).toHaveValue('');
    unmount();

    renderPortfolio(`?trades=import&account=${ids.swing}`);
    expect(await within(await tradesPanel()).findByLabelText('Account')).toHaveValue(ids.swing);
  });

  it('names the chosen file with its size and takes it away again', async () => {
    serveTrades();
    const { panel } = await openWizard();

    await choose(panel, csvFile('x'.repeat(2048), 'fy26.csv'));

    expect(panel.getByText('fy26.csv')).toBeInTheDocument();
    expect(panel.getByText('2 KB')).toBeInTheDocument();
    expect(panel.getByRole('button', { name: 'Preview' })).toBeEnabled();
    await userEvent.click(panel.getByRole('button', { name: 'Remove fy26.csv' }));
    expect(panel.queryByText('fy26.csv')).not.toBeInTheDocument();
    expect(panel.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('takes a file dropped on the area', async () => {
    serveTrades();
    const { panel } = await openWizard();
    const area = panel.getByLabelText('Tradebook file (CSV)').closest('label');
    if (!area) throw new Error('no drop area');

    const dropped = csvFile('a', 'dropped.csv');
    fireEvent.dragOver(area, { dataTransfer: { files: [dropped] } });
    expect(panel.getByText('Drop the file to choose it')).toBeInTheDocument();
    fireEvent.drop(area, { dataTransfer: { files: [dropped] } });

    expect(panel.getByText('dropped.csv')).toBeInTheDocument();
  });

  it.each([
    ['a spreadsheet', csvFile('x', 'trades.xlsx'), /“trades\.xlsx” is not a CSV file/],
    ['an empty file', csvFile('', 'empty.csv'), /“empty\.csv” is empty/],
    ['a file over 10 MB', bigFile(12 * 1024 * 1024 + 1, 'huge.csv'), /“huge\.csv” is 12 MB; the limit is 10 MB/],
  ])('refuses %s before uploading anything', async (_name, file, message) => {
    const fake = serveTrades();
    const { panel } = await openWizard();

    await choose(panel, file);

    expect(await panel.findByText(message)).toBeInTheDocument();
    expect(panel.getByRole('button', { name: 'Preview' })).toBeDisabled();
    expect(fake.calls.previews).toHaveLength(0);
  });
});

describe('Import CSV: the preview', () => {
  it('sends the account and the file as a form and shows nothing is stored yet', async () => {
    const fake = serveTrades();
    const { panel } = await openWizard();

    const report = await preview(panel);

    expect(fake.calls.previews).toEqual([
      { fields: { accountId: ids.main }, file: { name: 'tradebook.csv', text: 'date,symbol\n' } },
    ]);
    expect(within(report).getByRole('heading', { name: /nothing is stored yet/ })).toHaveFocus();
  });

  it('counts the rows: read, valid, new, already imported, duplicates, skipped and bad', async () => {
    serveTrades({
      report: buildReport({
        file: {
          fileName: 'tradebook-fy26.csv',
          rows: 1_240,
          parsed: 1_000,
          skipped: { total: 200, byReason: { derivatives: 150, commodity: 20, currency: 10, bse: 15, non_equity: 5 } },
          bad: [{ line: 7, reason: 'The date 31/02/2026 is not a real day.' }],
          badTotal: 40,
          duplicates: 3,
          alreadyImported: 120,
          newTrades: 880,
        },
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const count = (label: string) => report.getByText(label, { selector: 'dt' }).nextElementSibling;
    expect(count('Rows read')).toHaveTextContent('1,240');
    expect(count('Valid trades')).toHaveTextContent('1,000');
    expect(count('New')).toHaveTextContent('880');
    expect(count('Already imported')).toHaveTextContent('120');
    expect(count('Duplicates in the file')).toHaveTextContent('3');
    expect(count('Skipped')).toHaveTextContent('200');
    expect(count('Bad rows')).toHaveTextContent('40');
    const skipped = within(report.getByRole('list', { name: /Skipped/ }));
    expect(skipped.getByText('Derivatives: 150')).toBeInTheDocument();
    expect(skipped.getByText('Commodity: 20')).toBeInTheDocument();
    expect(skipped.getByText('Currency: 10')).toBeInTheDocument();
    expect(skipped.getByText('BSE only: 15')).toBeInTheDocument();
    expect(skipped.getByText('Not equity: 5')).toBeInTheDocument();
  });

  it('lists the bad rows with their line numbers behind a button, capped with the total', async () => {
    serveTrades({
      report: buildReport({
        file: {
          ...buildReport().file,
          bad: [
            { line: 7, reason: 'The date 31/02/2026 is not a real day.' },
            { line: 9, reason: 'The quantity 2.5 is not a whole number.' },
          ],
          badTotal: 340,
        },
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const button = report.getByRole('button', { name: 'Show 340 rows that could not be read' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(report.queryByText(/Line 7/)).not.toBeInTheDocument();
    await userEvent.click(button);

    const list = within(report.getByRole('list', { name: 'Rows that could not be read' }));
    expect(list.getByText('Line 7')).toBeInTheDocument();
    expect(list.getByText(/The quantity 2\.5 is not a whole number/)).toBeInTheDocument();
    expect(report.getByText('Showing the first 2 of 340.')).toBeInTheDocument();
    expect(report.getByRole('button', { name: 'Hide the rows that could not be read' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
  });

  it('shows no bad-rows button when every row could be read', async () => {
    serveTrades({ report: buildReport({ file: { ...buildReport().file, bad: [], badTotal: 0 } }) });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    expect(report.queryByRole('button', { name: /could not be read/ })).not.toBeInTheDocument();
  });

  it('shows the securities table with lots, opening quantity and realised profit before → after', async () => {
    serveTrades({
      report: buildReport({
        securities: [
          buildSecurity({
            rebuild: buildRebuild({
              lotsBefore: 2,
              lotsAfter: 5,
              openingQty: { before: 25, after: 10 },
              realisedPnl: 1_250,
              adjustedFor: 2,
            }),
          }),
        ],
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const table = within(report.getByRole('table', { name: 'Securities in the file' }));
    expect(table.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Security',
      'Status',
      'Trades',
      'Lots',
      'Opening qty',
      'Realised P&L',
    ]);
    const row = within(table.getByRole('row', { name: /INFY/ }));
    expect(row.getByRole('rowheader')).toHaveTextContent('INFYINE009A01021');
    expect(row.getByText('3 buy · 2 sell')).toBeInTheDocument();
    expect(row.getByText('10 Jun 2024')).toBeInTheDocument();
    expect(row.getByText('5 Oct 2026')).toBeInTheDocument();
    expect(row.getByText('2 → 5')).toBeInTheDocument();
    expect(row.getByText('2 to 5')).toBeInTheDocument();
    expect(row.getByText('25 → 10')).toBeInTheDocument();
    expect(row.getByText('+₹1,250')).toBeInTheDocument();
    expect(row.getByText('OK')).toBeInTheDocument();
    expect(row.getByText('Adjusted for 2 corporate actions')).toBeInTheDocument();
  });

  it.each([
    ['unchanged', buildRebuild({ status: 'unchanged' }), 'Unchanged', null],
    [
      'a mismatch where the trades explain too many shares',
      buildRebuild({ status: 'quantity_mismatch', reason: 'negative_opening', q: 20, net: 30, realisedPnl: null }),
      'Quantity mismatch',
      /explain more shares than Dhan says you hold.*Dhan shows 20 shares; the trades add up to a net 30\. Nothing is applied/,
    ],
    [
      'a mismatch where a sell is too big',
      buildRebuild({ status: 'quantity_mismatch', reason: 'sell_exceeds_open', realisedPnl: null }),
      'Quantity mismatch',
      /A sell is larger than the shares held at that point/,
    ],
    [
      'a mismatch after a split or bonus',
      buildRebuild({ status: 'quantity_mismatch', reason: 'fractional_after_adjustment', realisedPnl: null }),
      'Quantity mismatch',
      /After a split or bonus the quantity is not a whole number/,
    ],
    [
      'a security that is not held',
      buildRebuild({ status: 'no_holding', lotsBefore: 0, lotsAfter: 0, realisedPnl: null }),
      'Not held',
      /does not hold this security now: its trades are stored, not applied/,
    ],
    [
      'an ambiguous security',
      buildRebuild({ status: 'ambiguous', realisedPnl: null }),
      'Ambiguous',
      /More than one holding of the account matches this security/,
    ],
  ])('shows %s with its word and its reason', async (_name, rebuild, label, reason) => {
    serveTrades({ report: buildReport({ securities: [buildSecurity({ rebuild })] }) });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const row = within(report.getByRole('row', { name: /INFY/ }));
    expect(row.getByText(label)).toBeInTheDocument();
    if (reason) expect(row.getByText(reason)).toBeInTheDocument();
    expect(report.getByText(/OK: the lots would be rebuilt from the trades/)).toBeInTheDocument();
  });

  it('keeps the securities that are not recognised apart, and says that not-held trades are stored, not applied', async () => {
    serveTrades({
      report: buildReport({
        securities: [
          buildSecurity(),
          buildSecurity({
            symbol: 'ODDBALL',
            isin: null,
            matched: false,
            holdingId: null,
            trades: 2,
            rebuild: buildRebuild({ status: 'no_holding', realisedPnl: null }),
          }),
        ],
        notHeld: { securities: 4, trades: 12 },
        unmatched: { securities: 1, trades: 2 },
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const table = within(report.getByRole('table', { name: 'Securities in the file' }));
    expect(table.queryByText('ODDBALL')).not.toBeInTheDocument();
    expect(report.getByText(/12 trades for 4 securities not held are stored, not applied/)).toBeInTheDocument();
    expect(report.getByText(/2 trades for 1 security of those are not NSE shares or ETFs/)).toBeInTheDocument();
    await userEvent.click(report.getByRole('button', { name: 'Show the 1 security not recognised' }));
    expect(
      within(report.getByRole('list', { name: 'Securities not recognised' })).getByText('ODDBALL'),
    ).toBeInTheDocument();
  });

  it('notes when the cost basis of the trades differs from the broker’s average, with the reason in a tooltip', async () => {
    serveTrades({
      report: buildReport({
        securities: [
          buildSecurity({ rebuild: buildRebuild({ openCostAfter: 38_766, brokerCost: 40_000, costDelta: -1_234 }) }),
          buildSecurity({
            symbol: 'TCS',
            isin: null,
            rebuild: buildRebuild({
              openCostAfter: 40_000.4,
              brokerCost: 40_000,
              costDelta: 0.4,
              costNote: 'opening_cost_fallback',
            }),
          }),
        ],
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const infy = within(report.getByRole('row', { name: /INFY/ }));
    const note = infy.getByText('Cost basis from your trades is ₹1,234 lower than the broker’s cost');
    act(() => {
      note.focus();
    });
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/first-in first-out.*weighted average cost/);
    // A difference below a rupee is rounding and is not shown; the fallback of the opening lot's cost is.
    const tcs = within(report.getByRole('row', { name: /TCS/ }));
    expect(tcs.queryByText(/Cost basis from your trades is/)).not.toBeInTheDocument();
    expect(tcs.getByText(/Cost approximated: the opening lot’s cost could not be derived/)).toBeInTheDocument();
  });

  it('shows no cost note when the cost agrees', async () => {
    serveTrades();
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    expect(report.queryByText(/Cost basis from your trades is/)).not.toBeInTheDocument();
    expect(report.queryByText(/Cost approximated/)).not.toBeInTheDocument();
  });

  it('says how many unmatched securities are listed when the report caps them', async () => {
    const listed = Array.from({ length: 3 }, (_, n) =>
      buildSecurity({
        symbol: `ODD${String(n)}`,
        isin: null,
        matched: false,
        holdingId: null,
        trades: 1,
        rebuild: buildRebuild({ status: 'no_holding', realisedPnl: null }),
      }),
    );
    serveTrades({
      report: buildReport({
        securities: [buildSecurity(), ...listed],
        notHeld: { securities: 500, trades: 600 },
        unmatched: { securities: 450, trades: 450 },
        securitiesTotal: 452,
        unmatchedListed: 3,
      }),
    });
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    await userEvent.click(report.getByRole('button', { name: 'Show the 3 securities not recognised' }));
    expect(report.getByText('Showing the first 3 of 450.')).toBeInTheDocument();
  });

  it('shows a progress indicator while the preview is read, and the form stays labelled', async () => {
    serveTrades();
    server.use(http.post('/api/portfolio/trades/preview', () => pendingResponse()));
    const { panel } = await openWizard();
    await choose(panel);

    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(await panel.findByRole('progressbar')).toBeInTheDocument();
    expect(panel.getByLabelText('Account')).toBeDisabled();
    expect(panel.getByLabelText('Tradebook file (CSV)')).toBeDisabled();
  });

  it('aborts the preview that is still being read when the drawer is closed', async () => {
    serveTrades();
    // Answered at the latest when the test ends (see gate.ts), so the request never hangs.
    server.use(
      http.post('/api/portfolio/trades/preview', async () => {
        await holdStep();
        return HttpResponse.json(buildReport());
      }),
    );
    const fetched = vi.spyOn(globalThis, 'fetch');
    const { panel } = await openWizard();
    await choose(panel);
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    expect(await panel.findByRole('progressbar')).toBeInTheDocument();
    const sent = fetched.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.url.endsWith('/trades/preview'));
    expect(sent).toHaveLength(1);
    expect(sent[0]?.signal.aborted).toBe(false);

    await userEvent.click(panel.getByRole('button', { name: 'Close trades' }));

    await waitFor(() => {
      expect(sent[0]?.signal.aborted).toBe(true);
    });
  });

  it('shows Retry after a failed preview that reads the file again', async () => {
    const fake = serveTrades();
    fake.config.previewProblem = { status: 500, body: { title: 'The server hiccuped.' } };
    const { panel } = await openWizard();
    await choose(panel);
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    expect(await panel.findByText('The server hiccuped.')).toBeInTheDocument();

    fake.config.previewProblem = null;
    await userEvent.click(panel.getByRole('button', { name: 'Retry' }));

    expect(await panel.findByRole('region', { name: /Preview of/ })).toBeInTheDocument();
    expect(fake.calls.previews).toHaveLength(2);
    expect(panel.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('drops the preview when another account is chosen', async () => {
    server.use(twoPortfolioAccounts());
    serveTrades();
    const { panel } = await openWizard();
    await userEvent.selectOptions(await panel.findByLabelText('Account'), ids.main);
    await preview(panel);

    await userEvent.selectOptions(panel.getByLabelText('Account'), ids.swing);

    expect(panel.queryByRole('region', { name: /Preview of/ })).not.toBeInTheDocument();
    expect(panel.queryByRole('button', { name: 'Import only' })).not.toBeInTheDocument();
    expect(panel.getByText('tradebook.csv')).toBeInTheDocument();
  });

  it('puts the securities table in a labelled region that the keyboard can scroll, with the security kept in view', async () => {
    serveTrades();
    const { panel } = await openWizard();
    const report = within(await preview(panel));

    const scroller = report.getByRole('region', { name: 'Securities in the file, scrolls sideways' });
    expect(scroller).toHaveAttribute('tabindex', '0');
    const css = appliedCss(report.getByRole('rowheader', { name: /INFY/ }));
    expect(css.base).toMatch(/position: sticky/);
    expect(css.base).toMatch(/left: 0/);
  });

  it('shows what the API says about an unreadable file at the file', async () => {
    const fake = serveTrades();
    fake.config.previewProblem = {
      status: 400,
      body: {
        title: 'The file cannot be read.',
        code: 'portfolio.invalid',
        errors: { file: ['Missing the columns: price. Accepted names: price, trade price, rate.'] },
      },
    };
    const { panel } = await openWizard();
    await choose(panel);
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(await panel.findByText(/Missing the columns: price\. Accepted names/)).toBeInTheDocument();
    expect(panel.queryByRole('region', { name: /Preview of/ })).not.toBeInTheDocument();
  });

  it('shows a server error with its words and can be run again', async () => {
    const fake = serveTrades();
    fake.config.previewProblem = { status: 409, body: { title: 'Role off', code: 'portfolio.role_off' } };
    const { panel } = await openWizard();
    await choose(panel);
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(await panel.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();

    fake.config.previewProblem = null;
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    expect(await panel.findByRole('region', { name: /Preview of/ })).toBeInTheDocument();
    expect(panel.queryByText('Switch on the Portfolio role of this account first.')).not.toBeInTheDocument();
  });

  it('explains a refused upload type (415) and a network failure', async () => {
    serveTrades();
    server.use(http.post('/api/portfolio/trades/preview', () => problemResponse({ title: 'Unsupported' }, 415)));
    const { panel } = await openWizard();
    await choose(panel);
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    expect(await panel.findByText(/The server did not accept the upload/)).toBeInTheDocument();

    server.use(http.post('/api/portfolio/trades/preview', () => HttpResponse.error()));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    expect(
      await panel.findByText('Could not reach the server. Check your connection and try again.'),
    ).toBeInTheDocument();
  });

  it('forgets the preview when another file or account is chosen', async () => {
    serveTrades();
    server.use(twoPortfolioAccounts());
    const { panel } = await openWizard();
    await panel.findByLabelText('Account');
    await userEvent.selectOptions(panel.getByLabelText('Account'), ids.main);
    await preview(panel);

    await choose(panel, csvFile('other', 'other.csv'));

    expect(panel.queryByRole('region', { name: /Preview of/ })).not.toBeInTheDocument();
  });
});
