import type { ReviewAccount } from '../api/planQueries';

export const standingText: Record<string, string> = {
  passed: 'Passed',
  warned: 'Warned',
  needsconfirmation: 'Needs confirmation',
  confirmeddespite: 'Confirmed despite',
  refused: 'Refused (cannot be overridden)',
};

const isPassed = (c: ReviewAccount['checks'][number]) =>
  c.standing.toLowerCase() === 'passed' && c.outcome.toLowerCase() === 'pass';

/** The one-line standing of an account's checklist, as text: "12 checks, 12 passed", "1 warned, 1 needs confirmation", "refused, cannot be overridden". */
export function standingLine(account: ReviewAccount): string {
  const checks = account.checks;
  if (checks.length === 0) return 'no checklist was stored';
  const count = (standing: string) => checks.filter((c) => c.standing.toLowerCase() === standing).length;
  if (count('refused') > 0) return 'refused, cannot be overridden';
  if (checks.every(isPassed)) return `${String(checks.length)} checks, ${String(checks.length)} passed`;
  const parts = [
    [count('warned'), 'warned'],
    [count('needsconfirmation'), 'needs confirmation'],
    [count('confirmeddespite'), 'confirmed despite'],
  ] as const;
  const named = parts.filter(([n]) => n > 0).map(([n, text]) => `${String(n)} ${text}`);
  // A check that is neither passed nor one of the named standings (an unknown word) is still said, never hidden.
  const other = checks.filter((c) => !isPassed(c)).length - parts.reduce((sum, [n]) => sum + n, 0);
  return [...named, ...(other > 0 ? [`${String(other)} not passed`] : [])].join(', ');
}

/** The checks to list without being asked: those not passed. */
export const notPassed = (account: ReviewAccount) => account.checks.filter((c) => !isPassed(c));
