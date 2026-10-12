import { buildCsvFile, buildCsvPreview, buildErrorPreview, buildRefusedPreview } from '@/test/csvFixtures';
import {
  loadBlockers,
  maxLoadFiles,
  maxPreviewFiles,
  needsForce,
  previewBlocker,
  selectionBreakdown,
  selectionText,
} from './csvReview';

const alpha = buildCsvFile();
const loaded = buildCsvFile({ name: 'TCS_1m.csv.gz', state: 'loaded' });
const changed = buildCsvFile({ name: 'INFY_1m.csv.gz', state: 'changed' });
const unknown = buildCsvFile({ name: 'ZZZZ_1m.csv', mapping: 'unknown' });

describe('limits', () => {
  it('are the API limits: 20 files a preview, the whole folder of 214 a load', () => {
    expect([maxPreviewFiles, maxLoadFiles]).toEqual([20, 214]);
  });
});

describe('needsForce', () => {
  it('is false for pending files only, true as soon as one was loaded', () => {
    expect(needsForce([alpha])).toBe(false);
    expect(needsForce([alpha, loaded])).toBe(true);
    expect(needsForce([changed])).toBe(true);
    expect(needsForce([])).toBe(false);
  });
});

describe('selectionText and selectionBreakdown', () => {
  it('counts the files and says what the preview reads past 20', () => {
    expect(selectionText(0)).toBe('No files selected.');
    expect(selectionText(1)).toBe('1 file selected.');
    expect(selectionText(20)).toBe('20 files selected.');
    expect(selectionText(21)).toContain('Preview reads the first 20 you checked');
  });

  it('breaks the files down by state', () => {
    expect(selectionBreakdown([alpha, alpha, changed, loaded])).toBe('2 pending, 1 changed, 1 already loaded');
  });
});

describe('loadBlockers', () => {
  it('asks for a file when none is selected', () => {
    expect(loadBlockers([], [])).toEqual(['Select at least one file.']);
  });

  it('names a selected file that cannot be loaded', () => {
    expect(loadBlockers([alpha, unknown], [])).toEqual(['ZZZZ_1m.csv: Unknown symbol.']);
  });

  it('names selected files the preview refused or could not read, and ignores files that are not selected', () => {
    const blockers = loadBlockers(
      [alpha, buildCsvFile({ name: 'NEWCO_1m.csv.gz' }), buildCsvFile({ name: 'TCS_1m.csv.gz' })],
      [
        buildCsvPreview(),
        buildRefusedPreview(),
        buildErrorPreview('TCS_1m.csv.gz'),
        buildErrorPreview('NOT_SELECTED_1m.csv'),
      ],
    );
    expect(blockers).toHaveLength(2);
    expect(blockers[0]).toContain('NEWCO_1m.csv.gz is refused: The bars are shifted');
    expect(blockers[1]).toContain('TCS_1m.csv.gz has an error');
  });

  it('is empty for passing files', () => {
    expect(loadBlockers([alpha], [buildCsvPreview()])).toEqual([]);
  });

  it('does not let a file through that passes but maps to no instrument', () => {
    expect(previewBlocker(buildCsvPreview({ wouldLoad: false }))).toBe('ALPHA_1m.csv does not map to one instrument.');
  });
});
