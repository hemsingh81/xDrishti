import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import { buildImportReport } from '@/test/dividendImportFixtures';
import { stubPhoneWidth, stubReducedMotion } from '@/test/matchMedia';
import { stubUploads } from '@/test/multipart';
import { chooseFile, openDividendImport, previewFile } from './dividendImportTesting';
import { warmDividends } from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

describe('Import dividends: keyboard and screen readers', () => {
  it('names the dialog, the file input (described by its limits) and keeps the tabs a single tab stop', async () => {
    const { dialog } = await openDividendImport();

    const input = dialog.getByLabelText('Dividend list file (CSV)');
    expect(input).toHaveAccessibleDescription(/CSV, up to 10 MB and 20,000 rows/);
    expect(dialog.getByRole('tablist', { name: 'Import dividends' })).toBeInTheDocument();
    expect(dialog.getByRole('tab', { name: 'Import file', selected: true })).toHaveAttribute('tabindex', '0');
    expect(dialog.getByRole('tab', { name: 'History', selected: false })).toHaveAttribute('tabindex', '-1');
  });

  it('reaches the file input with the keyboard', async () => {
    const { dialog } = await openDividendImport();
    const input = dialog.getByLabelText('Dividend list file (CSV)');

    for (let presses = 0; presses < 6 && document.activeElement !== input; presses += 1) await userEvent.tab();

    expect(input).toHaveFocus();
  });

  it('chooses a holding for a name from the keyboard alone', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));
    region.getByRole('button', { name: 'Choose a holding for Northwind Traders Ltd' }).focus();

    await userEvent.keyboard('{Enter}');
    const picker = within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
    await waitFor(() => {
      expect(picker.getByRole('combobox', { name: 'Holding or instrument' })).toHaveFocus();
    });
    await userEvent.keyboard('infos');
    await screen.findByRole('option', { name: /INFY/ });
    await userEvent.keyboard('{ArrowDown}{Enter}');
    picker.getByRole('button', { name: 'Use this match' }).focus();
    await userEvent.keyboard('{Enter}');

    expect(within(region.getByRole('row', { name: /Northwind Traders Ltd/ })).getByText('INFY')).toBeInTheDocument();
    dialog.getByRole('button', { name: /^Import \d+ dividends$/ }).focus();
    await userEvent.keyboard('{Enter}');
    await dialog.findByText('Import done');
    expect(fake.calls.imports[0]?.choices[0]?.name).toBe('northwind traders ltd');
  });

  it('closes the picker with Escape without choosing', async () => {
    const { dialog } = await openDividendImport();
    const region = within(await previewFile(dialog));
    await userEvent.click(region.getByRole('button', { name: 'Choose a holding for Northwind Traders Ltd' }));
    await screen.findByRole('dialog', { name: 'Choose where these dividends go' });

    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Choose where these dividends go' })).not.toBeInTheDocument();
    });
    expect(region.getAllByText('Not chosen: stored unallocated')).toHaveLength(2);
  });

  it('says what is long as plain text: a name that looks like markup is not markup', async () => {
    const report = buildImportReport();
    const first = report.items[0];
    if (!first) throw new Error('no item');
    first.name = '<img src=x onerror=alert(1)> Globex';
    const { dialog } = await openDividendImport({ report });
    const region = await previewFile(dialog);

    await userEvent.click(within(region).getByRole('tab', { name: 'Ready to add (2)' }));

    expect(within(region).getByText('<img src=x onerror=alert(1)> Globex')).toBeInTheDocument();
    expect(region.querySelector('img')).toBeNull();
  });

  it('puts every wide table in a keyboard-reachable region that scrolls sideways', async () => {
    const { dialog } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const needs = region.getByRole('region', { name: 'Names that need a match, scrolls sideways' });
    expect(needs).toHaveAttribute('tabindex', '0');
    await userEvent.click(region.getByRole('tab', { name: 'Ready to add (2)' }));
    expect(region.getByRole('region', { name: 'Rows ready to add, scrolls sideways' })).toHaveAttribute(
      'tabindex',
      '0',
    );
    expect(appliedCss(needs).base).toMatch(/overflow-x: auto/);
  });
});

describe('Import dividends: from 360 px to 4K', () => {
  it('fills the screen on a phone, with tables that scroll inside their own region', async () => {
    stubPhoneWidth();
    const { dialog } = await openDividendImport();
    await previewFile(dialog);

    const paper = screen.getByRole('dialog', { name: 'Import dividends' }).closest('.MuiDialog-paper');
    if (!paper) throw new Error('no paper');
    expect(paper).toHaveClass('MuiDialog-paperFullScreen');
    expect(dialog.getByRole('region', { name: 'Names that need a match, scrolls sideways' })).toBeInTheDocument();
  });

  it('is a wide dialog, not a full screen one, on a large screen', async () => {
    const { dialog } = await openDividendImport();
    await previewFile(dialog);

    const paper = screen.getByRole('dialog', { name: 'Import dividends' }).closest('.MuiDialog-paper');
    expect(paper).not.toHaveClass('MuiDialog-paperFullScreen');
    expect(paper).toHaveClass('MuiDialog-paperWidthLg');
  });
});

describe('Import dividends: reduced motion', () => {
  it('works the same with prefers-reduced-motion and adds no animation of its own', async () => {
    stubReducedMotion();
    const { dialog } = await openDividendImport();
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));
    const region = await dialog.findByRole('region', { name: /Preview of/ });

    const css = appliedCss(region);
    expect(css.base).not.toMatch(/animation|transition/);
    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    expect(await dialog.findByText('Import done')).toBeInTheDocument();
  });
});
