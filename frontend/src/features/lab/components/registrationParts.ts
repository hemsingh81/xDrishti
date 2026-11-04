import type { RegistrationInput } from '../api/confidenceQueries';

export type PartKey = keyof RegistrationInput;

/** The five parts of a registration (spec CF-06, D1), in the order they are written. */
export const registrationParts: { key: PartKey; label: string; hint: string }[] = [
  {
    key: 'counterparty',
    label: 'Who is on the other side, and why do they lose?',
    hint: 'For example a forced seller, a slow reaction, a risk-transfer premium.',
  },
  {
    key: 'parameterSigns',
    label: "Expected sign of each parameter's effect",
    hint: 'For each parameter: does a higher value help or hurt, and why?',
  },
  {
    key: 'applicability',
    label: 'Instrument tiers and sessions it applies to',
    hint: 'Where the idea should work, and where not.',
  },
  {
    key: 'expectedEdge',
    label: 'Expected gross edge (bps) and the cost it must beat',
    hint: 'The edge before costs, and the round-trip cost it has to clear.',
  },
  {
    key: 'falsifier',
    label: 'Falsifier',
    hint: 'What result retires it: an out-of-sample expectancy under X, a placebo scoring within Y.',
  },
];
