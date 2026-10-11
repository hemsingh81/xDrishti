import { Divider, Stack } from '@mui/material';
import type { AccountDetail } from '@/shared/api';
import { BrokerAppSection } from './BrokerAppSection';
import { BrokerConnectionSection } from './BrokerConnectionSection';
import { ConnectionForm } from './ConnectionForm';
import { RemoveAccount } from './RemoveAccount';

/** Broker connection (token and actions); Dhan app and secrets; client id, name, colour and notes; removal. */
export function ConnectionTab({ account, onRemoved }: { account: AccountDetail; onRemoved: () => void }) {
  return (
    <Stack spacing={3}>
      <BrokerConnectionSection account={account} />
      <Divider />
      <BrokerAppSection account={account} />
      <Divider />
      <ConnectionForm key={account.updatedAt} account={account} />
      <Divider />
      <RemoveAccount account={account} onRemoved={onRemoved} />
    </Stack>
  );
}
