export const MET: 'MET';
export const NOT_MET: 'NOT MET';
export const NOT_EVALUABLE: 'NOT EVALUABLE';
export interface Judgement {
  verdict: string;
  detail: string;
  caveat: string | null;
}
export interface Criterion extends Judgement {
  number: number;
  name: string;
}
export type CodeChanged = (recorded: string) => boolean | null;
type Timing = { answerPath: boolean; coldMs: number | null; warmMs: number | null };
export const COMMIT_PATTERN: RegExp;
export function freshness(
  recorded: unknown,
  head: string,
  codeChanged: CodeChanged,
  dirty?: boolean,
): { state: 'current' | 'accepted' | 'stale'; note: string };
export function judgeBundle(record: unknown, head: string, codeChanged: CodeChanged, dirty?: boolean): Judgement;
export function judgeChecklist(
  lines: string[] | null,
  head: string,
  codeChanged: CodeChanged,
  builtScreens: string[] | null,
  dirty?: boolean,
): Judgement;
export function serverTiming(timings: Timing[]): string | null;
export function judgeExitQuestion(
  input: { runs: number; timings: Timing[]; interaction: unknown; stopwatch: unknown },
  head: string,
  codeChanged: CodeChanged,
  dirty?: boolean,
): Judgement;
export function judgeReadOnlyContract(contract: unknown, approved: string[]): Judgement;
export function approvedWrites(source: string): string[];
export function headline(criteria: { number: number; verdict: string; caveat: string | null }[]): string;
export function parseServerText(text: string): {
  criteria: Criterion[];
  runs: number;
  timings: (Timing & { operation: string })[];
};
export function mergeCriteria(server: { criteria: Criterion[] }, local: Record<number, Judgement>): Criterion[];
export function render(criteria: Criterion[], head: string): string;
export function parseStopwatch(args: Record<string, string | undefined>):
  | { error: string }
  | {
      trials: { seconds: number; interactions: number }[];
      date: string;
      answer: string;
      basket: string;
      note: string;
      error?: undefined;
    };
