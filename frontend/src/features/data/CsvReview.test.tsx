import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createDataFake } from '@/test/dataFake';
import {
  buildComparison,
  buildCsvPreview,
  buildErrorPreview,
  buildRefusedPreview,
  manyCsvFiles,
} from '@/test/csvFixtures';
import { buildCsvOverview, buildDataOverview } from '@/test/fixtures';
import { appliedCss } from '@/test/css';
import { scrollToEnd } from '@/test/intersection';
import { smallPage, threePages } from '@/test/smallPages';
import type * as CsvReviewLib from './lib/csvReview';
import { csvFileColumns } from './components/csvFileColumns';
import { openCsvReview, openStep, openTab, renderData } from './testing';

vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));
// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx), and one load
// may take a few more than two pages instead of 214 files (the real limit is asserted in lib/csvReview.test.ts).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);
vi.mock('./lib/csvReview', async (importOriginal) => ({
  ...(await importOriginal<typeof CsvReviewLib>()),
  maxLoadFiles: 22,
}));

const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;
const csvName = (n: number) => `CSV${String(n).padStart(3, '0')}_1m.csv.gz`;

const overviewWithPending = (filesPending: number) =>
  buildDataOverview({ csv: buildCsvOverview({ filesPending, filesLoaded: 5, filesTotal: 8 }) });
const fakeOf = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({ overview: overviewWithPending(3), ...options });

const check = (name: string) => screen.getByRole('checkbox', { name: `Select ${name}` });
const tick = async (...names: string[]) => {
  for (const name of names) await userEvent.click(check(name));
};
const row = (name: string) => screen.getByRole('row', { name: new RegExp(name.replace('.', '\\.')) });

describe('Review CSV files: the list', () => {
  it('opens from the CSV step with the pending count, and lists every file with its state and mapping', async () => {
    renderData({ fake: fakeOf() });

    const csv = await screen.findByRole('listitem', { name: /CSV files/ });
    expect(within(csv).getByText('3 pending')).toBeInTheDocument();
    expect(
      within(await openStep('CSV files')).getByRole('button', { name: 'Review files, 3 to review' }),
    ).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    const dialog = await openCsvReview();

    expect(await within(dialog).findByRole('grid', { name: 'CSV files' })).toBeInTheDocument();
    expect(within(dialog).getAllByRole('row')).toHaveLength(9);
    expect(within(row('ALPHA_1m.csv')).getByText('Pending')).toBeInTheDocument();
    expect(within(row('INFY_1m.csv.gz')).getByText('Changed')).toBeInTheDocument();
    expect(within(row('INFY_1m.csv.gz')).getByText(/the file differs now/)).toBeInTheDocument();
    const loaded = row('RELIANCE_1m.csv.gz');
    expect(within(loaded).getAllByText('Loaded').length).toBeGreaterThan(0);
    expect(loaded).toHaveTextContent('12,34,567 rows');
    expect(loaded).toHaveTextContent('NSE:RELIANCE');
    // Flags are words with an icon, and those files cannot be checked.
    expect(row('ZZZZ_1m.csv')).toHaveTextContent('Unknown symbol (ZZZZ)');
    expect(row('DUP_1m.csv')).toHaveTextContent('Ambiguous symbol (DUP)');
    expect(row('my data_1m.csv')).toHaveTextContent('Invalid file name');
    for (const name of ['ZZZZ_1m.csv', 'DUP_1m.csv', 'my data_1m.csv']) expect(check(name)).toBeDisabled();
    expect(check('ALPHA_1m.csv')).toBeEnabled();
    expect(within(dialog).getByText(/3 files cannot be loaded/)).toBeInTheDocument();
    // Pending and changed first, then loaded.
    const names = within(dialog)
      .getAllByRole('row')
      .slice(1)
      .map((r) => r.textContent);
    expect(names[0]).toContain('ALPHA_1m.csv');
    expect(names.at(-1)).toContain('TCS_1m.csv.gz');
  });

  it('has no pending count when nothing waits', async () => {
    renderData({ fake: createDataFake() });
    expect(
      within(await screen.findByRole('listitem', { name: /CSV files/ })).queryByText(/pending/),
    ).not.toBeInTheDocument();
    expect(within(await openStep('CSV files')).getByRole('button', { name: 'Review files' })).toBeInTheDocument();
  });

  it('closes the step’s popover when the dialog opens, and returns to the page when it closes', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();

    expect(screen.queryByRole('dialog', { name: 'CSV files details' })).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getAllByRole('button', { name: 'Close' })[0]!);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('filters by state with the counts on the chips, and searches by name or symbol', async () => {
    const fake = fakeOf();
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });

    for (const label of ['Pending 5', 'Changed 1', 'Loaded 2', 'All 8']) {
      expect(within(dialog).getByRole('button', { name: label })).toBeInTheDocument();
    }
    expect(within(dialog).getByRole('button', { name: 'All 8' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Changed 1' }));
    await waitFor(() => {
      expect(within(dialog).getAllByRole('row')).toHaveLength(2);
    });
    expect(row('INFY_1m.csv.gz')).toBeInTheDocument();
    expect(fake.csv.calls.list.at(-1)).toContain('state=changed');
    expect(within(dialog).getByRole('button', { name: 'Changed 1' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(dialog).getByRole('button', { name: 'All 8' }));
    await userEvent.type(within(dialog).getByRole('searchbox', { name: 'Search files' }), 'rel');
    await waitFor(() => {
      expect(within(dialog).getAllByRole('row')).toHaveLength(2);
    });
    expect(row('RELIANCE_1m.csv.gz')).toBeInTheDocument();
    expect(fake.csv.calls.list.at(-1)).toContain('q=rel');
  });

  it('says so when no file matches', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    await userEvent.type(await within(dialog).findByRole('searchbox', { name: 'Search files' }), 'nothing-like-it');
    expect(await within(dialog).findByText('No files match.')).toBeInTheDocument();
  });

  it('loads a long folder a page of files at a time as the list is scrolled, with no pager', async () => {
    const fake = fakeOf({ csv: { files: manyCsvFiles(threePages) } });
    renderData({ fake });
    const dialog = await openCsvReview();

    expect(await within(dialog).findByText(shown(smallPage))).toBeInTheDocument();
    expect(within(dialog).getAllByRole('row')).toHaveLength(smallPage + 1);
    expect(within(dialog).queryByRole('button', { name: /page/i })).not.toBeInTheDocument();

    scrollToEnd();
    expect(await within(dialog).findByText(shown(smallPage * 2))).toBeInTheDocument();
    scrollToEnd();
    expect(await within(dialog).findByText(shown(threePages))).toBeInTheDocument();
    expect(fake.csv.calls.list.map((call) => new URLSearchParams(call).get('page'))).toEqual(['1', '2', '3']);
    expect(fake.csv.calls.list.every((call) => call.includes(`pageSize=${String(smallPage)}`))).toBe(true);
    expect(within(dialog).getByRole('grid', { name: 'CSV files' })).toHaveAttribute(
      'aria-rowcount',
      String(threePages + 1),
    );
  });

  it('starts the list again from its first page when the search or the state changes', async () => {
    const fake = fakeOf({ csv: { files: manyCsvFiles(threePages) } });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByText(shown(smallPage));
    scrollToEnd();
    await within(dialog).findByText(shown(smallPage * 2));

    await userEvent.type(within(dialog).getByRole('searchbox', { name: 'Search files' }), 'CSV02');

    await within(dialog).findByText('Showing 5 of 5'); // CSV020 to CSV024
    expect(fake.csv.calls.list.at(-1)).toContain('page=1');
    expect(fake.csv.calls.list.at(-1)).toContain('q=CSV02');
  });

  it('keeps the loaded files and offers a retry when the next page cannot be loaded', async () => {
    const fake = fakeOf({ csv: { files: manyCsvFiles(threePages) } });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByText(shown(smallPage));

    fake.csv.failures.list = true;
    scrollToEnd();
    expect(await within(dialog).findByText('Could not load more rows')).toBeInTheDocument();
    expect(check(csvName(smallPage - 1))).toBeInTheDocument();
    fake.csv.failures.list = false;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Retry' }));

    await within(dialog).findByText(shown(smallPage * 2));
  });

  it('says the folder is empty', async () => {
    renderData({ fake: fakeOf({ csv: { files: [] } }) });
    const dialog = await openCsvReview();
    expect(await within(dialog).findByText('No CSV files in the folder.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('says the folder is missing', async () => {
    renderData({ fake: fakeOf({ csv: { folderMissing: true } }) });
    const dialog = await openCsvReview();
    expect(await within(dialog).findByText(/The CSV folder \/data\/csv was not found/)).toBeInTheDocument();
  });

  it('shows a failed read with Retry', async () => {
    const fake = fakeOf();
    fake.csv.failures.list = true;
    renderData({ fake });
    const dialog = await openCsvReview();

    expect(await within(dialog).findByText('The CSV folder could not be read.')).toBeInTheDocument();
    fake.csv.failures.list = false;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Retry' }));
    expect(await within(dialog).findByRole('grid', { name: 'CSV files' })).toBeInTheDocument();
  });
});

describe('Review CSV files: narrow screens', () => {
  it('keeps the checkbox, the file, its state, the instrument and Undo, and hides size below sm, modified time and the loaded column below md', () => {
    expect(Object.fromEntries(csvFileColumns.map((column) => [column.id, column.hideBelow]))).toEqual({
      select: undefined,
      name: undefined,
      state: undefined,
      instrument: undefined,
      size: 'sm',
      modified: 'md',
      loaded: 'md',
      undo: undefined,
    });
  });

  it('gives the list a height of its own: a share of the screen on a phone, 380 px from md', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    const grid = await within(dialog).findByRole('grid', { name: 'CSV files' });

    const rules: string[] = [];
    for (let element: Element | null = grid; element && element !== dialog; element = element.parentElement) {
      rules.push(...appliedCss(element).responsive.map((r) => `${r.query} ${r.css}`));
    }
    expect(rules.join(' ')).toMatch(/min-width:0px\) height: 45dvh/);
    expect(rules.join(' ')).toMatch(/min-width:900px\) height: 380px/);
  });
});

describe('Review CSV files: choosing files', () => {
  it('selects all loaded rows that can be loaded, never the flagged ones', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    expect(within(dialog).getByText('No files selected.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Preview' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Load selected' })).toBeDisabled();

    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));

    expect(within(dialog).getByText('5 files selected.')).toBeInTheDocument();
    expect(check('ALPHA_1m.csv')).toBeChecked();
    expect(check('ZZZZ_1m.csv')).not.toBeChecked();

    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(within(dialog).getByText('No files selected.')).toBeInTheDocument();
  });

  it('keeps the choice while more files load, takes up to the most one load may take and previews only the first 20 checked', async () => {
    const fake = fakeOf({ csv: { files: manyCsvFiles(threePages) } });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByText(shown(smallPage));

    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(within(dialog).getByText(/^10 files selected\./)).toBeInTheDocument();

    scrollToEnd();
    await within(dialog).findByText(shown(smallPage * 2));
    // The 10 checked stay checked while more rows arrive; the box is half way until the new ones are checked too.
    expect(within(dialog).getByText(/^10 files selected\./)).toBeInTheDocument();
    expect(check(csvName(5))).toBeChecked();
    expect(check(csvName(15))).not.toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' })).toHaveAttribute(
      'data-indeterminate',
      'true',
    );
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(within(dialog).getByText(/^20 files selected\./)).toBeInTheDocument();

    scrollToEnd();
    await within(dialog).findByText(shown(threePages));
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));

    expect(within(dialog).getByText(/^22 files selected\./)).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Preview reads the first 20 you checked; Load takes all of them\./),
    ).toBeInTheDocument();
    expect(within(dialog).getByText(/That is the most one load may take\./)).toBeInTheDocument();
    expect(check(csvName(threePages - 1))).toBeDisabled();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    await waitFor(() => {
      expect(fake.csv.calls.preview).toHaveLength(1);
    });
    const asked = fake.csv.calls.preview[0] ?? [];
    expect(asked).toHaveLength(20);
    expect(asked[0]).toBe(csvName(0));
    expect(asked[19]).toBe(csvName(19));
  });

  it('keeps the choice across a change of filter, and "select all loaded rows" acts on the rows in the list now', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Loaded 2' }));
    await waitFor(() => {
      expect(within(dialog).getAllByRole('row')).toHaveLength(3);
    });
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Select all loaded rows' }));
    expect(within(dialog).getByText('3 files selected.')).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('button', { name: 'All 8' }));
    await within(dialog).findByText('Showing 8 of 8');
    expect(check('ALPHA_1m.csv')).toBeChecked();
    expect(check('RELIANCE_1m.csv.gz')).toBeChecked();
    expect(within(dialog).getByText('3 files selected.')).toBeInTheDocument();
  });
});

describe('Review CSV files: the preview', () => {
  it('shows a panel per file with the verdict in words, the counts, examples, first rows and the comparison', async () => {
    const fake = fakeOf({
      csv: {
        previews: {
          'NEWCO_1m.csv.gz': buildRefusedPreview(),
          'TCS_1m.csv.gz': buildErrorPreview('TCS_1m.csv.gz'),
        },
      },
    });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv', 'NEWCO_1m.csv.gz', 'TCS_1m.csv.gz');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));

    const results = await within(dialog).findByRole('region', { name: 'Preview results' });
    expect(within(dialog).getByRole('status', { name: 'Preview status' })).toHaveTextContent('Preview ready.');
    expect(
      within(results).getByText(/3 files read, nothing was written: 1 pass, 1 refused, 1 with an error\./),
    ).toBeInTheDocument();
    expect(within(results).getByText('Passes')).toBeInTheDocument();
    expect(within(results).getByText(/Refused: The bars are shifted/)).toBeInTheDocument();
    expect(within(results).getByText('Error: The file is not in the CSV folder.')).toBeInTheDocument();

    const pass = within(results).getByRole('button', { name: /ALPHA_1m\.csv/ });
    expect(pass).toHaveAttribute('aria-expanded', 'true');
    expect(results).toHaveTextContent('2,100'); // rows
    expect(results).toHaveTextContent('2,075'); // accepted
    expect(results).toHaveTextContent('12 repaired, 25 rejected');
    const reasons = within(results).getByRole('list', { name: 'Rejected rows by reason' });
    expect(within(reasons).getByText('High below low: 20')).toBeInTheDocument();
    expect(within(results).getByRole('list', { name: 'Example lines' })).toHaveTextContent('Line 14: High below low');
    expect(results).toHaveTextContent('1 Apr 2024');
    const rows = within(results).getByRole('table', { name: 'First rows of ALPHA_1m.csv' });
    expect(within(rows).getAllByRole('row')).toHaveLength(3);
    expect(rows).toHaveTextContent('12,345');
    expect(results).toHaveTextContent('would insert 1,200 · update 30 · keep existing Dhan 45 · unchanged 800');
    expect(within(results).queryByText(/Sampled:/)).not.toBeInTheDocument();
    expect(within(results).getAllByText(/Nothing would be written for this file/)).toHaveLength(2);
  });

  it('says when the comparison is of a sample', async () => {
    const fake = fakeOf({
      csv: {
        previews: {
          'ALPHA_1m.csv': buildCsvPreview({ comparison: buildComparison({ sampled: true, comparedRows: 1_000_000 }) }),
        },
      },
    });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByText(/Sampled: only 10,00,000 rows were compared/)).toBeInTheDocument();
  });

  it('shows a failed preview and lets the owner try again', async () => {
    const fake = fakeOf();
    fake.csv.failures.preview = true;
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByText('The preview is not available.')).toBeInTheDocument();

    fake.csv.failures.preview = false;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByRole('region', { name: 'Preview results' })).toBeInTheDocument();
    expect(within(dialog).queryByText('The preview is not available.')).not.toBeInTheDocument();
  });

  it('stops the load while a checked file is refused, and says which', async () => {
    const fake = fakeOf({ csv: { previews: { 'NEWCO_1m.csv.gz': buildRefusedPreview() } } });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv', 'NEWCO_1m.csv.gz');
    expect(within(dialog).getByRole('button', { name: 'Load selected' })).toBeEnabled();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    await within(dialog).findByRole('region', { name: 'Preview results' });

    expect(within(dialog).getByRole('button', { name: 'Load selected' })).toBeDisabled();
    const why = within(dialog).getByRole('alert', { name: 'Why the files cannot be loaded' });
    expect(why).toHaveTextContent('NEWCO_1m.csv.gz is refused: The bars are shifted');

    // Taking the refused file out of the choice allows the load again.
    await userEvent.click(check('NEWCO_1m.csv.gz'));
    expect(within(dialog).getByRole('button', { name: 'Load selected' })).toBeEnabled();
    expect(within(dialog).queryByRole('alert', { name: 'Why the files cannot be loaded' })).not.toBeInTheDocument();
  });
});

describe('Review CSV files: Load selected', () => {
  it('asks first, then starts the CSV and aggregate steps for the chosen files only', async () => {
    const fake = fakeOf({ autoAdvance: false });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv', 'NEWCO_1m.csv.gz');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Load selected' }));
    const confirm = await screen.findByRole('dialog', { name: 'Load 2 files?' });
    expect(confirm).toHaveTextContent('2 pending');
    expect(confirm).not.toHaveTextContent('loaded again');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    expect(fake.calls.start).toHaveLength(0);

    await userEvent.click(await screen.findByRole('button', { name: 'Load selected' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Load 2 files?' })).getByRole('button', { name: 'Load' }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.start).toEqual([{ steps: ['csv', 'aggregate'], csvFiles: ['ALPHA_1m.csv', 'NEWCO_1m.csv.gz'] }]);
    // The page follows the job.
    expect(await screen.findByRole('progressbar', { name: 'CSV files progress' })).toBeInTheDocument();
    expect(fake.running?.csvFiles).toEqual(['ALPHA_1m.csv', 'NEWCO_1m.csv.gz']);
  });

  it('asks for the files to be loaded again only when the choice has files that were loaded', async () => {
    const fake = fakeOf({ autoAdvance: false });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv', 'INFY_1m.csv.gz', 'TCS_1m.csv.gz');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Load selected' }));
    const confirm = await screen.findByRole('dialog', { name: 'Load 3 files?' });
    expect(confirm).toHaveTextContent('1 pending, 1 changed, 1 already loaded');
    expect(confirm).toHaveTextContent('Files that were loaded already are loaded again.');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Load' }));

    await waitFor(() => {
      expect(fake.calls.start).toHaveLength(1);
    });
    expect(fake.calls.start[0]).toEqual({
      steps: ['csv', 'aggregate'],
      csvFiles: ['ALPHA_1m.csv', 'INFY_1m.csv.gz', 'TCS_1m.csv.gz'],
      forceCsv: true,
    });
  });

  it('runs to the end: the page shows the result and the chosen files are loaded', async () => {
    const fake = fakeOf();
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Load selected' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Load 1 file?' })).getByRole('button', { name: 'Load' }),
    );

    expect(await screen.findByText(/^Data updated: /)).toBeInTheDocument();
    await openTab('Runs');
    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    await waitFor(() => {
      expect(runs.getAllByRole('listitem')).toHaveLength(2);
    });
    expect(runs.getAllByRole('listitem')[0]).toHaveTextContent('CSV · Aggregates · 1 chosen CSV file');
    expect(fake.csv.find('ALPHA_1m.csv')?.state).toBe('loaded');
  });

  it('shows the refusal of a start on the page', async () => {
    const fake = fakeOf({ startError: 'job_running' });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Load selected' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Load 1 file?' })).getByRole('button', { name: 'Load' }),
    );

    expect(await screen.findByText('An update is already running.')).toBeInTheDocument();
  });

  it('waits while an update is running', async () => {
    const fake = fakeOf({ autoAdvance: false, running: { steps: ['dhan'] } });
    renderData({ fake });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });
    await tick('ALPHA_1m.csv');

    expect(within(dialog).getByRole('button', { name: 'Load selected' })).toBeDisabled();
    expect(within(dialog).getByText(/An update is running\. Load and Undo wait/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Undo load of RELIANCE_1m.csv.gz' })).toBeDisabled();
  });

  it('labels the dialog, the search, the state chips and the grid', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    expect(dialog).toHaveAccessibleName('Review CSV files');
    expect(within(dialog).getByRole('group', { name: 'Show files' })).toBeInTheDocument();
    expect(await within(dialog).findByRole('searchbox', { name: 'Search files' })).toBeInTheDocument();
    expect(await within(dialog).findByRole('grid', { name: 'CSV files' })).toBeInTheDocument();
  });
});
