import { screen, within, type BoundFunctions, type queries } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { csvFile } from '@/test/multipart';
import type { ReportImportFakeOptions } from '@/test/reportImportFake';
import type { TradeFakeOptions } from '@/test/tradeFake';
import { renderPortfolio, serveReportImport, serveTrades } from './testing';

export type Panel = BoundFunctions<typeof queries>;

/** Made-up file contents: the fake decides what each file is by its name, so the text does not matter. */
export const sampleCsv = 'Bill No,Date,Scrip Name\n';

export const gtrFile = () => csvFile(sampleCsv, 'global-transaction-fy26.csv');
export const dpFile = () => csvFile(sampleCsv, 'dp-charges-fy26.xls', 'application/vnd.ms-excel');
export const dematFile = () => csvFile(sampleCsv, 'demat-holding.csv');

/** Serves the report import API and the trades API, renders the Portfolio screen on `search` and returns the Import files dialog. */
export async function openImportFiles(
  options: ReportImportFakeOptions & { trades?: TradeFakeOptions } = {},
  search = '?import=open',
) {
  const { trades, ...importOptions } = options;
  const tradeFake = serveTrades(trades);
  const fake = serveReportImport(importOptions);
  const view = renderPortfolio(search);
  const dialog = within(await screen.findByRole('dialog', { name: 'Import files' }));
  return { ...view, fake, tradeFake, dialog };
}

export async function chooseFiles(dialog: Panel, files: readonly File[]) {
  await userEvent
    .setup({ applyAccept: false })
    .upload(dialog.getByLabelText('Report files (CSV, XLS or XLSX)'), [...files]);
}

/** Chooses the files and presses Preview; returns the preview section once it is there. */
export async function previewFiles(dialog: Panel, files: readonly File[]) {
  await chooseFiles(dialog, files);
  await userEvent.click(dialog.getByRole('button', { name: /^Preview/ }));
  return dialog.findByRole('region', { name: /^Preview of/ });
}

/** The card of one file (a region named by the file): queries inside it. */
export const cardOf = (dialog: Panel, fileName: string) => within(dialog.getByRole('region', { name: fileName }));

/** The value under a label of a `dl` of counts, inside a card. */
export const figureIn = (card: Panel, label: string) => card.getByText(label, { selector: 'dt' }).nextElementSibling;
