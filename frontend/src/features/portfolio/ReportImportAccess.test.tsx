import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import { stubPhoneWidth, stubReducedMotion } from '@/test/matchMedia';
import { stubUploads } from '@/test/multipart';
import { buildBill, buildBillName, buildDpPreview, buildGtrPreview } from '@/test/reportImportFixtures';
import { cardOf, chooseFiles, dpFile, gtrFile, openImportFiles, previewFiles } from './reportImportTesting';
import { warmReportImport } from './testing';

stubUploads();

beforeAll(warmReportImport, 60_000);

const gtrName = 'global-transaction-fy26.csv';

describe('Import files: keyboard and screen readers', () => {
  it('names the dialog, the file input (described by its limits) and keeps the tabs a single tab stop', async () => {
    const { dialog } = await openImportFiles();

    const input = dialog.getByLabelText('Report files (CSV, XLS or XLSX)');
    expect(input).toHaveAccessibleDescription(
      /CSV, XLS, XLSX or text; the format is recognised from the content.*up to 20 files, 10 MB each and 40 MB together/i,
    );
    expect(dialog.getByRole('tablist', { name: 'Import files' })).toBeInTheDocument();
    expect(dialog.getByRole('tab', { name: 'Import files', selected: true })).toHaveAttribute('tabindex', '0');
    expect(dialog.getByRole('tab', { name: 'History', selected: false })).toHaveAttribute('tabindex', '-1');
  });

  it('reaches the file input and then Preview with the keyboard alone', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDpPreview()] });
    const input = dialog.getByLabelText('Report files (CSV, XLS or XLSX)');

    for (let presses = 0; presses < 8 && document.activeElement !== input; presses += 1) await userEvent.tab();
    expect(input).toHaveFocus();
    await chooseFiles(dialog, [dpFile()]);
    const remove = dialog.getByRole('button', { name: 'Remove dp-charges-fy26.xls' });
    for (let presses = 0; presses < 4 && document.activeElement !== remove; presses += 1) await userEvent.tab();
    expect(remove).toHaveFocus();
    await userEvent.tab();
    expect(dialog.getByRole('button', { name: 'Preview' })).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    expect(await dialog.findByRole('region', { name: 'dp-charges-fy26.xls' })).toBeInTheDocument();
  });

  it('gives each file a region named by the file and a heading for its blocks', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, gtrName);
    expect(card.getByRole('heading', { name: gtrName })).toBeInTheDocument();
    for (const name of ['Names', 'Covered by Dhan', 'Charges', 'Securities']) {
      expect(card.getByRole('heading', { name })).toBeInTheDocument();
    }
  });

  it('puts the picker for a name on a dialog that Escape closes without choosing', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);
    await userEvent.click(
      cardOf(dialog, gtrName).getByRole('button', { name: 'Choose a match for ORION STEEL LTD., Orion Steel' }),
    );
    await screen.findByRole('dialog', { name: 'Choose what this name means' });

    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Choose what this name means' })).not.toBeInTheDocument();
    });
    expect(cardOf(dialog, gtrName).getAllByText('Not chosen: kept without an ISIN')).toHaveLength(2);
  });

  it('shows a name that looks like markup as plain text', async () => {
    const bill = buildBill({
      names: [
        buildBillName({
          name: '<img src=x onerror=alert(1)> Globex',
          nameKey: 'globex',
          status: 'needs_match',
          kind: 'none',
          isin: null,
          holdingId: null,
        }),
      ],
      namesTotal: 1,
      needsMatch: 1,
    });
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview({ bill })] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, gtrName);
    expect(card.getByText('<img src=x onerror=alert(1)> Globex')).toBeInTheDocument();
    expect(dialog.getByRole('region', { name: gtrName }).querySelector('img')).toBeNull();
  });

  it('puts every wide table in a keyboard-reachable region that scrolls sideways', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, gtrName);
    for (const name of [
      'Names that need a match, scrolls sideways',
      'Quantity differences from Dhan, scrolls sideways',
      'Securities in the file, scrolls sideways',
    ]) {
      expect(card.getByRole('region', { name })).toHaveAttribute('tabindex', '0');
    }
    expect(appliedCss(card.getByRole('region', { name: 'Names that need a match, scrolls sideways' })).base).toMatch(
      /overflow-x: auto/,
    );
  });
});

describe('Import files: from 360 px to 4K', () => {
  it('fills the screen on a phone, with tables that scroll inside their own region', async () => {
    stubPhoneWidth();
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const paper = screen.getByRole('dialog', { name: 'Import files' }).closest('.MuiDialog-paper');
    if (!paper) throw new Error('no paper');
    expect(paper).toHaveClass('MuiDialog-paperFullScreen');
    expect(dialog.getByRole('region', { name: 'Names that need a match, scrolls sideways' })).toBeInTheDocument();
  });

  it('is a wide dialog, not a full screen one, on a large screen', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const paper = screen.getByRole('dialog', { name: 'Import files' }).closest('.MuiDialog-paper');
    expect(paper).not.toHaveClass('MuiDialog-paperFullScreen');
    expect(paper).toHaveClass('MuiDialog-paperWidthLg');
  });

  it('lets the drop area, the figures and the buttons wrap instead of overflowing', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = within(dialog.getByRole('region', { name: gtrName }));
    const tiles = card.getByText('Rows read', { selector: 'dt' }).closest('dl');
    if (!tiles) throw new Error('no figures');
    expect(appliedCss(tiles).base).toMatch(/flex-wrap: wrap/);
    const heading = card.getByRole('heading', { name: gtrName });
    expect(appliedCss(heading).base).toMatch(/overflow-wrap: anywhere/);
  });
});

describe('Import files: reduced motion', () => {
  it('works the same with prefers-reduced-motion and adds no animation of its own', async () => {
    stubReducedMotion();
    const { dialog } = await openImportFiles({ previews: [buildDpPreview()] });
    const region = await previewFiles(dialog, [dpFile()]);

    expect(appliedCss(region).base).not.toMatch(/animation|transition/);
    await userEvent.click(dialog.getByRole('button', { name: 'Import 1 file' }));
    expect(await dialog.findByRole('heading', { name: 'Import result' })).toBeInTheDocument();
  });
});
