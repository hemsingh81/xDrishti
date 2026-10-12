import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import {
  buildBarsOverview,
  buildCalendarDay,
  buildDataOverview,
  buildDhanOverview,
  buildEodOverview,
  buildEodRun,
  ids,
} from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { ready, renderData } from './testing';

const day = 86_400_000;
const daysAgo = (n: number) => new Date(Date.now() - n * day).toISOString();
const lastTick = async () => within(await ready()).getByRole('region', { name: 'Last tick' });
const withBars = (bars: Parameters<typeof buildBarsOverview>[0], rest: Parameters<typeof buildDataOverview>[0] = {}) =>
  createDataFake({ overview: buildDataOverview({ bars: buildBarsOverview(bars), ...rest }) });

describe('Status ribbon: the last tick', () => {
  it('highlights the last tick in India time as current, in words and with an icon', async () => {
    renderData({ fake: withBars({ lastBar: daysAgo(3) }) });

    const tick = await lastTick();
    expect(within(tick).getByText('3 days ago')).toBeInTheDocument();
    expect(within(tick).getByText(/^Up to date · the 12 Oct 2026 session is in$/)).toBeInTheDocument();
    expect(within(tick).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).queryByText(/Behind/)).not.toBeInTheDocument();
  });

  it('shows the exact last bar in IST whatever the viewer’s zone', async () => {
    renderData();

    const time = within(await lastTick()).getByText('12 Oct 2026, 15:29 IST');
    expect(time).toHaveAttribute('datetime', '2026-10-12T15:29:00+05:30');
  });

  it('marks data that is behind with the warning text and icon, not by colour alone', async () => {
    renderData({
      fake: withBars({ lastBar: daysAgo(3), stale: true, daysBehind: 2, instrumentsBehind: 12, instrumentsEmpty: 3 }),
    });

    const tick = await lastTick();
    expect(within(tick).getByText('Behind by 2 trading days · expected the 12 Oct 2026 session')).toBeInTheDocument();
    expect(within(tick).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).getByText('3 days ago')).toBeInTheDocument();
    expect(within(tick).queryByText(/Up to date/)).not.toBeInTheDocument();
  });

  it('does not say "Up to date" when the newest bar is current but instruments are behind', async () => {
    renderData({ fake: withBars({ instrumentsBehind: 213 }) });

    const tick = await lastTick();
    expect(within(tick).getByText('Newest bar is current — 213 instruments behind')).toBeInTheDocument();
    expect(within(tick).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).queryByText(/Up to date/)).not.toBeInTheDocument();
  });

  it('says "1 instrument" in the singular when only one is behind', async () => {
    renderData({ fake: withBars({ instrumentsBehind: 1 }) });

    expect(await screen.findByText('Newest bar is current — 1 instrument behind')).toBeInTheDocument();
  });

  it('says "1 trading day" in the singular', async () => {
    renderData({ fake: withBars({ stale: true, daysBehind: 1 }) });

    expect(await screen.findByText(/^Behind by 1 trading day · /)).toBeInTheDocument();
  });

  it('says plainly that no bars are stored yet', async () => {
    renderData({ fake: withBars({ lastBar: null, firstBar: null, totalBars: 0, instruments: 0, daysBehind: null }) });

    const tick = await lastTick();
    expect(within(tick).getByText('No bars stored yet')).toBeInTheDocument();
    expect(within(tick).getByTestId('HelpOutlineOutlinedIcon')).toBeInTheDocument();
  });

  it('keeps the next holiday out of sight, as a tooltip on a button the keyboard can reach', async () => {
    renderData({ fake: createDataFake() });
    const tick = await lastTick();

    expect(within(tick).queryByText(/Next holiday/)).not.toBeInTheDocument();
    const button = within(tick).getByRole('button', { name: 'Next holiday: Tue 20 Oct 2026 — Diwali Laxmi Pujan' });
    await userEvent.hover(button);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Next holiday: Tue 20 Oct 2026 — Diwali Laxmi Pujan');
  });

  it('has no holiday button when no holiday is ahead', async () => {
    renderData({
      fake: createDataFake({ calendar: { days: [buildCalendarDay({ date: '2026-09-01' })], proposals: [] } }),
    });

    expect(within(await lastTick()).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('Status ribbon: instruments, bars and the period', () => {
  it('shows the instruments with data, the total 1-minute bars (compact, the full number as its title) and the period', async () => {
    renderData();

    const region = await ready();
    expect(within(region).getByText('Instruments').nextElementSibling).toHaveTextContent('214');
    expect(within(region).queryByText(/behind/)).not.toBeInTheDocument();
    expect(within(region).getByText('1-minute bars').nextElementSibling).toHaveTextContent('4.1Cr');
    expect(within(region).getByTitle('4,12,34,567 in total')).toBeInTheDocument();
    expect(within(region).getByText('1 Apr 2024 – 12 Oct 2026')).toBeInTheDocument();
  });

  it('says how many instruments are behind (with a warning icon) and how many have no bars', async () => {
    renderData({
      fake: withBars({ lastBar: daysAgo(3), stale: true, daysBehind: 2, instrumentsBehind: 12, instrumentsEmpty: 3 }),
    });

    const region = await ready();
    const behind = within(region).getByText('12 behind');
    expect(within(behind.parentElement!).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(within(region).getByText('3 without bars')).toBeInTheDocument();
  });

  it('shows dashes when nothing is stored', async () => {
    renderData({ fake: withBars({ lastBar: null, firstBar: null, totalBars: 0, instruments: 0, daysBehind: null }) });

    const region = await ready();
    expect(within(region).getByText('1-minute bars').nextElementSibling).toHaveTextContent('—');
    expect(within(region).getByText('Covered').nextElementSibling).toHaveTextContent('—');
  });
});

describe('Status ribbon: the Data account', () => {
  it('shows the connected Data account without a reconnect link', async () => {
    renderData();

    const region = await ready();
    expect(within(region).getByText('Dhan · Main')).toBeInTheDocument();
    expect(within(region).getByText('Connected')).toBeInTheDocument();
    expect(within(region).queryByRole('link', { name: 'Reconnect' })).not.toBeInTheDocument();
  });

  it('offers Reconnect, with the reason, when the Dhan step cannot run, and opens the account', async () => {
    const reason = 'The Dhan sign-in has expired — reconnect the Data account.';
    const { router } = renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({
            account: { id: ids.main, name: 'Dhan · Main', status: 'expired' },
            canFetch: false,
            reason,
            reasonCode: 'data.dhan_not_connected',
          }),
        }),
      }),
    });

    const region = await ready();
    expect(within(region).getByText('Sign-in expired')).toBeInTheDocument();
    expect(within(region).getByText(reason)).toBeInTheDocument();
    const link = within(region).getByRole('link', { name: 'Reconnect' });
    expect(link).toHaveAttribute('href', `/accounts?account=${ids.main}`);

    await userEvent.click(link);
    expect(router.state.location.pathname).toBe('/accounts');
    expect(router.state.location.search).toBe(`?account=${ids.main}`);
  });

  it('points to Accounts when no account has the Data role', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({
            account: null,
            canFetch: false,
            reason: 'No account has the Data role.',
            reasonCode: 'data.dhan_not_connected',
          }),
        }),
      }),
    });

    const region = await ready();
    expect(within(region).getByText('No Data account')).toBeInTheDocument();
    expect(within(region).getByRole('link', { name: 'Open Accounts' })).toHaveAttribute('href', '/accounts');
    expect(within(region).queryByRole('link', { name: 'Reconnect' })).not.toBeInTheDocument();
  });
});

describe('Status ribbon: on a phone', () => {
  const ribbonItems = async () => {
    const region = await ready();
    return {
      region,
      tick: within(region).getByRole('region', { name: 'Last tick' }),
      instruments: within(region).getByText('Instruments').parentElement,
      bars: within(region).getByText('1-minute bars').parentElement,
      account: within(region).getByText('Data account').parentElement,
      auto: within(region).getByRole('button', { name: /^Auto/ }),
      more: within(region).getByRole('button', { name: /^More/ }),
    };
  };
  const display = (element: Element | null) => {
    const css = element ? appliedCss(element) : null;
    return css?.responsive.map(({ query, css: declarations }) => `${query} ${declarations}`) ?? [];
  };

  it('keeps the first three items in view and folds the rest below the small breakpoint', async () => {
    renderData();
    const items = await ribbonItems();

    // The first three are never hidden.
    for (const kept of [items.tick, items.instruments, items.bars])
      expect(display(kept).join(' ')).not.toContain('none');
    // The others sit in a wrapper that is hidden on extra-small screens and takes no box of its own from `sm`.
    for (const folded of [items.account?.parentElement, items.auto.parentElement]) {
      expect(display(folded ?? null).join(' ')).toMatch(
        /min-width:0px\) display: none.*min-width:600px\) display: contents/,
      );
    }
    // "More" is a phone-only button.
    expect(display(items.more).join(' ')).toMatch(
      /min-width:0px\) display: inline-flex.*min-width:600px\) display: none/,
    );
  });

  it('opens the folded items in a popover', async () => {
    renderData();
    const { more } = await ribbonItems();

    await userEvent.click(more);

    const popover = await screen.findByRole('dialog', { name: 'More status' });
    expect(within(popover).getByText('Data account')).toBeInTheDocument();
    expect(within(popover).getByText('Connected')).toBeInTheDocument();
    expect(within(popover).getByRole('button', { name: /^Auto 15:50/ })).toBeInTheDocument();
    expect(within(popover).getByText('Covered')).toBeInTheDocument();
  });

  it('says on the More button when a folded item needs attention', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({ canFetch: false, reason: 'Dhan is not connected.', reasonCode: 'x' }),
          eod: buildEodOverview({ lastRun: buildEodRun({ status: 'failed' }) }),
        }),
      }),
    });
    const { more } = await ribbonItems();

    expect(more).toHaveAccessibleName('More, needs attention');
  });

  it('says only More when nothing needs attention', async () => {
    renderData();

    expect((await ribbonItems()).more).toHaveAccessibleName('More');
  });
});
