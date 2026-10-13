import { act } from '@testing-library/react';

/**
 * jsdom has no layout, so no IntersectionObserver. This stand-in never reports anything by itself; a test "scrolls to the
 * end" with `scrollToEnd()`, which reports every observed element as intersecting. Installed for every test (setup.ts).
 */
type Callback = (entries: IntersectionObserverEntry[], observer: IntersectionObserver) => void;

class FakeIntersectionObserver implements IntersectionObserver {
  static live = new Set<FakeIntersectionObserver>();
  readonly root: Element | Document | null;
  readonly rootMargin: string;
  readonly thresholds: readonly number[] = [0];
  readonly targets = new Set<Element>();

  constructor(
    private readonly callback: Callback,
    options: IntersectionObserverInit = {},
  ) {
    this.root = options.root ?? null;
    this.rootMargin = options.rootMargin ?? '0px';
    FakeIntersectionObserver.live.add(this);
  }

  observe(target: Element) {
    this.targets.add(target);
  }
  unobserve(target: Element) {
    this.targets.delete(target);
  }
  disconnect() {
    this.targets.clear();
    FakeIntersectionObserver.live.delete(this);
  }
  takeRecords(): IntersectionObserverEntry[] {
    return [];
  }

  report(isIntersecting: boolean) {
    const entries = [...this.targets].map((target) => ({ target, isIntersecting }) as IntersectionObserverEntry);
    if (entries.length > 0) this.callback(entries, this);
  }
}

export function installFakeIntersectionObserver() {
  globalThis.IntersectionObserver = FakeIntersectionObserver;
}

/** Reports every observed element as intersecting, as the browser does when the list end nears the viewport. */
export function scrollToEnd() {
  act(() => {
    for (const observer of [...FakeIntersectionObserver.live]) observer.report(true);
  });
}

/** Observers currently watching something (a grid that may load more has one; a finished or loading one has none). */
export const observedCount = () => [...FakeIntersectionObserver.live].filter((o) => o.targets.size > 0).length;

/** The `rootMargin` of the live observers, for a test of the look-ahead. */
export const observerMargins = () => [...FakeIntersectionObserver.live].map((o) => o.rootMargin);

/** Every element the live observers watch: a test can tell whether a part of the page is observed at all. */
export const observedElements = (): Element[] => [...FakeIntersectionObserver.live].flatMap((o) => [...o.targets]);
