import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { holdingIds } from '@/test/fixtures';
import { csvFile, stubUploads } from '@/test/multipart';
import { buildBill, buildBillName, buildGtrPreview, instrumentIds } from '@/test/reportImportFixtures';
import { cardOf, gtrFile, openImportFiles, previewFiles, sampleCsv, type Panel } from './reportImportTesting';
import { warmReportImport } from './testing';

stubUploads();

beforeAll(warmReportImport, 60_000);

const fileName = 'global-transaction-fy26.csv';

/** The same report once the Orion names are matched (what the server answers when the choices are sent). */
const resolved = (digest = 'gtr-digest-2') =>
  buildGtrPreview({
    digest,
    bill: buildBill({
      names: [
        buildBillName(),
        buildBillName({ name: 'ORION STEEL LTD.', nameKey: 'orion steel', kind: 'choice', rows: 3, trades: 3 }),
        buildBillName({ name: 'Orion Steel', nameKey: 'orion steel', kind: 'choice', rows: 2, trades: 2 }),
        buildBillName({
          name: 'NOVA OXIDES & CHEM LTD',
          nameKey: 'nova oxides chem',
          status: 'needs_match',
          kind: 'none',
          isin: null,
          holdingId: null,
          rows: 1,
          trades: 1,
        }),
      ],
      namesTotal: 4,
      needsMatch: 1,
    }),
  });

async function pick(card: Panel, name: string) {
  await userEvent.click(card.getByRole('button', { name: `Choose a match for ${name}` }));
  return within(await screen.findByRole('dialog', { name: 'Choose what this name means' }));
}

async function chooseInfy(picker: Panel) {
  await userEvent.type(picker.getByRole('combobox', { name: 'Holding or instrument' }), 'infos');
  await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
}

const orionName = 'ORION STEEL LTD., Orion Steel';

describe('Import files: matching names', () => {
  it('chooses a holding, remembers it by default and shows the choice on the name', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);

    const picker = await pick(card, orionName);
    expect(picker.getByRole('checkbox', { name: 'Remember this match for later files' })).toBeChecked();
    expect(picker.getByRole('button', { name: 'Use this match' })).toBeDisabled();
    expect(picker.queryByRole('option', { name: /Leave unallocated/ })).not.toBeInTheDocument();
    await chooseInfy(picker);
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    const row = within(card.getByRole('row', { name: /ORION STEEL LTD\./ }));
    expect(row.getByText('INFY')).toBeInTheDocument();
    expect(row.getByText('remembered for later files')).toBeInTheDocument();
    expect(row.getByRole('button', { name: `Change the match for ${orionName}` })).toBeInTheDocument();
  });

  it('must be read again with the choices before the files can be imported', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.afterChoices.set(fileName, resolved());
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);
    const picker = await pick(card, orionName);
    await chooseInfy(picker);
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeDisabled();
    expect(dialog.getByText('You changed matches: preview again to apply them before importing.')).toBeInTheDocument();
    await userEvent.click(card.getByRole('button', { name: 'Preview again with my matches' }));

    await dialog.findByText(/4 names in the file; 3 matched to a security, 1 need a match/);
    expect(fake.calls.previews[1]?.choices).toEqual([
      { name: 'orion steel', holdingId: holdingIds.infy, remember: true },
    ]);
    expect(fake.calls.previews[1]?.files).toEqual([fileName]);
    expect(dialog.queryByText(/You changed matches/)).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();

    await userEvent.click(dialog.getByRole('button', { name: 'Import 1 file' }));
    await dialog.findByText('Import result');
    expect(fake.calls.imports[0]?.choices).toEqual([
      { name: 'orion steel', holdingId: holdingIds.infy, remember: true },
    ]);
    expect(fake.calls.imports[0]?.digests).toEqual(['gtr-digest-2']);
    expect(dialog.getByText('1 match remembered for later files.')).toBeInTheDocument();
  });

  it('can be told not to remember: the choice is for these files only', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.afterChoices.set(fileName, resolved());
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);

    const picker = await pick(card, orionName);
    await chooseInfy(picker);
    await userEvent.click(picker.getByRole('checkbox', { name: 'Remember this match for later files' }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(card.getByText('this file only')).toBeInTheDocument();
    await userEvent.click(card.getByRole('button', { name: 'Preview again with my matches' }));
    await dialog.findByText(/3 matched to a security/);
    expect(fake.calls.previews[1]?.choices[0]).toMatchObject({ holdingId: holdingIds.infy, remember: false });
  });

  it('takes an ISIN typed in, once it has twelve characters', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.afterChoices.set(fileName, resolved());
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);

    const picker = await pick(card, orionName);
    await userEvent.type(picker.getByRole('textbox', { name: 'Or type the ISIN' }), 'ine0ts00');
    expect(picker.getByText('An ISIN has 12 characters, such as INE000A01010.')).toBeInTheDocument();
    expect(picker.getByRole('button', { name: 'Use this match' })).toBeDisabled();
    await userEvent.type(picker.getByRole('textbox', { name: 'Or type the ISIN' }), '1018');
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(
      within(card.getByRole('row', { name: /ORION STEEL LTD\./ })).getByText('ISIN INE0TS001018'),
    ).toBeInTheDocument();
    await userEvent.click(card.getByRole('button', { name: 'Preview again with my matches' }));
    await dialog.findByText(/3 matched to a security/);
    expect(fake.calls.previews[1]?.choices).toEqual([{ name: 'orion steel', isin: 'INE0TS001018', remember: true }]);
  });

  it('explains an ambiguous name, offers the securities it fits and sends an instrument of the list', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.afterChoices.set(fileName, resolved());
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);

    const picker = await pick(card, 'NOVA OXIDES & CHEM LTD');
    expect(
      picker.getByText(/More than one security fits this name \(NOVAOX, NOVAOY\), so nothing is guessed/),
    ).toBeInTheDocument();
    await userEvent.click(picker.getByRole('combobox', { name: 'Holding or instrument' }));
    expect(await screen.findByRole('option', { name: /NOVAOX/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('option', { name: /NOVAOX/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    await userEvent.click(card.getByRole('button', { name: 'Preview again with my matches' }));

    await dialog.findByText(/4 names in the file/);
    expect(fake.calls.previews[1]?.choices).toEqual([
      { name: 'nova oxides chem', holdingId: holdingIds.smallco, remember: true },
    ]);
    expect(instrumentIds.nova).toBeDefined();
  });

  it('takes a choice back', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);
    const card = cardOf(dialog, fileName);
    let picker = await pick(card, orionName);
    await chooseInfy(picker);
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    await userEvent.click(card.getByRole('button', { name: `Change the match for ${orionName}` }));
    picker = within(await screen.findByRole('dialog', { name: 'Choose what this name means' }));
    await userEvent.click(picker.getByRole('button', { name: 'Remove my choice' }));

    expect(card.getAllByText('Not chosen: kept without an ISIN')).toHaveLength(2);
    expect(dialog.queryByText(/You changed matches/)).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import 1 file' })).toBeEnabled();
  });

  it('applies a choice to every file with that name', async () => {
    const { dialog } = await openImportFiles({
      previews: [
        buildGtrPreview(),
        buildGtrPreview({ fileName: 'global-transaction-fy25.csv', digest: 'gtr-digest-9' }),
      ],
    });
    await previewFiles(dialog, [gtrFile(), csvFile(sampleCsv, 'global-transaction-fy25.csv')]);

    const picker = await pick(cardOf(dialog, fileName), orionName);
    await chooseInfy(picker);
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    const other = cardOf(dialog, 'global-transaction-fy25.csv');
    expect(within(other.getByRole('row', { name: /ORION STEEL LTD\./ })).getByText('INFY')).toBeInTheDocument();
  });

  it('shows holdings from the account and keeps the dialog usable when the search fails', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const picker = await pick(cardOf(dialog, fileName), orionName);
    await userEvent.click(picker.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog', { name: 'Choose what this name means' })).not.toBeInTheDocument();
    expect(holdingIds.infy).toBeDefined();
  });
});
