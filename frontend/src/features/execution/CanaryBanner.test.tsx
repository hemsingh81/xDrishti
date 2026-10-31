import { render, screen } from '@testing-library/react';
import { CanaryBanner, StaleView } from './components/PageBanners';
import { pinClock } from '@/test/pinClock';

const nowMs = pinClock('2026-10-30T04:00:00Z');

describe('Canary banner', () => {
  it.each([
    [true, 1],
    [false, 0],
    [null, 0],
    [undefined, 0],
  ])('pending %s shows %s banner(s)', (pending, count) => {
    render(<CanaryBanner pending={pending} />);
    expect(screen.queryAllByText(/Canary pending: orders are capped at 1 share/)).toHaveLength(count);
  });
});

describe('Stale view', () => {
  it('says when the last read was and that the numbers may be old, only while the market is open and after 30 s', () => {
    const { rerender } = render(<StaleView updatedAt={nowMs - 31_000} marketOpen now={nowMs} />);
    expect(screen.getByText(/Last updated 09:29:29, the numbers may be old/)).toBeInTheDocument();
    rerender(<StaleView updatedAt={nowMs - 29_000} marketOpen now={nowMs} />);
    expect(screen.queryByText(/numbers may be old/)).not.toBeInTheDocument();
    rerender(<StaleView updatedAt={nowMs - 600_000} marketOpen={false} now={nowMs} />);
    expect(screen.queryByText(/numbers may be old/)).not.toBeInTheDocument();
  });
});
