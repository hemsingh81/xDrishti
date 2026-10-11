import { splitImportErrors } from './importErrors';

describe('import errors', () => {
  const rows = [
    { key: 'A', acquiredOn: null },
    { key: 'B', acquiredOn: null },
  ];

  it('puts row messages on the keys that were sent and keeps the rest', () => {
    const { byKey, rest } = splitImportErrors(
      {
        'firstImport.rows[1].acquiredOn': 'Future.',
        firstImportDate: 'Bad.',
        'firstImport.rows': 'Too many.',
        'firstImport.rows[7].key': 'Required.',
      },
      rows,
    );

    expect(byKey).toEqual({ B: 'Future.' });
    expect(rest).toEqual({
      firstImportDate: 'Bad.',
      'firstImport.rows': 'Too many.',
      'firstImport.rows[7].key': 'Required.',
    });
  });
});
