const refusals: Record<string, string> = {
  halt_active: 'a halt is in force',
  not_flat: 'the account is not flat (a position or working order, or an open intent)',
  no_snapshot: 'the sandbox book or our record could not be read',
  halts_unreadable: 'halts could not be read',
};

/** The execution service's answer to a drill request, in plain words. No answer yet is not a result. */
export function drillAnswer(result: string | null, code: string | null): string {
  if (result === null) return 'Waiting for the execution service';
  if (code === null) return result; // an answer without a code is shown as received, never as a pass
  if (code === 'execution.drill_passed') return 'Passed';
  if (code === 'execution.drill_failed') return 'Failed';
  if (code === 'execution.drill_unavailable')
    return 'Not available in this environment (a drill runs only in the sandbox)';
  const prefix = 'execution.drill_refused:';
  if (code.startsWith(prefix)) {
    const why = code.slice(prefix.length);
    return `Refused: ${refusals[why] ?? why}`;
  }
  return code;
}
