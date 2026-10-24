import { act, render, screen } from '@testing-library/react';
import { fakeTimers, pinClock } from '@/test/pinClock';
import { Countdown } from './components/Countdown';
import { formatCountdown } from './lib/planText';

const now = pinClock('2026-10-26T03:00:00Z');

describe('formatCountdown', () => {
  it('counts down and then says the cutoff passed', () => {
    expect(formatCountdown(now + 3_725_000, now)).toBe('1h 02m 05s left');
    expect(formatCountdown(now + 59_000, now)).toBe('0m 59s left');
    expect(formatCountdown(now, now)).toBe('Cutoff passed');
    expect(formatCountdown(now - 1000, now)).toBe('Cutoff passed');
  });
});

describe('Countdown', () => {
  it('ticks every second', () => {
    fakeTimers();
    render(<Countdown cutoff="2026-10-26T03:00:03Z" />);
    expect(screen.getByText('0m 03s left')).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(screen.getByText('0m 01s left')).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(screen.getByText('Cutoff passed')).toBeInTheDocument();
  });

  it('calls onElapsed once when the cutoff is crossed, not when it is already past', () => {
    fakeTimers();
    const onElapsed = vi.fn();
    const { unmount } = render(<Countdown cutoff="2026-10-26T03:00:02Z" onElapsed={onElapsed} />);
    expect(onElapsed).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    expect(onElapsed).toHaveBeenCalledTimes(1);
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    expect(onElapsed).toHaveBeenCalledTimes(1);
    unmount();
    const late = vi.fn();
    render(<Countdown cutoff="2026-10-26T02:00:00Z" onElapsed={late} />);
    expect(late).not.toHaveBeenCalled();
  });
});
