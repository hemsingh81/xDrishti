/**
 * The min-height values (px) the stylesheet sets for an element, read from the CSS text so responsive rules (media
 * queries, which jsdom does not evaluate) count. Empty when the element reserves no height.
 */
export function reservedHeights(element: Element): number[] {
  const classes = [...element.classList];
  const found: number[] = [];
  const visit = (rules: CSSRuleList) => {
    for (const rule of Array.from(rules)) {
      if (rule instanceof CSSStyleRule) {
        const own = rule.selectorText.split(/[\s>+~,]/).some((part) => classes.some((c) => part === `.${c}`));
        const value = /min-height:\s*(\d+)px/.exec(rule.cssText);
        if (own && value?.[1]) found.push(Number(value[1]));
      } else if ('cssRules' in rule) {
        visit((rule as CSSGroupingRule).cssRules);
      }
    }
  };
  for (const sheet of Array.from(document.styleSheets)) visit(sheet.cssRules);
  return found;
}
