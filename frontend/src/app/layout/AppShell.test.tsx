import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import { renderRoutes } from '@/test/render';
import { routes } from '../router';

// jsdom has no layout, so these tests pin the structure that makes the layout work in a browser: which elements are
// full width, which are flex columns that may shrink, and where the one scroll container is.

const from = (element: Element, width: number) =>
  appliedCss(element).responsive.find((r) => r.query.includes(`min-width:${String(width)}px`))?.css ?? '';
/** Matches a whole declaration (`height: 100%` but not `min-height: 100%`). */
const declares = (css: string, declaration: string) =>
  new RegExp(`(^|[\\s;])${declaration.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(;|$)`).test(css);
const atMd = (element: Element) => from(element, 900);
const flexColumn = (css: string) => {
  expect(declares(css, 'display: flex')).toBe(true);
  expect(declares(css, 'flex-direction: column')).toBe(true);
};

describe('App shell layout', () => {
  it('lets the content fill the width on every page: padding only, no max-width', async () => {
    for (const path of ['/accounts', '/settings', '/system', '/']) {
      const { unmount } = renderRoutes(routes, { initialPath: path });
      await screen.findByRole('heading', { level: 1 });

      const main = appliedCss(screen.getByRole('main'));
      expect(declares(main.base, 'width: 100%')).toBe(true);
      expect(main.base).not.toMatch(/max-width/);
      // The prototype's page padding: 14 px on phones, 24 px from md, 28 / 36 px and 32 / 48 px on wide monitors.
      expect(declares(main.base, 'padding: 14px')).toBe(true);
      expect(declares(atMd(screen.getByRole('main')), 'padding: 24px')).toBe(true);
      expect(declares(from(screen.getByRole('main'), 1680), 'padding: 28px 36px')).toBe(true);
      expect(declares(from(screen.getByRole('main'), 2560), 'padding: 32px 48px')).toBe(true);
      unmount();
    }
  });

  it('is the viewport height from md up, with main as the one scroll container; below md the page scrolls', async () => {
    renderRoutes(routes, { initialPath: '/system' });
    await screen.findByRole('heading', { level: 1, name: 'System status' });

    const main = screen.getByRole('main');
    expect(declares(atMd(main), 'overflow-y: auto')).toBe(true);
    expect(declares(appliedCss(main).base, 'min-height: 0px')).toBe(true);
    expect(appliedCss(main).base).not.toMatch(/overflow/);
    const column = main.parentElement!;
    flexColumn(appliedCss(column).base);
    expect(declares(appliedCss(column).base, 'min-height: 0px')).toBe(true);
    const root = column.parentElement!;
    expect(declares(appliedCss(root).base, 'min-height: 100dvh')).toBe(true);
    expect(declares(atMd(root), 'height: 100dvh')).toBe(true);
  });
});

describe('Instruments layout', () => {
  it('fills the main area as a flex column, with the grid body as the only scrolling part', async () => {
    renderRoutes(routes, { initialPath: '/instruments' });
    const grid = await screen.findByRole('grid', { name: 'Instruments' });
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    const main = screen.getByRole('main');
    const page = main.firstElementChild!;
    expect(declares(atMd(page), 'height: 100%')).toBe(true);
    expect(appliedCss(page).base).not.toMatch(/(^|[\s;])height:/); // below md it is as tall as its content
    flexColumn(appliedCss(page).base);
    expect(declares(appliedCss(page).base, 'min-height: 0px')).toBe(true);

    // Header and filters sit outside the scroll area, as do the footer and the sticky column titles' container.
    const scroller = grid.parentElement!;
    expect(declares(appliedCss(scroller).base, 'overflow: auto')).toBe(true);
    expect(declares(appliedCss(scroller).base, 'min-height: 0px')).toBe(true);
    expect(scroller.contains(screen.getByRole('heading', { level: 1, name: 'Instruments' }))).toBe(false);
    expect(scroller.contains(screen.getByRole('searchbox', { name: 'Search instruments' }))).toBe(false);
    expect(scroller.contains(screen.getByText(/^Showing \d+ of \d+$/))).toBe(false);
    expect(screen.getByRole('columnheader', { name: 'Symbol' }).closest('[class*="MuiTableContainer"]')).toBeNull();
    expect(scroller.contains(screen.getByRole('columnheader', { name: 'Symbol' }))).toBe(true);

    // Every wrapper between the page and the scroller is a flex column that can shrink, or the body would not scroll.
    let wrapper: HTMLElement | null = scroller.parentElement;
    let depth = 0;
    while (wrapper && wrapper !== page) {
      const css = appliedCss(wrapper).base;
      flexColumn(css);
      wrapper = wrapper.parentElement;
      depth += 1;
    }
    expect(wrapper).toBe(page);
    expect(depth).toBeGreaterThan(0);
  });

  it('has a skip link as the first tab stop that moves focus to the main region', async () => {
    renderRoutes(routes, { initialPath: '/system' });
    await screen.findByRole('heading', { level: 1, name: 'System status' });
    await userEvent.tab();
    const skip = screen.getByRole('link', { name: 'Skip to content' });
    expect(skip).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('main')).toHaveFocus();
  });
});
