import KeyIcon from '@mui/icons-material/KeyOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useId, useState } from 'react';
import type { AccountDetail } from '@/shared/api';
import { dhanApp } from '../lib/dhanApp';
import { ApiKeyExpiry } from './ApiKeyExpiry';
import { DhanSetupGuide } from './DhanSetupGuide';
import { Facts } from './Facts';
import { ReplaceSecretsDialog } from './ReplaceSecretsDialog';
import { SecretsStatus } from './SecretsStatus';

/** The Dhan app: masked client id, secret status with key expiry, "Replace secrets" and the setup guide. */
export function BrokerAppSection({ account }: { account: AccountDetail }) {
  const headingId = useId();
  const [replacing, setReplacing] = useState(false);

  return (
    <Box component="section" aria-labelledby={headingId}>
      <Stack spacing={2}>
        <Typography id={headingId} variant="h3">
          Broker app
        </Typography>
        <Facts
          items={[
            { label: 'Broker', value: dhanApp.name },
            { label: dhanApp.clientIdLabel, value: account.maskedClientId },
            { label: 'Secrets', value: <SecretsStatus secrets={account.secrets} /> },
            { label: 'API key expiry', value: <ApiKeyExpiry expiresOn={account.apiKeyExpiresOn} /> },
          ]}
        />
        <Typography variant="body2" color="text.secondary">
          Secrets are stored encrypted and are never shown; replace them when you generate new ones.
        </Typography>
        <Box>
          <Button
            variant="outlined"
            startIcon={<KeyIcon />}
            onClick={() => {
              setReplacing(true);
            }}
          >
            Replace secrets
          </Button>
        </Box>
        <DhanSetupGuide collapsible />
        {replacing && (
          <ReplaceSecretsDialog
            account={account}
            onClose={() => {
              setReplacing(false);
            }}
          />
        )}
      </Stack>
    </Box>
  );
}
