import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DividendMatch } from '@/shared/api';
import { buildImportItem, reportOf } from '@/test/dividendImportFixtures';
import { holdingIds } from '@/test/fixtures';
import { stubUploads } from '@/test/multipart';
import { openDividendImport, previewFile, type Panel } from './dividendImportTesting';
import { warmDividends } from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

/** Opens the picker for a name on the "Needs a match" tab. */
async function pick(region: Panel, name: string) {
  await userEvent.click(region.getByRole('button', { name: `Choose a holding for ${name}` }));
  return within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
}

async function search(picker: Panel, text: string) {
  await userEvent.type(picker.getByRole('combobox', { name: 'Holding or instrument' }), text);
}

describe('Import dividends: choosing where a name goes', () => {
  it('searches the account’s holdings, ticks "remember" by default and shows the choice on the name', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const picker = await pick(region, 'Northwind Traders Ltd');
    expect(picker.getByRole('checkbox', { name: 'Remember this match for later files' })).toBeChecked();
    expect(picker.getByRole('button', { name: 'Use this match' })).toBeDisabled();
    await search(picker, 'infos');
    await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    const row = within(region.getByRole('row', { name: /Northwind Traders Ltd/ }));
    expect(row.getByText('INFY')).toBeInTheDocument();
    expect(row.getByText('remembered for later files')).toBeInTheDocument();
    expect(dialog.getByText('1 match will be remembered for later files.')).toBeInTheDocument();

    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices).toEqual([
      {
        name: 'northwind traders ltd',
        holdingId: holdingIds.infy,
        instrumentId: null,
        none: false,
        remember: true,
      },
    ]);
  });

  it('can be told not to remember: the choice is for this file only', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const picker = await pick(region, 'Northwind Traders Ltd');
    await search(picker, 'tcs');
    await userEvent.click(await screen.findByRole('option', { name: /TCS/ }));
    await userEvent.click(picker.getByRole('checkbox', { name: 'Remember this match for later files' }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(region.getByText('this file only')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices[0]).toMatchObject({ holdingId: holdingIds.tcs, remember: false });
  });

  it('can leave a name unallocated on purpose', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const picker = await pick(region, 'Northwind Traders Ltd');
    await userEvent.click(picker.getByRole('combobox', { name: 'Holding or instrument' }));
    await userEvent.click(await screen.findByRole('option', { name: /Leave unallocated/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(
      within(region.getByRole('row', { name: /Northwind Traders Ltd/ })).getByText('Left unallocated'),
    ).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices[0]).toMatchObject({
      name: 'northwind traders ltd',
      holdingId: null,
      instrumentId: null,
      none: true,
    });
  });

  it('can send a name to an instrument of the list that the account does not hold', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const picker = await pick(region, 'Northwind Traders Ltd');
    await search(picker, 'hdfc');
    await userEvent.click(await screen.findByRole('option', { name: /HDFCBANK.*not held/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');

    const choice = fake.calls.imports[0]?.choices[0];
    expect(choice?.holdingId).toBeNull();
    expect(choice?.instrumentId).toEqual(expect.any(String));
  });

  it('offers the holdings an ambiguous name fits first, and can take a choice back', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const picker = await pick(region, 'Contoso Holdings');
    expect(picker.getByText(/More than one holding fits this name \(INFY, TCS\)/)).toBeInTheDocument();
    await userEvent.click(picker.getByRole('combobox', { name: 'Holding or instrument' }));
    expect(await screen.findByRole('option', { name: /INFY.*fits this name/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('option', { name: /TCS.*fits this name/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    expect(within(region.getByRole('row', { name: /Contoso Holdings/ })).getByText('TCS')).toBeInTheDocument();

    const again = within(
      await (async () => {
        await userEvent.click(region.getByRole('button', { name: 'Change the choice for Contoso Holdings' }));
        return screen.findByRole('dialog', { name: 'Choose where these dividends go' });
      })(),
    );
    await userEvent.click(again.getByRole('button', { name: 'Remove my choice' }));
    expect(
      within(region.getByRole('row', { name: /Contoso Holdings/ })).getByText('Not chosen: stored unallocated'),
    ).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices).toEqual([]);
  });

  it('changes a partial name match from the ready rows', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('tab', { name: 'Ready to add (2)' }));
    await userEvent.click(region.getByRole('button', { name: 'Change the match for Tata Consultancy' }));
    const picker = within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
    await search(picker, 'infos');
    await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    const row = within(region.getByRole('row', { name: /Tata Consultancy/ }));
    expect(row.getByText('INFY')).toBeInTheDocument();
    expect(row.getByText('your choice, remembered')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices[0]).toMatchObject({ name: 'tata consultancy', holdingId: holdingIds.infy });
  });

  it('says when the holdings cannot be searched and offers Retry', async () => {
    const { dialog } = await openDividendImport();
    const region = within(await previewFile(dialog));
    const { server } = await import('@/test/server');
    const { http, HttpResponse } = await import('msw');
    server.use(http.get('/api/holdings', () => HttpResponse.json({ title: 'The search is down.' }, { status: 500 })));

    const picker = await pick(region, 'Northwind Traders Ltd');
    await search(picker, 'xyz');

    expect(await picker.findByText('The search is down.')).toBeInTheDocument();
    expect(picker.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});

describe('Import dividends: possible duplicates', () => {
  it('skips them unless ticked, and sends the lines that were ticked', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    expect(dialog.getByText('5 dividends (₹1,750.00) will be stored.')).toBeInTheDocument();
    await userEvent.click(region.getByRole('tab', { name: 'Possible duplicates (1)' }));
    await userEvent.click(region.getByRole('checkbox', { name: /Include line 7/ }));

    expect(dialog.getByText(/6 dividends \(₹2,950\.00\) will be stored\./)).toBeInTheDocument();
    expect(dialog.getByText(/1 possible duplicate included/)).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Import 6 dividends' }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.includeDuplicates).toEqual([7]);
  });

  it('sends none when none is ticked, and can include all of them or none', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('tab', { name: 'Possible duplicates (1)' }));
    await userEvent.click(region.getByRole('button', { name: 'Include all' }));
    expect(region.getByRole('checkbox', { name: /Include line 7/ })).toBeChecked();
    await userEvent.click(region.getByRole('button', { name: 'Include none' }));
    expect(region.getByRole('checkbox', { name: /Include line 7/ })).not.toBeChecked();
    await userEvent.click(dialog.getByRole('button', { name: 'Import 5 dividends' }));
    await dialog.findByText('Import done');

    expect(fake.calls.imports[0]?.includeDuplicates).toEqual([]);
  });
});

const nothingFits: DividendMatch = {
  kind: 'none',
  holdingId: null,
  instrumentId: null,
  symbol: null,
  isin: null,
  name: null,
  candidates: [],
};

const open = (line: number, name: string, status = 'needs_match') =>
  buildImportItem({ line, name, status, amount: 100, match: { ...nothingFits } });

describe('Import dividends: two spellings of one company', () => {
  it('are one name to choose for: one line, one choice, applied to every row of it', async () => {
    const report = reportOf([
      open(2, 'Northwind Traders Ltd'),
      open(3, 'NORTHWIND TRADERS LTD.'),
      open(4, 'Northwind  Traders Ltd', 'possible_duplicate'),
    ]);
    const { dialog, fake } = await openDividendImport({ report });
    const region = within(await previewFile(dialog));

    const table = within(region.getByRole('table', { name: 'Names that need a match' }));
    expect(table.getAllByRole('row')).toHaveLength(2);
    const row = within(table.getAllByRole('row')[1]!);
    expect(row.getByText('Northwind Traders Ltd')).toBeInTheDocument();
    expect(row.getByText('NORTHWIND TRADERS LTD.')).toBeInTheDocument();
    expect(row.getByText('2')).toBeInTheDocument();

    const picker = await pick(region, 'Northwind Traders Ltd, NORTHWIND TRADERS LTD.');
    await search(picker, 'infos');
    await userEvent.click(await screen.findByRole('option', { name: /INFY/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));

    expect(row.getByText('remembered for later files')).toBeInTheDocument();
    await userEvent.click(region.getByRole('tab', { name: 'Possible duplicates (1)' }));
    expect(
      within(region.getByRole('row', { name: /Northwind Traders Ltd/ })).getByText('your choice, remembered'),
    ).toBeInTheDocument();

    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices).toEqual([
      { name: 'northwind traders ltd', holdingId: holdingIds.infy, instrumentId: null, none: false, remember: true },
    ]);
  });

  it('lists up to three spellings of a name and counts the rest', async () => {
    const report = reportOf(
      ['Acme Ltd', 'ACME LTD', 'Acme Ltd.', 'acme ltd', 'Acme, Ltd'].map((n, i) => open(i + 2, n)),
    );
    const { dialog } = await openDividendImport({ report });
    const region = within(await previewFile(dialog));

    const table = within(region.getByRole('table', { name: 'Names that need a match' }));
    expect(table.getAllByRole('row')).toHaveLength(2);
    expect(table.getByText('Acme Ltd.')).toBeInTheDocument();
    expect(table.queryByText('acme ltd')).not.toBeInTheDocument();
    expect(table.getByText('+2 more')).toBeInTheDocument();
    expect(
      region.getByRole('button', { name: 'Choose a holding for Acme Ltd, ACME LTD, Acme Ltd. +2 more' }),
    ).toBeInTheDocument();
  });
});

describe('Import dividends: changing a match that was found', () => {
  const found = (kind: string, name: string, line: number) =>
    buildImportItem({
      line,
      name,
      match: {
        kind,
        holdingId: holdingIds.infy,
        instrumentId: null,
        symbol: 'INFY',
        isin: 'INE009A01021',
        name: 'Infosys',
        candidates: [],
      },
    });

  it.each(['alias', 'name', 'symbol', 'isin', 'master', 'contains'])(
    'offers Change on a row matched by %s',
    async (kind) => {
      const report = reportOf([found(kind, 'Infosys Ltd', 2)]);
      const { dialog } = await openDividendImport({ report });
      const region = within(await previewFile(dialog));

      expect(
        region.getByRole('button', {
          name: kind === 'alias' ? 'Change the remembered match for Infosys Ltd' : 'Change the match for Infosys Ltd',
        }),
      ).toBeInTheDocument();
    },
  );

  it('offers Change on a possible duplicate but not on a row already imported', async () => {
    const report = reportOf([
      { ...found('name', 'Infosys Ltd', 2), status: 'possible_duplicate' },
      { ...found('name', 'Tata Power', 3), status: 'imported' },
    ]);
    const { dialog } = await openDividendImport({ report });
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('tab', { name: 'Possible duplicates (1)' }));
    expect(region.getByRole('button', { name: 'Change the match for Infosys Ltd' })).toBeInTheDocument();
    await userEvent.click(region.getByRole('tab', { name: 'Already imported (1)' }));
    expect(region.queryByRole('button', { name: /^Change/ })).not.toBeInTheDocument();
  });

  it('retargets a remembered match: the dialog says so and "remember" is ticked', async () => {
    const report = reportOf([found('alias', 'Infosys Ltd', 2)]);
    const { dialog, fake } = await openDividendImport({ report });
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('button', { name: 'Change the remembered match for Infosys Ltd' }));
    const picker = within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
    expect(
      picker.getByText(/Remembered match: choose another holding and keep “Remember this match” ticked/),
    ).toBeInTheDocument();
    expect(picker.getByRole('checkbox', { name: 'Remember this match for later files' })).toBeChecked();
    await search(picker, 'tcs');
    await userEvent.click(await screen.findByRole('option', { name: /TCS/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends?$/ }));
    await dialog.findByText('Import done');

    expect(fake.calls.imports[0]?.choices).toMatchObject([
      { name: 'infosys ltd', holdingId: holdingIds.tcs, remember: true },
    ]);
  });
});
