import { act, render, screen } from '@testing-library/react';
import type { BrokerConnection } from '@/shared/api';
import { buildConnected, buildConnection } from '@/test/fixtures';
import { AppThemeProvider } from '@/theme';
import { ConnectionStatus } from './components/ConnectionStatus';

// 01:40 IST on 11 Oct; the token runs until 03:30 IST the same morning (22:00 UTC the day before).
const now = new Date('2026-10-10T20:10:00Z');
const soonExpiry = '2026-10-10T22:00:00Z';

function renderStatus(connection: BrokerConnection) {
  return render(
    <AppThemeProvider>
      <ConnectionStatus connection={connection} />
    </AppThemeProvider>,
  );
}

const advance = (minutes: number) => {
  act(() => {
    vi.advanceTimersByTime(minutes * 60_000);
  });
};

describe('ConnectionStatus', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('counts down in the last two hours and turns to Expired when the time is up', () => {
    renderStatus(buildConnected({ expiresAt: soonExpiry }));

    expect(screen.getByText('Connected')).toBeInTheDocument();
    expect(screen.getByText('valid until 03:30, 11 Oct')).toBeInTheDocument();
    expect(screen.getByText('expires in 1 h 50 min')).toBeInTheDocument();

    advance(30);
    expect(screen.getByText('expires in 1 h 20 min')).toBeInTheDocument();

    advance(79);
    expect(screen.getByText('expires in 1 min')).toBeInTheDocument();

    advance(1);
    expect(screen.getByText('Expired')).toBeInTheDocument();
    expect(screen.getByText('expired at 03:30, 11 Oct')).toBeInTheDocument();
    expect(screen.queryByText(/^expires in/)).not.toBeInTheDocument();
  });

  it('shows only the expiry while more than two hours are left', () => {
    renderStatus(buildConnected({ expiresAt: '2026-10-11T22:00:00Z' }));

    expect(screen.getByText('valid until 03:30, 12 Oct')).toBeInTheDocument();
    expect(screen.queryByText(/^expires in/)).not.toBeInTheDocument();

    advance(24 * 60);
    expect(screen.getByText('expires in 1 h 50 min')).toBeInTheDocument();
  });

  it.each([
    [buildConnection(), 'Not connected'],
    [buildConnected({ status: 'expired', expiresAt: '2026-10-09T22:00:00Z' }), 'Expired'],
  ])('names the status in words (%#)', (connection, label) => {
    renderStatus(connection);

    expect(screen.getByText(label)).toBeInTheDocument();
    expect(screen.queryByText(/^valid until/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^expires in/)).not.toBeInTheDocument();
  });
});
