import { Alert, Stack } from '@mui/material';
import type { ImportWarning } from '@/shared/api';
import { informationalWarnings, warningText } from '../../lib/reportImport';

/** The notices about a file, in plain words (the colour is never the only sign: every one is a sentence). */
export function FileWarnings({ warnings, accountName }: { warnings: readonly ImportWarning[]; accountName: string }) {
  if (warnings.length === 0) return null;
  return (
    <Stack component="ul" aria-label="Notices about this file" spacing={0.75} sx={{ m: 0, p: 0, listStyle: 'none' }}>
      {warnings.map((warning) => (
        <li key={warning.code}>
          <Alert severity={informationalWarnings.includes(warning.code) ? 'info' : 'warning'}>
            {warningText(warning, accountName)}
          </Alert>
        </li>
      ))}
    </Stack>
  );
}
