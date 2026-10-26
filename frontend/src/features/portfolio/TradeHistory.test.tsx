import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { smallPage } from '@/test/smallPages';
import { server } from '@/test/server';
import { batchIds, buildBatch, buildUndoResult } from '@/test/tradeFixtures';
import { renderPortfolio, serveTrades, tradesPanel, warmTrades } from './testing';

// The list shows 50 imports a page in the app; here it pages by a few (the 50 is the constant `tradePageSize`).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

beforeAll(warmTrades, 60_000);

async function openHistory() {
  const view = renderPortfolio('?trades=history');
  const panel = within(await tradesPanel());
  return { ...view, panel };
}

const undone = () =>
  buildBatch({
    id: batchIds.second,
    fileName: 'old-tradebook.csv',
    status: 'undone',
    activeTrades: 0,
    undoneAt: '2026-10-11T08:00:00Z',
    createdAt: '2026-10-10T09:30:00Z',
  });

describe('Import history', () => {
  it('lists the imports with file, account, time, counts, status and what still counts', async () => {
    serveTrades({ batches: [buildBatch(), undone()] });
    const { panel } = await openHistory();

    const list = within(await panel.findByRole('list', { name: 'Imports' }));
    const [current, old] = list.getAllByRole('listitem');
    if (!current || !old) throw new Error('two imports expected');
    expect(within(current).getByText('tradebook-fy26.csv')).toBeInTheDocument();
    expect(within(current).getByText('Imported')).toBeInTheDocument();
    expect(within(current).getByText('Test Dhan Main · 12 Oct 2026, 15:00 IST · by owner')).toBeInTheDocument();
    expect(
      within(current).getByText('12 valid trades · 10 trades stored · 2 duplicates · 1 row skipped · 1 bad row'),
    ).toBeInTheDocument();
    expect(within(current).getByText('10 trades in 3 securities still count.')).toBeInTheDocument();
    expect(within(current).getByRole('button', { name: 'Undo the import of tradebook-fy26.csv' })).toBeInTheDocument();

    expect(within(old).getByText('old-tradebook.csv')).toBeInTheDocument();
    expect(within(old).getByText('Undone')).toBeInTheDocument();
    expect(within(old).getByText(/Undone 11 Oct 2026, 13:30 IST; none of its trades count now\./)).toBeInTheDocument();
    expect(within(old).queryByRole('button', { name: /Undo/ })).not.toBeInTheDocument();
  });

  it('says there is nothing yet, with the way to the first import', async () => {
    serveTrades();
    const { panel } = await openHistory();

    expect(
      await panel.findByText(/No tradebook has been imported yet\. Use Import CSV to load one/),
    ).toBeInTheDocument();
  });

  it('shows progress while the list loads', async () => {
    const fake = serveTrades();
    fake.config.holdBatches = true;
    const { panel } = await openHistory();

    expect(await panel.findByText('Loading the imports')).toBeInTheDocument();
  });

  it('shows an error with Retry', async () => {
    serveTrades();
    let fail = true;
    server.use(
      http.get('/api/portfolio/trade-batches', () =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ items: [buildBatch()], page: 1, pageSize: 50, total: 1 }),
      ),
    );
    const { panel } = await openHistory();

    expect(await panel.findByText('The imports could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(panel.getByRole('button', { name: 'Retry' }));
    expect(await panel.findByText('tradebook-fy26.csv')).toBeInTheDocument();
  });

  it('shows more imports on request', async () => {
    const many = Array.from({ length: smallPage * 2 + 5 }, (_, n) =>
      buildBatch({
        id: `00000000-0000-4000-8000-000000008${String(n + 10).padStart(3, '0')}`,
        fileName: `file-${String(n)}.csv`,
      }),
    );
    serveTrades({ batches: many });
    const { panel } = await openHistory();

    const list = () => within(panel.getByRole('list', { name: 'Imports' }));
    await panel.findByRole('list', { name: 'Imports' });
    expect(list().getAllByRole('listitem')).toHaveLength(smallPage);
    expect(panel.getByText(`Showing ${String(smallPage)} of ${String(many.length)}`)).toBeInTheDocument();

    await userEvent.click(panel.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(list().getAllByRole('listitem')).toHaveLength(smallPage * 2);
    });
    await userEvent.click(panel.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(list().getAllByRole('listitem')).toHaveLength(many.length);
    });
    expect(panel.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
  });
});

describe('Undo an import', () => {
  async function askUndo() {
    const fake = serveTrades({ batches: [buildBatch()] });
    const view = await openHistory();
    await userEvent.click(await view.panel.findByRole('button', { name: 'Undo the import of tradebook-fy26.csv' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    await dialog.findByText(/will be voided/);
    return { ...view, fake, dialog };
  }

  it('asks the API what the undo would do and says it before anything happens, starting on Cancel', async () => {
    const { dialog, fake } = await askUndo();

    expect(
      await dialog.findByText('“tradebook-fy26.csv” has 10 trades in 3 securities that still count.'),
    ).toBeInTheDocument();
    expect(
      dialog.getByText(/10 trades will be voided: kept in the list, struck out, no longer counted/),
    ).toBeInTheDocument();
    expect(
      dialog.getByText(
        /1 holding whose lots came from trades will be rebuilt from the trades that remain \(3 lots replaced\)/,
      ),
    ).toBeInTheDocument();
    expect(
      dialog.getByText(
        /Undo cannot bring back lots that an earlier rebuild replaced, including lots you had dated by hand/,
      ),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    });
    expect(fake.calls.undoPreviews).toEqual([batchIds.first]);
    expect(fake.calls.undone).toEqual([]);
  });

  it('counts the hand-dated and sync lots that would be replaced, lists what cannot be rebuilt and needs a tick', async () => {
    const fake = serveTrades({ batches: [buildBatch()] });
    fake.config.undoPreview = {
      handDatedLotsReplaced: 2,
      syncLotsReplaced: 1,
      holdingsNotRebuilt: 2,
      notRebuilt: [
        { symbol: 'TCS', holdingId: null, reason: 'negative_opening' },
        { symbol: 'INFY', holdingId: null, reason: 'ambiguous' },
      ],
    };
    const { panel } = await openHistory();
    await userEvent.click(await panel.findByRole('button', { name: 'Undo the import of tradebook-fy26.csv' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));

    expect(
      await dialog.findByText('2 hand-dated lots and 1 lot from syncs will be replaced, and they do not come back.'),
    ).toBeInTheDocument();
    expect(
      dialog.getByText(
        /2 holdings cannot be rebuilt and will keep their lots: TCS \(the remaining trades explain more shares than Dhan says you hold\), INFY \(more than one holding matches\)/,
      ),
    ).toBeInTheDocument();
    const confirm = dialog.getByRole('button', { name: 'Undo import' });
    expect(confirm).toBeDisabled();
    await userEvent.click(
      dialog.getByRole('checkbox', {
        name: /I understand that 2 lots I dated by hand will be replaced and not come back/,
      }),
    );
    expect(confirm).toBeEnabled();
  });

  it('cannot be confirmed before the dry run is there, and shows its error with Retry', async () => {
    const fake = serveTrades({ batches: [buildBatch()] });
    fake.config.undoPreviewProblem = { status: 500, body: { title: 'Boom' } };
    const { panel } = await openHistory();
    await userEvent.click(await panel.findByRole('button', { name: 'Undo the import of tradebook-fy26.csv' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));

    expect(await dialog.findByText('Boom')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Undo import' })).toBeDisabled();
    fake.config.undoPreviewProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    expect(await dialog.findByText(/10 trades will be voided/)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Undo import' })).toBeEnabled();
  });

  it('says so when the dry run finds the import undone already', async () => {
    const fake = serveTrades({ batches: [buildBatch()] });
    fake.config.undoPreview = { alreadyUndone: true };
    const { panel } = await openHistory();
    await userEvent.click(await panel.findByRole('button', { name: 'Undo the import of tradebook-fy26.csv' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));

    expect(await dialog.findByText('This import has been undone already. Nothing will change.')).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Undo import' })).not.toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Done' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Undo this import?' })).not.toBeInTheDocument();
    });
    expect(fake.calls.undone).toEqual([]);
  });

  it('does nothing on Cancel', async () => {
    const { dialog, fake, panel } = await askUndo();

    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Undo this import?' })).not.toBeInTheDocument();
    });
    expect(fake.calls.undone).toEqual([]);
    expect(panel.getByText('Imported')).toBeInTheDocument();
    // The button that asked is still there, and the keyboard is back on it.
    expect(panel.getByRole('button', { name: 'Undo the import of tradebook-fy26.csv' })).toHaveFocus();
  });

  it('undoes the import, tells the outcome in a toast and marks the import as undone', async () => {
    const { dialog, fake, panel } = await askUndo();

    await userEvent.click(dialog.getByRole('button', { name: 'Undo import' }));

    expect(
      await screen.findByText('Undid “tradebook-fy26.csv”: 10 trades voided. Rebuilt 1 holding, 3 lots replaced.'),
    ).toBeInTheDocument();
    expect(fake.calls.undone).toEqual([batchIds.first]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Undo this import?' })).not.toBeInTheDocument();
    });
    expect(await panel.findByText('Undone')).toBeInTheDocument();
    expect(panel.queryByRole('button', { name: /^Undo the import/ })).not.toBeInTheDocument();
    // The Undo button is gone with the import's state: the keyboard goes to the list, not to nowhere.
    await waitFor(() => {
      expect(panel.getByRole('list', { name: 'Imports' })).toHaveFocus();
    });
  });

  it('says so when no lots were rebuilt, or some holdings could not be', async () => {
    const { dialog } = await askUndo();
    server.use(
      http.post('/api/portfolio/trade-batches/:id/undo', () =>
        HttpResponse.json(
          buildUndoResult({
            holdingsRebuilt: 0,
            lotsReplaced: 0,
            holdingsNotRebuilt: 2,
            notRebuilt: [
              { symbol: 'TCS', holdingId: null, reason: 'negative_opening' },
              { symbol: 'INFY', holdingId: null, reason: 'ambiguous' },
            ],
          }),
        ),
      ),
    );

    await userEvent.click(dialog.getByRole('button', { name: 'Undo import' }));

    expect(
      await screen.findByText(
        'Undid “tradebook-fy26.csv”: 10 trades voided. No lots were changed. 2 holdings could not be rebuilt and kept their lots (TCS, INFY).',
      ),
    ).toBeInTheDocument();
  });

  it('handles an import that was undone already (elsewhere) without error', async () => {
    const { dialog } = await askUndo();
    server.use(
      http.post('/api/portfolio/trade-batches/:id/undo', () =>
        HttpResponse.json(
          buildUndoResult({
            alreadyUndone: true,
            voided: 0,
            holdingsRebuilt: 0,
            lotsReplaced: 0,
            batch: buildBatch({ status: 'undone', activeTrades: 0 }),
          }),
        ),
      ),
    );

    await userEvent.click(dialog.getByRole('button', { name: 'Undo import' }));

    expect(
      await screen.findByText('“tradebook-fy26.csv” had already been undone. Nothing changed.'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Undo this import?' })).not.toBeInTheDocument();
    });
  });

  it('keeps the dialog open with the reason when a sync is running, and works on the next try', async () => {
    const { dialog, fake } = await askUndo();
    fake.config.undoProblem = { status: 409, body: { title: 'Sync running', code: 'portfolio.sync_running' } };

    await userEvent.click(dialog.getByRole('button', { name: 'Undo import' }));

    expect(
      await dialog.findByText('A sync or import of this account is already running. Wait a moment for it to finish.'),
    ).toBeInTheDocument();
    fake.config.undoProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Undo import' }));
    expect(await screen.findByText(/Undid “tradebook-fy26\.csv”/)).toBeInTheDocument();
  });
});
