import { screen, within, type BoundFunctions, type queries } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { csvFile } from '@/test/multipart';
import type { DividendFakeOptions } from '@/test/dividendFake';
import type { DividendImportFakeOptions } from '@/test/dividendImportFake';
import { renderPortfolio, serveDividendImport, serveDividends } from './testing';

/** A made-up dividend list with the header the importer recognises (the content does not matter: the preview is a fake). */
export const dividendCsv = 'Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid\n';

export type Panel = BoundFunctions<typeof queries>;

/** Opens Cash & dividends, then Import dividends; returns the dialog's queries and the fakes. */
export async function openDividendImport(
  options: DividendImportFakeOptions & { dividends?: DividendFakeOptions['dividends'] } = {},
  search = '?cash=open',
) {
  const { dividends, ...importOptions } = options;
  serveDividends({ dividends });
  const fake = serveDividendImport(importOptions);
  const view = renderPortfolio(search);
  // Opened by the link, the import dialog covers the panel (which is then hidden from the accessibility tree).
  const cash = within(
    await screen.findByRole('dialog', { name: search.includes('import') ? 'Import dividends' : 'Cash & dividends' }),
  );
  if (!search.includes('import')) await userEvent.click(await cash.findByRole('button', { name: 'Import dividends' }));
  const dialog = within(await screen.findByRole('dialog', { name: 'Import dividends' }));
  return { ...view, fake, cash, dialog };
}

export async function chooseFile(dialog: Panel, file = csvFile(dividendCsv, 'dividends-sample.csv')) {
  await userEvent.setup({ applyAccept: false }).upload(dialog.getByLabelText('Dividend list file (CSV)'), file);
}

/** Chooses the file and presses Preview; returns the preview region once it is there. */
export async function previewFile(dialog: Panel) {
  await chooseFile(dialog);
  await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));
  return dialog.findByRole('region', { name: /Preview of dividends-sample\.csv/ });
}
