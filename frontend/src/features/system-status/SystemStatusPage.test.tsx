import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildSystemStatus } from '@/test/fixtures';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { SystemStatusPage } from './components/SystemStatusPage';

describe('SystemStatusPage', () => {
  it('shows a healthy stack with database versions and running services', async () => {
    renderWithProviders(<SystemStatusPage />);

    expect(await screen.findByText('PostgreSQL 18.6')).toBeInTheDocument();
    expect(screen.getByText(/TimescaleDB 2\.30\.2/)).toBeInTheDocument();
    expect(screen.getByText('Healthy')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'xd-worker' })).toBeInTheDocument();
    expect(screen.getByText('Running')).toBeInTheDocument();
  });

  it('flags a service that stopped reporting', async () => {
    const status = buildSystemStatus({ isHealthy: false });
    status.services = status.services.map((s) => ({ ...s, isAlive: false }));
    server.use(http.get('/api/system/status', () => HttpResponse.json(status)));

    renderWithProviders(<SystemStatusPage />);

    expect(await screen.findByText('Not reporting')).toBeInTheDocument();
    expect(screen.getByText('Unhealthy')).toBeInTheDocument();
  });

  it('shows an error when the API fails', async () => {
    server.use(
      http.get('/api/system/status', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })),
    );

    renderWithProviders(<SystemStatusPage />);

    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
  });
});
