import { readFileSync } from 'node:fs';
import { dropReasonCodes } from './dropReasons';

// The bell's text for a "rows dropped" item is built by the server (DropReasonWords.cs), the sync line's by dropReasons.ts.
// The two tables are written separately and the words may differ (the bell may use shorter ones), so only the SET of reason
// codes is compared: a code added on one side and not the other fails here.
const wordsPath = '../backend/src/XDrishti.Application/Portfolio/Attention/DropReasonWords.cs';
const reasonsPath = '../backend/src/XDrishti.Application/Abstractions/Portfolio/BrokerDropReasons.cs';

/** The string constants of BrokerDropReasons: `public const string BadQuantity = "bad_quantity";`. */
function codesByConstant(source: string): Map<string, string> {
  const map = new Map<string, string>();
  for (const [, name, code] of source.matchAll(/public const string (\w+)\s*=\s*"([a-z_]+)";/g)) {
    if (name && code) map.set(name, code);
  }
  return map;
}

describe('drop reason words', () => {
  it('cover the same reason codes on the server (the bell) and in the app (the sync line)', () => {
    const constants = codesByConstant(readFileSync(reasonsPath, 'utf8'));
    constants.set('Other', constants.get('Other') ?? 'other');
    const serverCodes = [...readFileSync(wordsPath, 'utf8').matchAll(/\[BrokerDropReasons\.(\w+)\]\s*=\s*"/g)].map(
      ([, constant]) => constants.get(constant ?? '') ?? `unresolved:${constant ?? ''}`,
    );

    expect(serverCodes).toHaveLength(14);
    expect([...dropReasonCodes].sort()).toEqual([...serverCodes].sort());
  });
});
