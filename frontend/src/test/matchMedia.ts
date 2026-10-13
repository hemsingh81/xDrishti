/**
 * jsdom does not evaluate media queries. Stubs `window.matchMedia` so that exactly the queries `matches` accepts match
 * (undo with `vi.unstubAllGlobals()`).
 */
export function stubMatchMedia(matches: (query: string) => boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: matches(query),
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}

/** A phone: every width query matches (the theme's `down('sm')` and `down('md')`), `prefers-reduced-motion` does not. */
export const stubPhoneWidth = () => {
  stubMatchMedia((query) => !query.includes('prefers-reduced-motion'));
};

/** `prefers-reduced-motion: reduce` on a wide screen. */
export const stubReducedMotion = () => {
  stubMatchMedia((query) => query.includes('prefers-reduced-motion'));
};
