/** Whether two lists of run keys name the same set of runs, in any order. */
export const sameRunSet = (a: readonly string[], b: readonly string[]): boolean =>
  a.length === b.length && a.every((key) => b.includes(key));
