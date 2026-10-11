import type { BrokerConnection } from '@/shared/api';
import { connectionState, needsClock, type ConnectionState } from '../lib/connection';
import { useNow } from './useNow';

/**
 * The connection's state against the clock: re-checked every ~30 s, and only while a connected token can run out
 * (so the countdown moves and an expired token flips to "Expired" without a refetch).
 */
export function useConnectionState(connection: BrokerConnection): ConnectionState {
  const now = useNow(needsClock(connection), connection.expiresAt);
  return connectionState(connection, now);
}
