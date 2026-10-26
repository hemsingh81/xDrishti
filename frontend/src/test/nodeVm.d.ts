// The service worker test runs public/sw.js in a vm context with a fake `self` (P8-21).
declare module 'node:vm' {
  export type Context = object;
  export function createContext(sandbox: object): Context;
  export function runInContext(code: string, context: Context): unknown;
  const vm: { createContext: typeof createContext; runInContext: typeof runInContext };
  export default vm;
}
