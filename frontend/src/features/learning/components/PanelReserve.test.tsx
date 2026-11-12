import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { reservedHeights } from '@/test/reservedHeight';
import { Panel } from './Panel';

const reserved = () => reservedHeights(screen.getByRole('region', { name: 'Probe' }));

describe('learning Panel reserve', () => {
  it('applies the reserve only when one is given', () => {
    const { rerender } = renderWithProviders(
      <Panel title="Probe" reserve={{ xs: 250, md: 250 }}>
        <p>x</p>
      </Panel>,
    );
    expect(reserved()).toContain(250);
    rerender(
      <Panel title="Probe">
        <p>x</p>
      </Panel>,
    );
    expect(reserved()).toEqual([]);
  });
});
