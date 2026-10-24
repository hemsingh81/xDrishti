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
export const WARN_HEADROOM_BYTES: number;
export const ALWAYS_NEEDED_CHUNKS: string[];
export interface AlwaysNeeded {
  lazy: { href: string; gzip: number }[];
  styles: { href: string; gzip: number }[];
  fonts: { href: string; bytes: number }[];
  total: number;
}
export function measureAlwaysNeeded(
  html: string,
  readAsset: (href: string) => string | Uint8Array,
  assetNames: string[],
  guarded: FirstLoadResult,
): AlwaysNeeded;
export function reportAlwaysNeeded(needed: AlwaysNeeded, guarded: FirstLoadResult): string[];
export function warning(result: FirstLoadResult): string | null;
