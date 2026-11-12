import { screen } from '@testing-library/react';
import { useQuery } from '@tanstack/react-query';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { reservedHeights } from '@/test/reservedHeight';
import { server } from '@/test/server';
import { QueryPanel } from './Panel';

function Probe() {
  const query = useQuery({
    queryKey: ['probe'],
    queryFn: async () => {
      const response = await fetch('/api/probe');
      if (!response.ok) throw new Error('probe failed');
      return (await response.json()) as { text: string };
    },
    retry: false,
  });
  return (
    <QueryPanel title="Probe" query={query} reserve={{ xs: 300, md: 300 }}>
      {(data) => <p>{data.text}</p>}
    </QueryPanel>
  );
}

const reserved = () => reservedHeights(screen.getByRole('region', { name: 'Probe' }));

describe('QueryPanel reserved height', () => {
  it('holds the reserved height while pending and releases it once the data is there', async () => {
    server.use(http.get('/api/probe', () => HttpResponse.json({ text: 'Settled' })));
    renderWithProviders(<Probe />);
    expect(reserved()).toContain(300);
    expect(await screen.findByText('Settled')).toBeInTheDocument();
    expect(reserved()).toEqual([]);
  });

  it('releases it on an error', async () => {
    server.use(http.get('/api/probe', () => HttpResponse.json({ title: 'x' }, { status: 500 })));
    renderWithProviders(<Probe />);
    expect(reserved()).toContain(300);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(reserved()).toEqual([]);
  });
});
