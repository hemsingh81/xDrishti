import { Stack, Typography } from '@mui/material';
import { formatIstDateTime } from '@/shared/lib/format';
import { Facts } from '@/shared/ui';
import type { Registration } from '../api/confidenceQueries';
import { registrationParts } from './registrationParts';

/** A stored registration, read-only. The texts are React text children: never parsed as markup. */
export function RegistrationView({ registration }: { registration: Registration }) {
  return (
    <Stack spacing={2}>
      <Typography>
        {`Registered by ${registration.actor} on ${formatIstDateTime(registration.createdAt)}. Frozen: it cannot be changed.`}
      </Typography>
      <Facts
        items={registrationParts.map((part) => ({
          label: part.label,
          value: <Typography sx={{ whiteSpace: 'pre-wrap' }}>{registration[part.key]}</Typography>,
        }))}
      />
      <Typography variant="caption" color="text.secondary">
        {`Text hash ${registration.textHash}`}
      </Typography>
    </Stack>
  );
}
