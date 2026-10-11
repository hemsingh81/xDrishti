/**
 * jsdom does not evaluate media queries, so layout tests read the style rules that apply to an element: the plain
 * declarations (`base`) and, per media query, the declarations that apply from that width (`responsive`).
 *
 * Limits: it reads the rules emotion injected into `document.styleSheets` and matches only selectors that are exactly
 * one of the element's classes (`.css-abc`), at top level or one `@media` deep. Pseudo-classes (`:hover`), descendant or
 * compound selectors (`.a .b`, `.a.Mui-selected`), `@supports` and nested media are ignored, as are inline styles. It
 * does not resolve the cascade or compute widths: it answers "which declarations does this component set", not "what
 * does the browser end up showing". Use a real browser for the latter.
 */
export interface AppliedCss {
  base: string;
  responsive: { query: string; css: string }[];
}

/** Declarations as `prop: value;` with single spaces, e.g. `display: flex; flex-direction: column;`. */
const compact = (css: string) => css.replace(/\s+/g, ' ').trim();

export function appliedCss(element: Element): AppliedCss {
  const classes = Array.from(element.classList);
  const matches = (selector: string) =>
    classes.some((name) => selector.split(',').some((s) => s.trim() === `.${name}`));
  const result: AppliedCss = { base: '', responsive: [] };
  for (const sheet of Array.from(document.styleSheets)) {
    for (const rule of Array.from(sheet.cssRules)) {
      if ('selectorText' in rule && matches((rule as CSSStyleRule).selectorText)) {
        result.base += ` ${compact((rule as CSSStyleRule).style.cssText)}`;
      } else if ('cssRules' in rule && 'media' in rule) {
        const query = compact((rule as CSSMediaRule).media.mediaText);
        for (const inner of Array.from((rule as CSSMediaRule).cssRules)) {
          if ('selectorText' in inner && matches((inner as CSSStyleRule).selectorText)) {
            result.responsive.push({ query, css: compact((inner as CSSStyleRule).style.cssText) });
          }
        }
      }
    }
  }
  result.base = result.base.trim();
  return result;
}
