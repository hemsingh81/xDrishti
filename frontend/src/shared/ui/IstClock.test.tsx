import { act, fireEvent, render, screen } from '@testing-library/react';
import { stubPhoneWidth, stubReducedMotion } from '@/test/matchMedia';
import { AppThemeProvider } from '@/theme';
import { IstClock } from './IstClock';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-10-13T10:00:00.100Z'));
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

const renderClock = () =>
  render(
    <AppThemeProvider>
      <IstClock />
    </AppThemeProvider>,
  );

/** The digit columns, left to right, as "HHmmss" (they are decorative: hidden from assistive technology). */
const digits = (container: HTMLElement) =>
  Array.from(container.querySelectorAll<HTMLElement>('[data-value]'))
    .map((d) => d.dataset.value)
    .join('');
const rolling = (container: HTMLElement) =>
  Array.from(container.querySelectorAll<HTMLElement>('[data-value]'))
    .filter((d) => d.dataset.rolling === 'true')
    .map((d) => d.dataset.value);
const tick = (ms = 1000) => {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
};

describe('IST clock', () => {
  it('shows India time with an IST label, whatever the time zone of the browser', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles');
    // The zone really is not India's: the local hour is 03, India's is 15.
    expect(new Date().getHours()).toBe(3);

    const { container } = renderClock();

    expect(digits(container)).toBe('153000');
    expect(screen.getByText('IST')).toBeInTheDocument();
    expect(screen.getByRole('timer', { name: /^15:30 India Standard Time, Tuesday, 13 October 2026$/ })).toBeVisible();
  });

  it('counts on every second and rolls only the digit that changed', () => {
    const { container } = renderClock();
    expect(rolling(container)).toEqual([]);

    tick();

    expect(digits(container)).toBe('153001');
    expect(rolling(container)).toEqual(['1']);
    // The old digit sits above the new one in the column that rolls (the roll is a CSS animation).
    const column = container.querySelector<HTMLElement>('[data-rolling="true"]');
    expect(column).toHaveTextContent('01');
    // When the roll has ended the column shows the new digit only.
    // jsdom reports the vendor-prefixed event name for animation ends, which is what React listens for there.
    fireEvent(column!.firstElementChild!, new Event('webkitAnimationEnd', { bubbles: true }));
    expect(column).toHaveTextContent(/^1$/);
    expect(rolling(container)).toEqual([]);
  });

  it('rolls the minute and the hour together when they change', () => {
    vi.setSystemTime(new Date('2026-10-13T10:29:59.100Z'));
    const { container } = renderClock();
    expect(digits(container)).toBe('155959');

    tick();

    expect(digits(container)).toBe('160000');
    expect(rolling(container)).toEqual(['6', '0', '0', '0', '0']);
  });

  it('never announces the seconds: the accessible label changes at most once a minute', () => {
    renderClock();
    const timer = screen.getByRole('timer');
    expect(timer).toHaveAttribute('aria-live', 'off');
    const label = timer.getAttribute('aria-label');

    tick(30_000);
    expect(timer.getAttribute('aria-label')).toBe(label);
    tick(30_000);
    expect(timer.getAttribute('aria-label')).toMatch(/^15:31 India Standard Time/);
    // The rolling digits are decorative.
    expect(timer.querySelector('[data-value]')).toHaveAttribute('aria-hidden', 'true');
  });

  it('does not roll with reduced motion: the digits just change', () => {
    stubReducedMotion();
    const { container } = renderClock();

    tick();

    expect(digits(container)).toBe('153001');
    expect(rolling(container)).toEqual([]);
  });

  it('shows hours and minutes only on a phone, and ticks once a minute', () => {
    stubPhoneWidth();
    const { container } = renderClock();

    expect(digits(container)).toBe('1530');
    expect(screen.getByText('IST')).toBeInTheDocument();
    tick(20_000);
    expect(digits(container)).toBe('1530');
    tick(40_000);
    expect(digits(container)).toBe('1531');
  });

  it('keeps its timer to a single one, and clears it when it leaves', () => {
    const { unmount } = renderClock();
    expect(vi.getTimerCount()).toBe(1);

    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});
