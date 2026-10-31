/** The rungs that exist in this build, lowest first (ADR 0163). */
export const rungs = ['paper', 'manual_live', 'assisted', 'semi_auto', 'auto_live'] as const;

export const rungBelow = (mode: string): string | null => {
  const at = rungs.findIndex((r) => r === mode);
  return at > 0 ? (rungs[at - 1] ?? null) : null;
};
