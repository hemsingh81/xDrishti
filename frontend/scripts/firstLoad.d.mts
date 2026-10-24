export const BUDGET_BYTES: number;
export function firstLoadAssets(html: string): string[];
export function gzipBytes(content: string | Uint8Array): number;
export interface FirstLoadResult {
  chunks: { href: string; gzip: number }[];
  total: number;
  budget: number;
  withinBudget: boolean;
  headroom: number;
}
export function measureFirstLoad(
  html: string,
  readAsset: (href: string) => string | Uint8Array,
  budget?: number,
): FirstLoadResult;
export function kb(bytes: number): string;
export function report(result: FirstLoadResult): string[];
