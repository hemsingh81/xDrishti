import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ids } from '@/test/fixtures';
import { csvFile, stubUploads } from '@/test/multipart';
import {
  buildCosts,
  buildDematPreview,
  buildDpPreview,
  buildFileResult,
  buildGtrPreview,
  buildTradeListPreview,
} from '@/test/reportImportFixtures';
import { findToast } from '@/test/toast';
import { buildRebuild, buildReport, buildSecurity } from '@/test/tradeFixtures';
import { cardOf, dematFile, dpFile, gtrFile, openImportFiles, previewFiles, sampleCsv } from './reportImportTesting';
import { warmReportImport } from './testing';

stubUploads();

beforeAll(warmReportImport, 60_000);

const gtrName = 'global-transaction-fy26.csv';

const importAll = (dialog: Parameters<typeof cardOf>[0], name: RegExp | string) =>
  userEvent.click(dialog.getByRole('button', { name }));

describe('Import files: importing', () => {
  it('sends every reviewed file with its digest and says what each one did', async () => {
    const { dialog, fake } = await openImportFiles({
      previews: [buildGtrPreview(), buildDpPreview(), buildDematPreview()],
    });
    await previewFiles(dialog, [gtrFile(), dpFile(), dematFile()]);

    await importAll(dialog, 'Import 3 files');
    const heading = await dialog.findByRole('heading', { name: 'Import result' });

    expect(fake.calls.imports).toHaveLength(1);
    expect(fake.calls.imports[0]).toMatchObject({
      files: [gtrName, 'dp-charges-fy26.xls', 'demat-holding.csv'],
      digests: ['gtr-digest-1', 'dp-digest-1', 'demat-digest-1'],
      acceptOtherAccount: [],
      rebuild: 'true',
      fields: { accountId: ids.main },
    });
    expect(heading).toHaveFocus();
    expect(
      within(cardOf(dialog, gtrName).getByRole('region', { name: `Result for ${gtrName}` })).getByText(
        '30 trades stored. The lots of 1 holding were rebuilt (1 lot replaced).',
      ),
    ).toBeInTheDocument();
    expect(
      cardOf(dialog, 'dp-charges-fy26.xls').getByText('1 charge stored (₹12.50); 1 line in the ledger already.'),
    ).toBeInTheDocument();
    expect(
      cardOf(dialog, 'demat-holding.csv').getByText('3 names remembered (3 known already). No holding was changed.'),
    ).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'See it in History' })).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: /^Import \d/ })).not.toBeInTheDocument();
  });

  it('tells the owner in a toast', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview(), buildDpPreview()] });
    await previewFiles(dialog, [gtrFile(), dpFile()]);

    await importAll(dialog, 'Import 2 files');

    const toast = await findToast();
    expect(toast).toHaveRole('status');
    expect(toast).toHaveTextContent('Imported 30 trades. Rebuilt 1 holding, 1 lot replaced.');
    expect(toast).toHaveTextContent('Imported 1 charge stored (₹12.50)');
  });

  it('says what will be stored and what a rebuild replaces before anything is sent', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview(), buildDpPreview()] });
    await previewFiles(dialog, [gtrFile(), dpFile()]);

    expect(dialog.getByText('30 trades, 1 depository charge (₹12.50) will be stored.')).toBeInTheDocument();
    expect(dialog.getByText('Rebuilding replaces 1 lot with 3 lots in 1 holding.')).toBeInTheDocument();
  });

  it('can import and leave the lots alone', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    await userEvent.click(dialog.getByRole('checkbox', { name: 'Rebuild the lots from the trades' }));
    expect(dialog.getByText(/The trades are stored and your lots stay as they are/)).toBeInTheDocument();
    await importAll(dialog, 'Import 1 file');

    await dialog.findByRole('heading', { name: 'Import result' });
    expect(fake.calls.imports[0]?.rebuild).toBe('false');
    expect(dialog.getByText(/Your lots were not changed/)).toBeInTheDocument();
  });

  it('needs a tick before it replaces lots the owner dated by hand', async () => {
    const report = buildReport({
      securities: [buildSecurity({ rebuild: buildRebuild({ handDatedLotsReplaced: 2, syncLotsReplaced: 1 }) })],
    });
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview({ trades: report })] });
    await previewFiles(dialog, [gtrFile()]);

    expect(dialog.getByText(/2 hand-dated lots and 1 lot from syncs will be replaced/)).toBeInTheDocument();
    const tick = dialog.getByRole('checkbox', {
      name: 'I understand that 2 lots I dated by hand will be replaced by lots built from the trades.',
    });
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeDisabled();
    await userEvent.click(tick);
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();
    await importAll(dialog, 'Import 1 file');

    await dialog.findByRole('heading', { name: 'Import result' });
    expect(fake.calls.imports[0]?.rebuild).toBe('true');
  });

  it('does not count a file with nothing new, and says so', async () => {
    const trades = buildReport({
      file: { ...buildReport().file, newTrades: 0, alreadyImported: 10 },
    });
    const { dialog } = await openImportFiles({
      previews: [buildGtrPreview({ trades }), buildTradeListPreview(), buildDpPreview()],
    });
    await previewFiles(dialog, [gtrFile(), csvFile(sampleCsv, 'tradebook-fy26.csv'), dpFile()]);

    expect(
      cardOf(dialog, gtrName).getByText(/Nothing new: everything in this file is stored or known already/),
    ).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 2 files' })).toBeEnabled();
  });

  it('shows a file the server found nothing new in, and one it did not import', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview(), buildDematPreview()] });
    fake.config.queued.set('dp-charges-fy26.xls', [
      buildFileResult({
        fileName: 'dp-charges-fy26.xls',
        kind: 'dp_charges',
        status: 'nothing_new',
        costs: { batch: null, imported: 0, amount: 0, alreadyImported: 4, alreadyInLedger: 0, namesLearned: 0 },
      }),
    ]);
    fake.config.queued.set('demat-holding.csv', [
      buildFileResult({
        fileName: 'demat-holding.csv',
        kind: 'demat_holding',
        status: 'rejected',
        error: { code: 'portfolio.conflict', message: 'The account changed while the file was being read.' },
      }),
    ]);
    await previewFiles(dialog, [dpFile(), dematFile()]);

    await importAll(dialog, 'Import 2 files');

    await dialog.findByRole('heading', { name: 'Import result' });
    expect(
      cardOf(dialog, 'dp-charges-fy26.xls').getByRole('heading', { name: 'Nothing new to import' }),
    ).toBeInTheDocument();
    expect(
      cardOf(dialog, 'dp-charges-fy26.xls').getByText(/Nothing new: 4 lines imported already/),
    ).toBeInTheDocument();
    const rejected = cardOf(dialog, 'demat-holding.csv');
    expect(rejected.getByRole('heading', { name: 'Not imported' })).toBeInTheDocument();
    expect(rejected.getByRole('alert')).toHaveTextContent('The account changed while the file was being read.');
    expect((await findToast()).textContent).toMatch(/1 file had nothing new\. 1 file not imported\./);
  });

  it('keeps the files and shows the problem when the whole request is refused', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview()] });
    fake.config.importProblems.push({
      status: 409,
      body: { title: 'Switch on the Portfolio role', code: 'portfolio.role_off' },
    });
    await previewFiles(dialog, [dpFile()]);

    await importAll(dialog, 'Import 1 file');

    expect(await dialog.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();
    expect(dialog.queryByRole('heading', { name: 'Import result' })).not.toBeInTheDocument();
  });

  it('imports a file again when a sync was running, and then only that file', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview(), buildDematPreview()] });
    fake.config.queued.set('dp-charges-fy26.xls', [
      buildFileResult({
        fileName: 'dp-charges-fy26.xls',
        kind: 'dp_charges',
        status: 'rejected',
        error: { code: 'portfolio.sync_running', message: 'A sync is running.' },
      }),
    ]);
    await previewFiles(dialog, [dpFile(), dematFile()]);

    await importAll(dialog, 'Import 2 files');

    expect(
      await dialog.findByText(
        'A sync or import of this account is running. Nothing was stored; try again in a moment.',
      ),
    ).toBeInTheDocument();
    await importAll(dialog, 'Import 1 file');
    await waitFor(() => {
      expect(fake.calls.imports).toHaveLength(2);
    });
    expect(fake.calls.imports[1]?.files).toEqual(['dp-charges-fy26.xls']);
    expect(await cardOf(dialog, 'dp-charges-fy26.xls').findByText(/1 charge stored/)).toBeInTheDocument();
  });

  it('asks for the files to be read again when the server refuses a digest', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview()] });
    fake.config.queued.set('dp-charges-fy26.xls', [
      buildFileResult({
        fileName: 'dp-charges-fy26.xls',
        kind: 'dp_charges',
        status: 'rejected',
        error: { code: 'portfolio.invalid', message: 'The digest is not the one of the preview.', field: 'digest' },
      }),
    ]);
    await previewFiles(dialog, [dpFile()]);

    await importAll(dialog, 'Import 1 file');

    expect(
      await dialog.findByText(/The preview is out of date\. Nothing was stored; preview the files again/),
    ).toBeInTheDocument();
    expect(dialog.getByText(/press Preview to read the files again before importing/)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeDisabled();
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));
    await waitFor(() => {
      expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();
    });
  });
});

describe('Import files: a file of another account', () => {
  const otherAccount = { code: 'other_account', message: 'x' };

  it('is imported only when the owner ticks the box, and the tick is sent', async () => {
    const { dialog, fake } = await openImportFiles({
      previews: [buildDpPreview(), buildGtrPreview({ warnings: [otherAccount] })],
    });
    await previewFiles(dialog, [dpFile(), gtrFile()]);

    expect(
      cardOf(dialog, gtrName).getByText('This file looks like it belongs to another Dhan account, not Test Dhan Main.'),
    ).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();
    await userEvent.click(
      cardOf(dialog, gtrName).getByRole('checkbox', { name: `Import ${gtrName} into Test Dhan Main anyway` }),
    );
    expect(dialog.getByRole('button', { name: 'Import 2 files' })).toBeEnabled();
    await importAll(dialog, 'Import 2 files');

    await dialog.findByRole('heading', { name: 'Import result' });
    expect(fake.calls.imports[0]).toMatchObject({ files: ['dp-charges-fy26.xls', gtrName], acceptOtherAccount: [1] });
  });

  it('does not show a tick when the file raises no warning', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    expect(dialog.queryByRole('checkbox', { name: /anyway/ })).not.toBeInTheDocument();
  });

  it('asks for the tick when the server refuses a file that showed no warning', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.queued.set(gtrName, [
      buildFileResult({
        fileName: gtrName,
        kind: 'global_transaction_report',
        status: 'rejected',
        error: { code: 'portfolio.other_account', message: 'This report belongs to another account.' },
      }),
    ]);
    await previewFiles(dialog, [gtrFile()]);

    await importAll(dialog, 'Import 1 file');

    const card = cardOf(dialog, gtrName);
    expect(await card.findByText('This report belongs to another account.')).toBeInTheDocument();
    const tick = card.getByRole('checkbox', { name: `Import ${gtrName} into Test Dhan Main anyway` });
    expect(dialog.getByText('There is nothing to import.')).toBeInTheDocument();
    await userEvent.click(tick);
    await importAll(dialog, 'Import 1 file');

    await dialog.findByRole('heading', { name: 'Import result' });
    expect(fake.calls.imports[1]?.acceptOtherAccount).toEqual([0]);
  });
});

describe('Import files: a preview that went stale', () => {
  const stale = (fileName: string, kind: string, preview: ReturnType<typeof buildGtrPreview>) =>
    buildFileResult({
      fileName,
      kind,
      status: 'rejected',
      error: { code: 'portfolio.preview_stale', message: 'Holdings changed.' },
      preview,
    });

  it('swaps in the fresh preview of that file only, keeps the choices that still apply and confirms again', async () => {
    const fresh = buildGtrPreview({ digest: 'gtr-digest-3' });
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview(), buildDpPreview()] });
    fake.config.queued.set(gtrName, [stale(gtrName, 'global_transaction_report', fresh)]);
    await previewFiles(dialog, [gtrFile(), dpFile()]);
    // A choice that still applies (the name is in the fresh preview) is kept.
    await userEvent.click(cardOf(dialog, gtrName).getByRole('button', { name: /^Choose a match for NOVA OXIDES/ }));
    const picker = within(await screen.findByRole('dialog', { name: 'Choose what this name means' }));
    await userEvent.type(picker.getByRole('combobox', { name: 'Holding or instrument' }), 'infos');
    await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    fake.config.afterChoices.set(gtrName, buildGtrPreview({ digest: 'gtr-digest-2' }));
    await userEvent.click(dialog.getByRole('button', { name: 'Preview 2 files' }));
    await dialog.findByRole('heading', { name: /Preview of 2 files/ });

    await importAll(dialog, 'Import 2 files');

    const notice = await dialog.findByText(/Holdings, trades or remembered matches changed — please review again/);
    expect(notice.closest('[role="alert"]')).toHaveFocus();
    expect(cardOf(dialog, 'dp-charges-fy26.xls').getByRole('heading', { name: 'Imported' })).toBeInTheDocument();
    expect(cardOf(dialog, gtrName).getByText(/Your choices that still apply were kept/)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();

    await importAll(dialog, 'Import 1 file');
    await waitFor(() => {
      expect(fake.calls.imports).toHaveLength(2);
    });
    expect(fake.calls.imports[1]).toMatchObject({ files: [gtrName] });
    expect(fake.calls.imports[1]?.digests).toEqual(['gtr-digest-3']);
    expect(fake.calls.imports[1]?.choices).toHaveLength(1);
    expect(await cardOf(dialog, gtrName).findByRole('heading', { name: 'Imported' })).toBeInTheDocument();
  });

  it('shows the refusal as the result when no fresh preview came with it', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview()] });
    fake.config.queued.set('dp-charges-fy26.xls', [
      buildFileResult({
        fileName: 'dp-charges-fy26.xls',
        kind: 'dp_charges',
        status: 'rejected',
        error: {
          code: 'portfolio.preview_stale',
          message: 'The ledger changed since the preview. Nothing was stored.',
        },
      }),
    ]);
    await previewFiles(dialog, [dpFile()]);

    await importAll(dialog, 'Import 1 file');

    expect(await dialog.findByText('The ledger changed since the preview. Nothing was stored.')).toBeInTheDocument();
  });
});

describe('Import files: Undo', () => {
  it('undoes a trade import with the trade import’s own dry run and dialog', async () => {
    const { dialog, tradeFake } = await openImportFiles({
      previews: [buildGtrPreview()],
      trades: {
        batches: [
          {
            id: '00000000-0000-4000-8000-000000008002',
            accountId: ids.main,
            accountName: 'Test Dhan Main',
            fileName: gtrName,
            sha256: 'a'.repeat(64),
            parsed: 34,
            imported: 30,
            duplicates: 0,
            skipped: 4,
            bad: 1,
            status: 'imported',
            activeTrades: 30,
            securities: 2,
            createdBy: 'owner',
            createdAt: '2026-10-12T09:30:00Z',
            undoneAt: null,
          },
        ],
      },
    });
    await previewFiles(dialog, [gtrFile()]);
    await importAll(dialog, 'Import 1 file');
    await dialog.findByRole('heading', { name: 'Import result' });

    await userEvent.click(dialog.getByRole('button', { name: `Undo the import of ${gtrName}` }));
    const undo = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    await undo.findByText(/30 trades will be voided/);
    await userEvent.click(undo.getByRole('button', { name: 'Undo import' }));

    await waitFor(() => {
      expect(tradeFake.calls.undone).toHaveLength(1);
    });
    expect(
      await cardOf(dialog, gtrName).findByText('This import was undone: what it stored no longer counts.'),
    ).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: `Undo the import of ${gtrName}` })).not.toBeInTheDocument();
    expect((await findToast()).textContent).toMatch(/Undid “global-transaction-fy26\.csv”/);
  });

  it('undoes a depository-charges import', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview()] });
    await previewFiles(dialog, [dpFile()]);
    await importAll(dialog, 'Import 1 file');
    await dialog.findByRole('heading', { name: 'Import result' });

    await userEvent.click(dialog.getByRole('button', { name: 'Undo the import of dp-charges-fy26.xls' }));
    const undo = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    expect(
      undo.getByText(/1 charge of “dp-charges-fy26\.xls” will be voided: kept in the ledger list, flagged/),
    ).toBeInTheDocument();
    await userEvent.click(undo.getByRole('button', { name: 'Undo import' }));

    const card = within(await dialog.findByRole('region', { name: 'dp-charges-fy26.xls' }));
    expect(await card.findByText('This import was undone: what it stored no longer counts.')).toBeInTheDocument();
    expect(fake.calls.costUndone).toHaveLength(1);
    expect((await findToast()).textContent).toMatch(/Import undone: 1 charge voided\./);
  });

  it('shows why a depository-charges import could not be undone', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildDpPreview({ costs: buildCosts() })] });
    fake.config.undoProblem = {
      status: 404,
      body: { title: 'The import does not exist.', code: 'portfolio.not_found' },
    };
    await previewFiles(dialog, [dpFile()]);
    await importAll(dialog, 'Import 1 file');
    await dialog.findByRole('heading', { name: 'Import result' });

    await userEvent.click(dialog.getByRole('button', { name: 'Undo the import of dp-charges-fy26.xls' }));
    const undo = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    await userEvent.click(undo.getByRole('button', { name: 'Undo import' }));

    expect(await undo.findByRole('alert')).toHaveTextContent('The import does not exist.');
  });
});
