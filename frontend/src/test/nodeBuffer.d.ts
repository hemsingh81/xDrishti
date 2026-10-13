// Node's own `Blob` and `File` (the app's types do not include Node's): `multipart.ts` needs them because the jsdom
// environment replaces the globals and Node's `fetch` takes only Node's.
declare module 'node:buffer' {
  export const Blob: typeof globalThis.Blob;
  export const File: typeof globalThis.File;
}
