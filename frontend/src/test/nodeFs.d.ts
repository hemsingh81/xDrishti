// The contract tests (run from frontend/, as npm does) read files of the repository (the API contract, a backend source, the PWA files) with node:fs.
// Only these functions are declared, so the Node typings do not leak into the app's code (which targets the browser).
declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf8'): string;
  export function readFileSync(path: string): {
    readUInt32BE(offset: number): number;
    subarray(from: number, to: number): { toString(): string };
  };
  export function existsSync(path: string): boolean;
}
