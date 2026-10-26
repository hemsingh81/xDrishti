import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { fail, pass, type Result, type Scheme } from './types';

const wcag = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** C5 (light) / C8 (dark): zero axe violations of the WCAG tags; best-practice findings are only notes. */
export async function axeCheck(page: Page, scheme: Scheme): Promise<Result> {
  const id = scheme === 'dark' ? 'C8' : 'C5';
  const builder = new AxeBuilder({
    page: page as unknown as ConstructorParameters<typeof AxeBuilder>[0]['page'],
  }).withTags([...wcag, 'best-practice']);
  const { violations } = await builder.analyze();
  const hard = violations.filter((v) => v.tags.some((t) => wcag.includes(t)));
  const soft = violations.filter((v) => !hard.includes(v));
  const softNote = soft.length > 0 ? ` (best-practice notes: ${soft.map((v) => v.id).join(', ')})` : '';
  if (hard.length === 0) return pass(id, `no violations${softNote}`);
  const text = hard
    .map((v) => `${v.id} x${v.nodes.length} [${v.nodes[0]?.target.join(' ') ?? ''}]`)
    .slice(0, 5)
    .join(' | ');
  return fail(id, `${text}${softNote}`);
}
