import { Box } from '@mui/material';
import { connectionStatusLabel, toStatusKey, type ConnectionStatusKey } from '../lib/connection';

const dotColors: Record<ConnectionStatusKey, string> = {
  connected: 'success.main',
  expired: 'warning.main',
  not_connected: 'text.disabled',
};

/** Token health as a dot plus words (the words may be visually hidden in tight rows, e.g. the accounts list). */
export function TokenStatus({ status, showLabel = true }: { status: string; showLabel?: boolean }) {
  const label = connectionStatusLabel(status);
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75 }}>
      <Box
        component="span"
        aria-hidden
        sx={{ width: 9, height: 9, borderRadius: '50%', bgcolor: dotColors[toStatusKey(status)], flex: 'none' }}
      />
      <Box
        component="span"
        sx={
          showLabel
            ? undefined
            : {
                position: 'absolute',
                width: 1,
                height: 1,
                overflow: 'hidden',
                clip: 'rect(0 0 0 0)',
                whiteSpace: 'nowrap',
              }
        }
      >
        {showLabel ? label : `Token: ${label}`}
      </Box>
    </Box>
  );
}
