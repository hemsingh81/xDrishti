import { fireEvent, screen, waitFor, within, type queries, type BoundFunctions } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildSummary } from '@/test/fixtures';
import { pendingResponse } from '@/test/gate';
import { csvFile, stubUploads } from '@/test/multipart';
import { server } from '@/test/server';
import { buildRebuild, buildReport, buildSecurity } from '@/test/tradeFixtures';
import { renderPortfolio, serveTrades, tradesPanel, warmTrades } from './testing';

stubUploads();

beforeAll(warmTrades, 60_000);

type Panel = BoundFunctions<typeof queries>;

async function previewed(report = buildReport(), setup?: (fake: ReturnType<typeof serveTrades>) => void) {
  const fake = serveTrades({ report });
  setup?.(fake);
  const view = renderPortfolio('?trades=import');
  const panel = within(await tradesPanel());
  await userEvent.setup({ applyAccept: false }).upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('d,s\n'));
  await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
  await panel.findByRole('region', { name: /Preview of/ });
  return { ...view, fake, panel };
}

const rebuildButton = (panel: Panel) => panel.getByRole('button', { name: 'Import and rebuild lots' });
const onlyButton = (panel: Panel) => panel.getByRole('button', { name: 'Import only' });

const withHandDated = (hand = 3, sync = 2) =>
  buildReport({
    securities: [
      buildSecurity({
        rebuild: buildRebuild({ lotsBefore: 5, lotsAfter: 4, handDatedLotsReplaced: hand, syncLotsReplaced: sync }),
      }),
    ],
  });

describe('Import: what happens to the lots', () => {
  it('says what each button does', async () => {
    const { panel } = await previewed();

    expect(
      panel.getByText(
        /Import only stores the new trades; your lots stay as they are\. Import and rebuild lots also replaces all the lots/,
      ),
    ).toBeInTheDocument();
    expect(panel.getByText('Rebuilding replaces 1 lot with 3 lots in 1 holding.')).toBeInTheDocument();
    expect(rebuildButton(panel)).toBeEnabled();
    expect(onlyButton(panel)).toBeEnabled();
  });

  it('counts the hand-dated and sync lots that a rebuild replaces and needs a tick before Import and rebuild', async () => {
    const { panel } = await previewed(withHandDated());

    expect(panel.getByText(/3 hand-dated lots and 2 lots from syncs will be replaced\./)).toBeInTheDocument();
    expect(rebuildButton(panel)).toBeDisabled();
    expect(onlyButton(panel)).toBeEnabled();
    const box = panel.getByRole('checkbox', {
      name: 'I understand that 3 lots I dated by hand will be replaced by lots built from the trades.',
    });
    expect(box).not.toBeChecked();

    await userEvent.click(box);
    expect(rebuildButton(panel)).toBeEnabled();
    await userEvent.click(box);
    expect(rebuildButton(panel)).toBeDisabled();
  });

  it('needs no tick when only sync lots are replaced', async () => {
    const { panel } = await previewed(withHandDated(0, 4));

    expect(panel.getByText(/4 lots from syncs will be replaced\./)).toBeInTheDocument();
    expect(panel.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(rebuildButton(panel)).toBeEnabled();
  });

  it('only counts the securities whose result is OK', async () => {
    const { panel } = await previewed(
      buildReport({
        securities: [
          buildSecurity({ rebuild: buildRebuild({ lotsBefore: 2, lotsAfter: 3, handDatedLotsReplaced: 1 }) }),
          buildSecurity({
            symbol: 'TCS',
            rebuild: buildRebuild({
              status: 'quantity_mismatch',
              reason: 'negative_opening',
              lotsBefore: 9,
              lotsAfter: 9,
              handDatedLotsReplaced: 8,
            }),
          }),
        ],
      }),
    );

    expect(
      panel.getByText(/Rebuilding replaces 2 lots with 3 lots in 1 holding\. 1 hand-dated lot will be replaced\./),
    ).toBeInTheDocument();
  });

  it('cannot rebuild when no holding can be rebuilt, but can still store the trades', async () => {
    const { panel } = await previewed(
      buildReport({ securities: [buildSecurity({ rebuild: buildRebuild({ status: 'unchanged' }) })] }),
    );

    expect(rebuildButton(panel)).toBeDisabled();
    expect(panel.getByText(/No holding can be rebuilt from this file/)).toBeInTheDocument();
    expect(onlyButton(panel)).toBeEnabled();
  });

  it('cannot import only when every trade is stored already, but still rebuilds', async () => {
    const { panel } = await previewed(
      buildReport({ file: { ...buildReport().file, newTrades: 0, alreadyImported: 10 } }),
    );

    expect(onlyButton(panel)).toBeDisabled();
    expect(panel.getByText(/Every trade of the file is stored already/)).toBeInTheDocument();
    expect(rebuildButton(panel)).toBeEnabled();
  });
});

describe('Import: doing it', () => {
  it('imports and rebuilds with the digest of the preview, then says what was done', async () => {
    const { panel, fake } = await previewed();

    await userEvent.click(rebuildButton(panel));

    const done = await panel.findByRole('region', { name: 'Import result' });
    expect(fake.calls.imports).toEqual([
      {
        fields: { accountId: expect.any(String) as string, rebuild: 'true', digest: 'digest-1' },
        file: { name: 'tradebook.csv', text: 'd,s\n' },
      },
    ]);
    expect(within(done).getByRole('heading', { name: 'Import done' })).toHaveFocus();
    expect(within(done).getByText('10 trades stored.')).toBeInTheDocument();
    expect(
      within(done).getByText(/The lots of 1 holding were rebuilt from the trades \(1 lot replaced\)/),
    ).toBeInTheDocument();
    expect(await screen.findByText('Imported 10 trades. Rebuilt 1 holding, 1 lot replaced.')).toBeInTheDocument();
    expect(panel.queryByRole('region', { name: /Preview of/ })).not.toBeInTheDocument();
  });

  it('imports only without a digest and leaves the lots alone', async () => {
    const { panel, fake } = await previewed();

    await userEvent.click(onlyButton(panel));

    const done = await panel.findByRole('region', { name: 'Import result' });
    expect(fake.calls.imports[0]?.fields).toEqual({ accountId: expect.any(String) as string, rebuild: 'false' });
    expect(within(done).getByText(/Your lots were not changed/)).toBeInTheDocument();
    expect(await screen.findByText('Imported 10 trades. Your lots were not changed.')).toBeInTheDocument();
  });

  it('says so when nothing was new', async () => {
    const { panel, fake } = await previewed(
      buildReport({ file: { ...buildReport().file, newTrades: 0, alreadyImported: 10 } }),
    );
    server.use(
      http.post('/api/portfolio/trades/import', () =>
        HttpResponse.json({
          batch: null,
          imported: 0,
          alreadyImported: 10,
          rebuilt: true,
          holdingsRebuilt: 1,
          lotsReplaced: 1,
          report: fake.config.report,
        }),
      ),
    );

    await userEvent.click(rebuildButton(panel));

    const done = await panel.findByRole('region', { name: 'Import result' });
    expect(within(done).getByRole('heading', { name: 'Nothing new to import' })).toBeInTheDocument();
    expect(within(done).getByText('The 10 trades of the file were stored already.')).toBeInTheDocument();
  });

  it('shows the batch in History afterwards and lets another file be imported', async () => {
    const { panel, router } = await previewed();
    await userEvent.click(onlyButton(panel));
    await panel.findByRole('region', { name: 'Import result' });

    await userEvent.click(panel.getByRole('button', { name: 'See it in History' }));

    expect(router.state.location.search).toBe('?trades=history');
    const list = within(await panel.findByRole('list', { name: 'Imports' }));
    expect(list.getByText('tradebook.csv')).toBeInTheDocument();
    expect(list.getByText('Imported')).toBeInTheDocument();
  });

  it('reads the totals again after a rebuild, but not after Import only (the lots did not change)', async () => {
    const { panel, client } = await previewed();
    let summaryReads = 0;
    server.use(
      http.get('/api/holdings/summary', () => {
        summaryReads += 1;
        return HttpResponse.json(buildSummary());
      }),
    );
    // Everything the import asked for has been answered (and so everything it made stale has been read again).
    const settled = () =>
      waitFor(() => {
        expect(client.isFetching()).toBe(0);
      });

    await userEvent.click(onlyButton(panel));
    await panel.findByRole('region', { name: 'Import result' });
    await settled();
    expect(summaryReads).toBe(0);

    await userEvent.click(panel.getByRole('button', { name: 'Import another file' }));
    await userEvent
      .setup({ applyAccept: false })
      .upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('d,s\n'));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));
    await panel.findByRole('region', { name: /Preview of/ });
    await userEvent.click(rebuildButton(panel));
    await panel.findByRole('region', { name: 'Import result' });

    await waitFor(() => {
      expect(summaryReads).toBeGreaterThan(0);
    });
  });

  it('disables both buttons and shows progress while the import runs, and locks every control of the form', async () => {
    const { panel } = await previewed();
    server.use(http.post('/api/portfolio/trades/import', () => pendingResponse()));

    await userEvent.click(rebuildButton(panel));

    await waitFor(() => {
      expect(onlyButton(panel)).toBeDisabled();
    });
    expect(rebuildButton(panel)).toBeDisabled();
    expect(within(rebuildButton(panel)).getByRole('progressbar')).toBeInTheDocument();
    expect(panel.getByLabelText('Account')).toBeDisabled();
    expect(panel.getByLabelText('Tradebook file (CSV)')).toBeDisabled();
    expect(panel.getByRole('button', { name: 'Remove tradebook.csv' })).toBeDisabled();
    expect(panel.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('ignores a file dropped on the form while the import runs', async () => {
    const { panel } = await previewed();
    server.use(http.post('/api/portfolio/trades/import', () => pendingResponse()));
    await userEvent.click(rebuildButton(panel));
    await waitFor(() => {
      expect(onlyButton(panel)).toBeDisabled();
    });
    const area = panel.getByLabelText('Tradebook file (CSV)').closest('label');
    if (!area) throw new Error('no drop area');

    fireEvent.drop(area, { dataTransfer: { files: [csvFile('x', 'other.csv')] } });

    expect(panel.getByText('tradebook.csv')).toBeInTheDocument();
    expect(panel.queryByText('other.csv')).not.toBeInTheDocument();
    expect(panel.getByRole('region', { name: /Preview of tradebook-fy26\.csv/ })).toBeInTheDocument();
  });
});

describe('Import: when the world changed or the server refuses', () => {
  const freshReport = () =>
    buildReport({
      digest: 'digest-2',
      securities: [buildSecurity({ rebuild: buildRebuild({ lotsBefore: 4, lotsAfter: 6, handDatedLotsReplaced: 1 }) })],
    });

  it('swaps in the fresh preview for a stale one, asks to review, and nothing is imported', async () => {
    const { panel, fake } = await previewed(withHandDated(), (f) => {
      f.config.importProblems.push({
        status: 409,
        body: {
          title: 'The preview is stale.',
          code: 'portfolio.preview_stale',
          report: freshReport(),
        },
      });
    });
    await userEvent.click(
      panel.getByRole('checkbox', { name: /I understand that 3 lots I dated by hand will be replaced/ }),
    );

    await userEvent.click(rebuildButton(panel));

    const notice = await panel.findByText(/Holdings or trades changed — please review again\. Nothing was imported/);
    expect(notice.closest('[role="alert"]')).toHaveFocus();
    expect(panel.queryByRole('region', { name: 'Import result' })).not.toBeInTheDocument();
    // The new numbers are on screen and the tick has to be given again.
    expect(
      panel.getByText('Rebuilding replaces 4 lots with 6 lots in 1 holding. 1 hand-dated lot will be replaced.'),
    ).toBeInTheDocument();
    const box = panel.getByRole('checkbox', { name: /I understand that 1 lot I dated by hand will be replaced/ });
    expect(box).not.toBeChecked();
    expect(rebuildButton(panel)).toBeDisabled();

    await userEvent.click(box);
    await userEvent.click(rebuildButton(panel));
    await panel.findByRole('region', { name: 'Import result' });
    expect(fake.calls.imports.map((upload) => upload.fields.digest)).toEqual(['digest-1', 'digest-2']);
  });

  it('reads the file again when the refusal carries no usable preview, and hides the buttons meanwhile', async () => {
    const { panel, fake } = await previewed(withHandDated(), (f) => {
      f.config.importProblems.push({
        status: 409,
        body: { title: 'The preview is stale.', code: 'portfolio.preview_stale', report: { nonsense: true } },
      });
    });
    fake.config.report = freshReport();
    await userEvent.click(panel.getByRole('checkbox', { name: /I understand that 3 lots/ }));

    await userEvent.click(rebuildButton(panel));

    expect(await panel.findByText(/Holdings or trades changed — please review again/)).toBeInTheDocument();
    expect(fake.calls.previews).toHaveLength(2);
    expect(
      panel.getByText('Rebuilding replaces 4 lots with 6 lots in 1 holding. 1 hand-dated lot will be replaced.'),
    ).toBeInTheDocument();
    expect(panel.getByRole('checkbox', { name: /I understand that 1 lot I dated by hand/ })).not.toBeChecked();
    expect(panel.queryByRole('region', { name: 'Import result' })).not.toBeInTheDocument();
    await userEvent.click(panel.getByRole('checkbox', { name: /I understand that 1 lot/ }));
    await userEvent.click(rebuildButton(panel));
    await panel.findByRole('region', { name: 'Import result' });
    expect(fake.calls.imports.map((upload) => upload.fields.digest)).toEqual(['digest-1', 'digest-2']);
  });

  it('has no Import buttons while the new preview is being read', async () => {
    const { panel, fake } = await previewed(buildReport(), (f) => {
      f.config.importProblems.push({
        status: 409,
        body: { title: 'The preview is stale.', code: 'portfolio.preview_stale' },
      });
    });
    server.use(http.post('/api/portfolio/trades/preview', () => pendingResponse()));

    await userEvent.click(rebuildButton(panel));

    await waitFor(() => {
      expect(panel.queryByRole('button', { name: 'Import and rebuild lots' })).not.toBeInTheDocument();
    });
    expect(panel.queryByRole('button', { name: 'Import only' })).not.toBeInTheDocument();
    expect(fake.calls.imports).toHaveLength(1);
  });

  it('says a sync is running and lets the owner try again', async () => {
    const { panel } = await previewed(buildReport(), (fake) => {
      fake.config.importProblems.push({
        status: 409,
        body: { title: 'Sync running', code: 'portfolio.sync_running' },
      });
    });

    await userEvent.click(rebuildButton(panel));

    expect(
      await panel.findByText('A sync or import of this account is already running. Wait a moment for it to finish.'),
    ).toBeInTheDocument();
    expect(rebuildButton(panel)).toBeEnabled();
    await userEvent.click(rebuildButton(panel));
    await panel.findByRole('region', { name: 'Import result' });
  });

  it('says the Portfolio role is off', async () => {
    const { panel } = await previewed(buildReport(), (fake) => {
      fake.config.importProblems.push({ status: 409, body: { title: 'Role off', code: 'portfolio.role_off' } });
    });

    await userEvent.click(onlyButton(panel));

    expect(await panel.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();
    expect(panel.getByRole('region', { name: /Preview of/ })).toBeInTheDocument();
  });

  it('shows the field messages of a refused form (the digest, the file)', async () => {
    const { panel } = await previewed(buildReport(), (fake) => {
      fake.config.importProblems.push({
        status: 400,
        body: {
          title: 'Invalid',
          code: 'portfolio.invalid',
          errors: { digest: ['Send the digest of the preview.'], file: ['The file changed.'] },
        },
      });
    });

    await userEvent.click(rebuildButton(panel));

    expect(await panel.findByText('The file changed. Send the digest of the preview.')).toBeInTheDocument();
  });

  it('explains a refused upload type (415) and keeps the preview', async () => {
    const { panel } = await previewed();
    server.use(
      http.post('/api/portfolio/trades/import', () =>
        HttpResponse.json({ title: 'Unsupported media type' }, { status: 415 }),
      ),
    );

    await userEvent.click(onlyButton(panel));

    expect(await panel.findByText(/The server did not accept the upload/)).toBeInTheDocument();
    expect(panel.getByRole('region', { name: /Preview of/ })).toBeInTheDocument();
  });
});
