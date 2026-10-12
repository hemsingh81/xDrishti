import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, type RequestHandler } from 'msw';
import { buildCorporateAction, coverageWithCsvEnd, sampleCorporateActions } from '@/test/actionFixtures';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { problemResponse } from '@/test/server';
import { renderData as renderAt } from './testing';

/** Every test here looks at the Corporate actions tab. */
const renderData = (options: Parameters<typeof renderAt>[0] = {}) => renderAt({ search: '?tab=actions', ...options });

const region = () => screen.findByRole('tabpanel', { name: /^Corporate actions/ });
const rowsIn = (grid: HTMLElement) => within(grid).getAllByRole('row').slice(1);
const grid = () => screen.getByRole('grid', { name: 'Corporate actions list' });

const fakeWith = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({
    coverage: coverageWithCsvEnd(),
    corporateActions: sampleCorporateActions,
    calendar: { proposals: [] },
    ...options,
  });

async function open({ fake = fakeWith(), handlers }: { fake?: DataFake; handlers?: RequestHandler[] } = {}) {
  const result = renderData({ fake, handlers });
  const card = await region();
  await within(card).findByText('NSE:TCS');
  return { ...result, card };
}

async function openAdd(options: Parameters<typeof open>[0] = {}) {
  const result = await open(options);
  await userEvent.click(within(result.card).getByRole('button', { name: 'Add action' }));
  const dialog = await screen.findByRole('dialog', { name: 'Add corporate action' });
  return { ...result, dialog };
}

async function chooseInstrument(dialog: HTMLElement, typed: string, option: RegExp) {
  await userEvent.type(within(dialog).getByRole('combobox', { name: 'Instrument' }), typed);
  await userEvent.click(await screen.findByRole('option', { name: option }));
}

const fill = async (dialog: HTMLElement, { date, n, d }: { date?: string; n?: string; d?: string }) => {
  if (date) await userEvent.type(within(dialog).getByLabelText(/^Ex-date/), date);
  if (n) await userEvent.type(within(dialog).getByLabelText(/^Numerator/), n);
  if (d) await userEvent.type(within(dialog).getByLabelText(/^Denominator/), d);
};

describe('Corporate actions: Add action', () => {
  it('searches the instruments as you type, shows the factor it gives, and adds the action as applied', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    expect(within(dialog).getByText('Enter the ex-date and the ratio to see the price factor.')).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Split n:d is old shares : new shares, so 1:5 turns one share into five/),
    ).toBeInTheDocument();
    await chooseInstrument(dialog, 'inf', /NSE:INFY/);
    await fill(dialog, { date: '2026-12-01', n: '1', d: '5' });
    expect(within(dialog).getByText('Prices before 1 Dec 2026 × 0.2')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Note'), 'Split 1:5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText(/^Added NSE:INFY split 1:5 on 1 Dec 2026; it is applied/)).toBeInTheDocument();
    expect(fake.actions.calls.add).toEqual([
      {
        instrumentId: sampleCoverageId('NSE:INFY'),
        exDate: '2026-12-01',
        kind: 'split',
        numerator: 1,
        denominator: 5,
        note: 'Split 1:5',
      },
    ]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const row = await within(grid()).findByRole('row', { name: /NSE:INFY.*1:5.*You.*Applied/ });
    expect(within(row).getByText('Applied')).toBeInTheDocument();
    // The type-ahead searched on the server, with the text typed.
    expect(fake.calls.lookups.some((search) => search.includes('q=inf'))).toBe(true);
  });

  it('explains the bonus ratio and its factor', async () => {
    const { dialog } = await openAdd();

    await userEvent.selectOptions(within(dialog).getByLabelText('Kind'), 'bonus');
    await fill(dialog, { date: '2026-12-01', n: '1', d: '1' });

    expect(within(dialog).getByText(/Bonus a:b is a bonus shares for b held/)).toBeInTheDocument();
    expect(within(dialog).getByText('Prices before 1 Dec 2026 × 0.5')).toBeInTheDocument();
    expect(within(dialog).getByText('Bonus shares')).toBeInTheDocument();
    expect(within(dialog).getByText('Shares held')).toBeInTheDocument();
  });

  it('says what is missing before sending anything', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await within(dialog).findByText('Choose an instrument.')).toBeInTheDocument();
    expect(within(dialog).getByText('Choose the ex-date.')).toBeInTheDocument();
    expect(within(dialog).getAllByText('Enter a whole number from 1 to 100000.')).toHaveLength(2);
    expect(fake.actions.calls.add).toHaveLength(0);
  });

  it('refuses a split 1:1 before sending', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    await chooseInstrument(dialog, 'tcs', /NSE:TCS/);
    await fill(dialog, { date: '2026-12-01', n: '2', d: '2' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(
      await within(dialog).findByText('A 1:1 split changes nothing. Enter a different ratio.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByText('Enter the ex-date and the ratio to see the price factor.')).toBeInTheDocument();
    expect(fake.actions.calls.add).toHaveLength(0);
  });

  it('shows the messages of the server on their fields', async () => {
    const reject = [
      http.post('/api/data/corporate-actions', () =>
        problemResponse(
          {
            title: 'The corporate action is not valid.',
            code: 'data.invalid',
            errors: { exDate: ['The ex-date is more than a year ahead.'], note: ['No odd characters.'] },
          },
          400,
        ),
      ),
    ];
    const { dialog } = await openAdd({ handlers: reject });

    await chooseInstrument(dialog, 'tcs', /NSE:TCS/);
    await fill(dialog, { date: '2027-12-01', n: '1', d: '5' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await within(dialog).findByText('The ex-date is more than a year ahead.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Ex-date/)).toBeInvalid();
    expect(within(dialog).getByText('No odd characters.')).toBeInTheDocument();
  });

  it('says in words that the action exists already (409) and stays open', async () => {
    const { dialog } = await openAdd();

    await chooseInstrument(dialog, 'inf', /NSE:INFY/);
    await fill(dialog, { date: '2026-09-01', n: '1', d: '5' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(
      await within(dialog).findByText(
        'This instrument already has an action of this kind on that ex-date. Edit that one instead.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Add corporate action' })).toBeInTheDocument();
  });

  it('says in words when the instrument is unknown to the server (404)', async () => {
    const reject = [
      http.post('/api/data/corporate-actions', () =>
        problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404),
      ),
    ];
    const { dialog } = await openAdd({ handlers: reject });

    await chooseInstrument(dialog, 'tcs', /NSE:TCS/);
    await fill(dialog, { date: '2026-12-01', n: '1', d: '5' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await within(dialog).findByText('That instrument is not in the instrument list.')).toBeInTheDocument();
  });

  it('sends an ex-date inside adjusted CSV history plain first; on the 409 it asks and repeats with consent', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    await chooseInstrument(dialog, 'inf', /NSE:INFY/);
    await fill(dialog, { date: '2026-04-30', n: '1', d: '5' });
    // No guess before sending: the API knows where each instrument's CSV files end.
    expect(within(dialog).queryByRole('note')).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    const question = await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' });
    expect(question).toHaveTextContent('NSE:INFY');
    expect(question).toHaveTextContent('up to 2026-04-30');
    expect(question).toHaveTextContent('adjust those prices twice');
    expect(fake.actions.calls.add).toHaveLength(1);
    expect(fake.actions.calls.add[0]).not.toHaveProperty('acknowledgeCsvAdjusted');
    expect(fake.actions.items).toHaveLength(sampleCorporateActions.length);

    // Cancel leaves the form as it was.
    await userEvent.click(within(question).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(
        screen.queryByRole('dialog', { name: 'Adjust history that is already adjusted?' }),
      ).not.toBeInTheDocument();
    });
    expect(within(dialog).getByLabelText(/^Ex-date/)).toHaveValue('2026-04-30');
    expect(fake.actions.calls.add).toHaveLength(1);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' })).getByRole(
        'button',
        { name: 'Adjust anyway' },
      ),
    );

    expect(await screen.findByText(/^Added NSE:INFY split 1:5 on 30 Apr 2026/)).toBeInTheDocument();
    expect(fake.actions.calls.add).toHaveLength(3);
    expect(fake.actions.calls.add[2]).toMatchObject({ exDate: '2026-04-30', acknowledgeCsvAdjusted: true });
    expect(fake.actions.items).toHaveLength(sampleCorporateActions.length + 1);
  });

  it('does not ask for an ex-date after the CSV data or for an instrument without CSV history', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    await chooseInstrument(dialog, 'tcs', /NSE:TCS/);
    await fill(dialog, { date: '2026-04-30', n: '1', d: '5' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText(/^Added NSE:TCS split 1:5 on 30 Apr 2026/)).toBeInTheDocument();
    expect(fake.actions.calls.guarded).toEqual([]);
  });

  it('says in words that an action of the other kind is on that day (409) and stays open', async () => {
    const { dialog } = await openAdd();

    await userEvent.selectOptions(within(dialog).getByLabelText('Kind'), 'split');
    await chooseInstrument(dialog, 'reli', /NSE:RELIANCE/);
    // Reliance has an applied bonus 1:1 on 11 Oct 2026.
    await fill(dialog, { date: '2026-10-11', n: '1', d: '5' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(
      await within(dialog).findByText(
        'Another action of the other kind exists on that date for this instrument — ignore it first.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Add corporate action' })).toBeInTheDocument();
  });

  it('closes on Cancel without sending anything', async () => {
    const fake = fakeWith();
    const { dialog } = await openAdd({ fake });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.actions.calls.add).toHaveLength(0);
  });
});

const csvText = [
  'symbol,exDate,kind,numerator,denominator,note',
  'TCS,2026-12-01,split,1,2,Planned split',
  'NSE:INFY,2026-09-01,split,1,5,',
  'NOPE,2026-12-01,bonus,1,1,',
  'RELIANCE,not-a-date,bonus,1,1,',
  'HDFCBANK,2026-12-02,split,3,3,',
].join('\n');

describe('Corporate actions: Import CSV', () => {
  const openImport = async (options: Parameters<typeof open>[0] = {}) => {
    const result = await open(options);
    await userEvent.click(within(result.card).getByRole('button', { name: 'Import CSV' }));
    const dialog = await screen.findByRole('dialog', { name: 'Import corporate actions from CSV' });
    return { ...result, dialog };
  };
  const paste = async (dialog: HTMLElement, text: string) => {
    await userEvent.click(within(dialog).getByRole('textbox', { name: 'CSV text' }));
    await userEvent.paste(text);
  };

  it('explains the columns, the limit of 5,000 lines and the ratio, and cannot preview an empty text', async () => {
    const { dialog } = await openImport();

    expect(within(dialog).getByText(/Columns: symbol, exDate, kind/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Up to 5,000 lines\./)).toBeInTheDocument();
    expect(within(dialog).getByText(/Split 1:5 is old shares : new shares/)).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Lines whose ex-date is on or before the day an instrument’s CSV files end/),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Preview' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Import' })).toBeDisabled();
  });

  it('previews the text: counts, unknown symbols and the lines with a problem, and saves nothing', async () => {
    const fake = fakeWith();
    const { dialog } = await openImport({ fake });

    await paste(dialog, csvText);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));

    const preview = await within(dialog).findByRole('region', { name: 'Import preview' });
    const count = (label: string) => within(preview).getByText(label).nextElementSibling?.textContent;
    expect(count('Lines read')).toBe('5');
    expect(count('Will be imported')).toBe('1');
    expect(count('Duplicates (skipped)')).toBe('1');
    expect(count('Invalid lines (skipped)')).toBe('2');
    expect(
      within(within(preview).getByRole('list', { name: /Unknown symbols/ })).getByText('NOPE'),
    ).toBeInTheDocument();
    const problems = within(preview).getByRole('list', { name: /Lines with a problem/ });
    expect(within(problems).getByText('Line 5: The ex-date must be yyyy-MM-dd.')).toBeInTheDocument();
    expect(within(problems).getByText('Line 6: A 1:1 split changes nothing.')).toBeInTheDocument();
    expect(fake.actions.calls.imports).toEqual([
      { preview: true, text: csvText, contentType: 'text/csv', acknowledge: false },
    ]);
    expect(fake.actions.items).toHaveLength(sampleCorporateActions.length);
    expect(within(dialog).getByRole('button', { name: 'Import 1' })).toBeEnabled();
  });

  it('imports after the preview, tells what was left out, closes and shows the new action', async () => {
    const fake = fakeWith();
    const { dialog, card } = await openImport({ fake });
    await paste(dialog, csvText);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    await within(dialog).findByRole('region', { name: 'Import preview' });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Import 1' }));

    expect(
      await screen.findByText('Imported 1 action (1 duplicates skipped, 2 invalid lines skipped).'),
    ).toBeInTheDocument();
    expect(fake.actions.calls.imports.map((call) => call.preview)).toEqual([true, false]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const added = await within(card).findByRole('row', { name: /NSE:TCS.*1:2.*CSV import.*Applied/ });
    expect(within(added).getByText('Planned split')).toBeInTheDocument();
  });

  describe('with lines inside adjusted CSV history', () => {
    const early = [
      'symbol,exDate,kind,numerator,denominator,note',
      'INFY,2026-02-02,split,1,2,',
      'RELIANCE,2026-03-03,bonus,1,1,',
      'TCS,2026-12-01,split,1,2,',
    ].join('\n');

    const preview = async (text = early) => {
      const result = await openImport();
      await paste(result.dialog, text);
      await userEvent.click(within(result.dialog).getByRole('button', { name: 'Preview' }));
      await within(result.dialog).findByRole('region', { name: 'Import preview' });
      return result;
    };

    it('lists them in a warning with line, symbol, ex-date and the date the CSV files reach', async () => {
      const { dialog } = await preview();

      const warning = within(dialog).getByRole('note');
      expect(warning).toHaveTextContent('2 lines adjust history that is already adjusted');
      const lines = within(within(warning).getByRole('list', { name: /adjust history/ })).getAllByRole('listitem');
      expect(lines.map((line) => line.textContent)).toEqual([
        'Line 2: NSE:INFY, ex-date 2 Feb 2026 — CSV files adjusted through 30 Apr 2026',
        'Line 3: NSE:RELIANCE, ex-date 3 Mar 2026 — CSV files adjusted through 30 Apr 2026',
      ]);
    });

    it('keeps Import disabled until the owner ticks the consent, then sends it with the import', async () => {
      const { dialog, fake } = await preview();
      const importButton = within(dialog).getByRole('button', { name: 'Import 3' });

      expect(importButton).toBeDisabled();
      await userEvent.click(
        within(dialog).getByRole('checkbox', {
          name: 'I understand these actions adjust history that is already adjusted',
        }),
      );
      expect(importButton).toBeEnabled();
      await userEvent.click(importButton);

      expect(await screen.findByText('Imported 3 actions.')).toBeInTheDocument();
      expect(fake.actions.calls.imports.map((call) => [call.preview, call.acknowledge])).toEqual([
        [true, false],
        [false, true],
      ]);
    });

    it('asks again for the consent after the text changes and a new preview', async () => {
      const { dialog } = await preview();
      const consent = () =>
        within(dialog).getByRole('checkbox', {
          name: 'I understand these actions adjust history that is already adjusted',
        });
      await userEvent.click(consent());

      await userEvent.type(within(dialog).getByRole('textbox', { name: 'CSV text' }), '\n');
      await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
      await within(dialog).findByRole('region', { name: 'Import preview' });

      expect(consent()).not.toBeChecked();
      expect(within(dialog).getByRole('button', { name: 'Import 3' })).toBeDisabled();
    });

    it('needs no consent when no line is inside CSV history', async () => {
      const { dialog, fake } = await preview(
        'symbol,exDate,kind,numerator,denominator\nTCS,2026-12-01,split,1,2\nINFY,2026-12-02,split,1,2',
      );

      expect(within(dialog).queryByRole('note')).not.toBeInTheDocument();
      expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument();
      await userEvent.click(within(dialog).getByRole('button', { name: 'Import 2' }));
      expect(await screen.findByText('Imported 2 actions.')).toBeInTheDocument();
      expect(fake.actions.calls.imports.at(-1)?.acknowledge).toBe(false);
    });

    it('says in words when the API refuses the import for lack of consent (409)', async () => {
      const { dialog } = await openImport({
        handlers: [
          http.post('/api/data/corporate-actions/import', ({ request }) =>
            new URL(request.url).searchParams.get('preview') === 'true'
              ? HttpResponse.json({
                  preview: true,
                  lines: 1,
                  importable: 1,
                  imported: 0,
                  duplicates: 0,
                  invalid: 0,
                  unknownSymbols: [],
                  errors: [],
                })
              : problemResponse(
                  { title: 'Already adjusted.', code: 'data.csv_already_adjusted', adjustsCsvTwice: 2, instruments: 1 },
                  409,
                ),
          ),
        ],
      });
      await paste(dialog, early);
      await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
      await userEvent.click(await within(dialog).findByRole('button', { name: 'Import 1' }));

      expect(
        await within(dialog).findByText(
          /2 actions on 1 instrument would adjust history that is already adjusted\. Confirm that you want this, or leave it out\./,
        ),
      ).toBeInTheDocument();
    });
  });

  it('shows the lines of other kinds on the same day with the prefix the API gives them (kind: …)', async () => {
    const { dialog } = await openImport();

    // Reliance has an applied bonus 1:1 on 11 Oct 2026, so a split on that day clashes.
    await paste(dialog, 'symbol,exDate,kind,numerator,denominator\nRELIANCE,2026-10-11,split,1,5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));

    const preview = await within(dialog).findByRole('region', { name: 'Import preview' });
    expect(
      within(within(preview).getByRole('list', { name: /Lines with a problem/ })).getByText(
        /^Line 2: kind: The instrument has a split or bonus of the other kind on this ex-date/,
      ),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Import' })).toBeDisabled();
  });

  it('reads a chosen file into the text', async () => {
    const fake = fakeWith();
    const { dialog } = await openImport({ fake });

    await userEvent.upload(
      within(dialog).getByLabelText('CSV file'),
      new File([csvText], 'actions.csv', { type: 'text/csv' }),
    );

    expect(await within(dialog).findByDisplayValue(/^symbol,exDate,kind/)).toBeInTheDocument();
    expect(within(dialog).getByText('actions.csv')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByRole('region', { name: 'Import preview' })).toBeInTheDocument();
  });

  it('asks for a new preview when the text changes, and does not import without one', async () => {
    const { dialog } = await openImport();
    await paste(dialog, csvText);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    await within(dialog).findByRole('region', { name: 'Import preview' });

    await userEvent.type(within(dialog).getByRole('textbox', { name: 'CSV text' }), '\n');

    expect(within(dialog).queryByRole('region', { name: 'Import preview' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Import' })).toBeDisabled();
  });

  it('cannot import when the preview finds nothing to import', async () => {
    const { dialog } = await openImport();

    await paste(dialog, 'symbol,exDate,kind,numerator,denominator\nNOPE,2026-12-01,split,1,5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));

    const preview = await within(dialog).findByRole('region', { name: 'Import preview' });
    expect(within(preview).getByText('NOPE')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Import' })).toBeDisabled();
  });

  it('says in words what the server refused', async () => {
    const reject = [
      http.post('/api/data/corporate-actions/import', () =>
        problemResponse(
          { title: 'The text is not valid.', code: 'data.invalid', errors: { text: ['At most 5000 lines.'] } },
          400,
        ),
      ),
    ];
    const { dialog } = await openImport({ handlers: reject });

    await paste(dialog, csvText);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));

    expect(await within(dialog).findByText(/The text is not valid\. At most 5000 lines\./)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Import' })).toBeDisabled();
  });
});

describe('Corporate actions: Detect now', () => {
  const found = [
    buildCorporateAction({
      id: '00000000-0000-4000-8000-000000000561',
      instrumentId: sampleCoverageId('NSE:HDFCBANK'),
      symbol: 'NSE:HDFCBANK',
      exDate: '2026-07-01',
    }),
    buildCorporateAction({
      id: '00000000-0000-4000-8000-000000000562',
      instrumentId: sampleCoverageId('NSE:RELIANCE'),
      symbol: 'NSE:RELIANCE',
      exDate: '2026-07-02',
    }),
  ];

  it('runs the detection, says how many suggestions it found, and shows them', async () => {
    const fake = fakeWith({ detectFinds: found });
    const { card } = await open({ fake });

    await userEvent.click(within(card).getByRole('button', { name: 'Detect now' }));

    expect(await screen.findByText('2 suggestions for review. Checked 4 instruments.')).toBeInTheDocument();
    expect(fake.actions.calls.detect).toBe(1);
    expect(await within(card).findByRole('button', { name: 'Suggested 4' })).toBeInTheDocument();
    expect(
      within(await screen.findByRole('region', { name: 'Needs attention' })).getByRole('link', {
        name: '4 suggested corporate actions',
      }),
    ).toBeInTheDocument();
  });

  it('says so when it found nothing new, and how many still wait', async () => {
    const fake = fakeWith();
    const { card } = await open({ fake });

    await userEvent.click(within(card).getByRole('button', { name: 'Detect now' }));

    expect(
      await screen.findByText('No new suggestions. Checked 4 instruments; 2 waiting for review.'),
    ).toBeInTheDocument();
    expect(rowsIn(grid())).toHaveLength(5);
  });

  it('says in words when the detection fails', async () => {
    const reject = [
      http.post('/api/data/corporate-actions/detect', () =>
        problemResponse({ title: 'The detection is not available.', code: 'data.down' }, 500),
      ),
    ];
    const { card } = await open({ handlers: reject });

    await userEvent.click(within(card).getByRole('button', { name: 'Detect now' }));

    expect(await screen.findByText('The detection is not available.')).toBeInTheDocument();
  });
});

/** The id the sample coverage gives a symbol. */
function sampleCoverageId(symbol: string): string {
  const row = coverageWithCsvEnd().find((r) => r.symbol === symbol);
  if (!row) throw new Error(`no ${symbol}`);
  return row.id;
}
