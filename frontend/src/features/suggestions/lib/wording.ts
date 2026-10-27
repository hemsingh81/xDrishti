/** The fixed words of the Suggestions screen (spec P9-08 AC 14). Text from the server is shown as text and never as markup. */
export const words = {
  title: 'Suggestions',
  intro:
    'The local model proposes; the engine re-reads the evidence and checks every rule; you decide. Nothing changes until you accept, and accepting never tracks an instrument, starts a backfill, approves or runs anything, or places an order.',
  empty: 'No suggestions',
  switchOff: 'Accepting is switched off. Turn it on in the assistant settings (it asks your password).',
  stale: 'The checks no longer pass on today’s data, so nothing was changed.',
  closed: 'This suggestion is closed and can no longer be decided.',
  trial: 'Each judged hypothesis counts as a trial.',
} as const;

export const kindLabel: Record<string, string> = {
  hypothesis: 'Hypothesis',
  basket_add: 'Add to basket',
  basket_remove: 'Remove from basket',
};

export function figureText(f: { label: string; value: string; state: string; n: number | null }): string {
  if (f.state !== 'value') return `${f.label}: ${f.state.replace('_', ' ')}`;
  return `${f.label}: ${f.value}`;
}
