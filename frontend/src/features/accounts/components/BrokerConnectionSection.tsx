import { Box, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { AccountDetail } from '@/shared/api';
import { formatDateTime } from '@/shared/lib/format';
import { useConnectionState } from '../hooks/useConnectionState';
import { dhanApp } from '../lib/dhanApp';
import { ConnectionActions } from './ConnectionActions';
import { ConnectionStatus } from './ConnectionStatus';
import { Facts } from './Facts';

const when = (value: string | null) => (value ? formatDateTime(value) : 'never');

/** Status of the Dhan token, who it belongs to and its history, plus the actions to (re)connect or disconnect. */
export function BrokerConnectionSection({ account }: { account: AccountDetail }) {
  const headingId = useId();
  const { connection } = account;
  const { status } = useConnectionState(connection);

  return (
    <Box component="section" aria-labelledby={headingId}>
      <Stack spacing={2}>
        <Typography id={headingId} variant="h3">
          Broker connection
        </Typography>
        <Facts
          items={[
            { label: 'Status', value: <ConnectionStatus connection={connection} /> },
            { label: 'Signed in as', value: connection.brokerUserName ?? '—' },
            { label: 'Broker user id', value: connection.brokerUserId ?? '—' },
            { label: 'Last connected', value: when(connection.lastConnectedAt) },
            { label: 'Last error', value: connection.lastError ?? 'none' },
          ]}
        />
        <Typography variant="body2" color="text.secondary">
          Connecting opens {dhanApp.name}&apos;s own sign-in page (with its two-factor check) in this tab and brings you
          back here. The access token is stored encrypted and is never shown.
        </Typography>
        <ConnectionActions account={account} status={status} />
      </Stack>
    </Box>
  );
}
