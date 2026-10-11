import { Chip, Stack, Typography } from '@mui/material';
import { formatDate } from '@/shared/lib/format';
import { keyExpiry } from '../lib/secrets';

/** "API key valid until 9 Oct 2027", with a warning chip close to expiry and an error chip once expired (words, not colour). */
export function ApiKeyExpiry({ expiresOn }: { expiresOn: string }) {
  const expiry = keyExpiry(expiresOn);
  const text =
    expiry.state === 'expired'
      ? `API key expired on ${formatDate(expiry.expiresOn)}`
      : `API key valid until ${formatDate(expiry.expiresOn)}`;
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
      <Typography component="span">{text}</Typography>
      {expiry.state === 'soon' && (
        <Chip
          size="small"
          color="warning"
          variant="outlined"
          label={`Expires in ${expiry.daysLeft} day${expiry.daysLeft === 1 ? '' : 's'} — generate a new key`}
        />
      )}
      {expiry.state === 'expired' && (
        <Chip size="small" color="error" variant="outlined" label="Expired — generate a new key" />
      )}
    </Stack>
  );
}
