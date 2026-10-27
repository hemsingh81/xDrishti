/** The plain-words reason a fixed-format text was stored instead of a model text (the codes are the API's, spec P9-06). */
export function reasonWords(reason: string | null | undefined): string {
  switch (reason) {
    case 'model_off':
      return 'the model switch is off';
    case 'model_unavailable':
      return 'the model was not running or did not answer';
    case 'guard_rejected':
      return 'the checker rejected the model text';
    case 'facts_thin':
      return 'there was too little in the stored plan to phrase';
    default:
      return 'no reason was recorded';
  }
}

export const modelLabel = 'Drafted by a local model from stored records; every number was checked against them.';

export const fixedLabel = (reason: string | null | undefined) =>
  `Fixed-format text; the model was not used (${reasonWords(reason)}).`;
