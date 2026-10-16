// The contract tests (run from frontend/, as npm does) read two files of the repository (the API contract, a backend source) with node:fs. Only this one
// function is declared, so the Node typings do not leak into the app's code (which targets the browser).
declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf8'): string;
}
