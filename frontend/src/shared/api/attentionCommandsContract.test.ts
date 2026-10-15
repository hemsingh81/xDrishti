import { readFileSync } from 'node:fs';
import { attentionCommands } from './attentionCommands.generated';

// The generated list (attentionCommands.generated.ts) must be exactly what the server's contract names (ADR 0030):
// a stale or hand-edited file fails here. Regenerate with scripts/gen-api.sh.
const contractPath = '../backend/contracts/XDrishti.Api.json';

describe('attentionCommands.generated.ts', () => {
  it('equals the x-xdrishti-attention-commands extension of the API contract: names, methods, templates and patterns', () => {
    const contract = JSON.parse(readFileSync(contractPath, 'utf8')) as Record<string, unknown>;
    const fromContract = contract['x-xdrishti-attention-commands'];

    expect(Array.isArray(fromContract)).toBe(true);
    expect(fromContract).toEqual(
      attentionCommands.map(({ name, method, template, pattern }) => ({ name, method, template, pattern })),
    );
  });
});
