import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import HighlightOffIcon from '@mui/icons-material/HighlightOff';
import { Stack, Typography } from '@mui/material';
import type { SecretStatus } from '@/shared/api';
import { secretStatusText } from '../lib/secrets';

/** One line per broker secret: set (and when) or not set. The value itself is never known to the browser. */
export function SecretsStatus({ secrets }: { secrets: SecretStatus[] }) {
  if (secrets.length === 0) {
    return <Typography component="span">No secrets stored</Typography>;
  }
  return (
    <Stack component="ul" aria-label="Broker secrets" spacing={0.5} sx={{ m: 0, p: 0, listStyle: 'none' }}>
      {secrets.map((secret) => (
        <Stack component="li" key={secret.name} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          {secret.set ? (
            <CheckCircleIcon fontSize="small" color="success" aria-hidden />
          ) : (
            <HighlightOffIcon fontSize="small" color="warning" aria-hidden />
          )}
          <Typography component="span">{secretStatusText(secret)}</Typography>
        </Stack>
      ))}
    </Stack>
  );
}
