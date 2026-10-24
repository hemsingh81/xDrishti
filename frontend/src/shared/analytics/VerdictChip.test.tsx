import { screen } from '@testing-library/react';
import { renderKit, usesGoodColour } from '@/test/analyticsKit';
import { VerdictChip, type Verdict } from './VerdictChip';

describe('VerdictChip', () => {
  it.each([
    ['met', 'MET'],
    ['not_met', 'NOT MET'],
    ['not_evaluable', 'NOT EVALUABLE'],
  ] as const)('prints %s as the words %s with a decorative icon', (verdict, text) => {
    const { container } = renderKit(<VerdictChip verdict={verdict} />);
    expect(screen.getByText(text)).toBeInTheDocument();
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('uses the good colour for MET alone, never for NOT MET or NOT EVALUABLE', () => {
    const colour = (verdict: Verdict) => {
      const { container, unmount } = renderKit(<VerdictChip verdict={verdict} />);
      const good = usesGoodColour(container);
      unmount();
      return good;
    };
    expect(colour('met')).toBe(true);
    expect(colour('not_met')).toBe(false);
    expect(colour('not_evaluable')).toBe(false);
  });

  it('uses the good colour for MET alone in dark mode too', () => {
    const colour = (verdict: Verdict) => {
      const { container, unmount } = renderKit(<VerdictChip verdict={verdict} />, 'dark');
      const good = usesGoodColour(container, 'dark');
      unmount();
      return good;
    };
    expect(colour('met')).toBe(true);
    expect(colour('not_met')).toBe(false);
    expect(colour('not_evaluable')).toBe(false);
  });

  it('does not read MET inside NOT MET or NOT EVALUABLE', () => {
    renderKit(<VerdictChip verdict="not_evaluable" />);
    expect(screen.queryByText('MET')).not.toBeInTheDocument();
    expect(screen.queryByText('NOT MET')).not.toBeInTheDocument();
  });
});
