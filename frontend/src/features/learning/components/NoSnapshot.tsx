import { NonValue } from '@/shared/analytics';

/** The first view on a machine that has not computed the cells yet. An ordinary answer, said in words. */
export function NoSnapshot() {
  return <NonValue state={{ kind: 'empty', reason: 'No snapshot yet; run `xd learn cells`.' }} />;
}
