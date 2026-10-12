import { Alert, Typography } from '@mui/material';
import { phaseReason } from '../lib/errors';

interface PhaseNoticeProps {
  status: 'failed' | 'skipped';
  message: string | null;
  code: string | null;
}

/** Why a step failed or was skipped, in words, with the code for the record. */
export function PhaseNotice({ status, message, code }: PhaseNoticeProps) {
  const failed = status === 'failed';
  return (
    <Alert severity={failed ? 'error' : 'info'} sx={{ py: 0 }}>
      <strong>{failed ? 'Failed: ' : 'Skipped: '}</strong>
      {phaseReason(message, code)}
      {code && (
        <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          Code {code}
        </Typography>
      )}
    </Alert>
  );
}
