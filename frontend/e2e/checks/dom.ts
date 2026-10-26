import type { XdWindow } from './types';

/**
 * Runs inside the page before any script (page.addInitScript): a selector helper for reporting offenders, a log of what
 * the live regions were told (C15) and the layout shift total (C14). Self-contained: it is serialised into the page.
 */
export function installProbes(): void {
  const w = window as unknown as XdWindow;
  w.__sel = (element) => {
    const parts: string[] = [];
    let node: Element | null = element;
    while (node && node !== document.documentElement && parts.length < 4) {
      const id = node.id ? `#${node.id}` : '';
      const role = node.getAttribute('role');
      const label = node.getAttribute('aria-label');
      const cls = [...node.classList].find((c) => c.startsWith('Mui')) ?? '';
      parts.unshift(
        `${node.tagName.toLowerCase()}${id}${role ? `[role=${role}]` : ''}${label ? `[aria-label="${label.slice(0, 30)}"]` : ''}${cls ? `.${cls}` : ''}`,
      );
      if (id) break;
      node = node.parentElement;
    }
    return parts.join(' > ');
  };
  w.__live = [];
  w.__cls = 0;
  try {
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        const shift = entry as unknown as { hadRecentInput: boolean; value: number };
        if (!shift.hadRecentInput) w.__cls += shift.value;
      }
    }).observe({ type: 'layout-shift', buffered: true });
  } catch {
    // layout-shift entries are not supported: CLS stays 0 and the check says so.
  }
  const liveSelector = '[role=status],[role=alert],[aria-live]:not([aria-live=off])';
  const note = (region: Element) => {
    const text = region.textContent.trim();
    const line = `${region.getAttribute('role') ?? 'live'}: ${text.slice(0, 80)}`;
    // The same region reporting the same text again is one announcement, not two.
    if (text && w.__live[w.__live.length - 1] !== line) w.__live.push(line);
  };
  new MutationObserver((mutations) => {
    for (const mutation of mutations) {
      if (mutation.type === 'attributes') {
        if (mutation.attributeName === 'data-ready' && (mutation.target as Element).hasAttribute('data-ready')) {
          if (performance.getEntriesByName('xd-ready').length === 0) performance.mark('xd-ready');
        }
        continue;
      }
      const target = mutation.target instanceof Element ? mutation.target : mutation.target.parentElement;
      const region = target?.closest(liveSelector);
      if (region) {
        note(region);
        continue;
      }
      for (const added of mutation.addedNodes) {
        if (!(added instanceof Element)) continue;
        if (added.matches(liveSelector)) note(added);
        else added.querySelectorAll(liveSelector).forEach(note);
      }
    }
  }).observe(document, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ['data-ready'],
  });
}
