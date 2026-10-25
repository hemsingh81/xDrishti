import { render, screen } from '@testing-library/react';
import { pinClock } from '@/test/pinClock';
import { LevelsRow } from './components/LevelsRow';

const base = {
  entryRef: 100,
  session: '2026-10-23',
  stop: 95,
  stopAnchor: 'level',
  target: 110,
  rewardRisk: 2,
  basis: 'indicative from the close of the session; not tomorrow’s price',
};

pinClock('2026-10-24T05:00:00Z');

describe('LevelsRow', () => {
  it('shows entry, stop with its anchor, target with RR and the pinned sentence', () => {
    render(<LevelsRow levels={base} />);
    expect(screen.getByText(/entry 100\.00 · stop 95\.00 \(level\) · target 110\.00 \(RR 2\)/)).toBeInTheDocument();
    expect(
      screen.getByText(/Indicative from the close of 2026-10-23, not tomorrow's price\. No order is placed\./),
    ).toBeInTheDocument();
  });

  it('mirrors a sell ticket and labels a close-anchored stop', () => {
    render(<LevelsRow levels={{ ...base, stop: 105, stopAnchor: 'close_of_d', target: 90 }} />);
    expect(screen.getByText(/stop 105\.00 \(from the close\) · target 90\.00/)).toBeInTheDocument();
  });

  it('says rules only when there are no levels', () => {
    render(<LevelsRow levels={null} />);
    expect(screen.getByText('Levels: rules only (see the stop and target text)')).toBeInTheDocument();
  });

  it('says rule only for a stop and a target with no number', () => {
    render(<LevelsRow levels={{ ...base, stop: null, stopAnchor: null, target: null, rewardRisk: null }} />);
    expect(screen.getByText(/stop: rule only · target: rule only/)).toBeInTheDocument();
  });

  it('renders markup in a server string as text, never as elements', () => {
    const { container } = render(<LevelsRow levels={{ ...base, session: '<b>x</b>' }} />);
    expect(screen.getByText(/read at the close of <b>x<\/b>/)).toBeInTheDocument();
    expect(container.querySelector('b')).toBeNull();
  });
});
