import { Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { useReleaseHalt } from '../api/writesApi';
import { PasswordReasonDialog } from './PasswordReasonDialog';

/** Release of a halt: a fresh password and a reason; the server's refusal reasons are shown verbatim. */
export function ReleaseDialog({
  halt,
  open,
  onClose,
}: {
  halt: Schemas['ExecutionHaltView'];
  open: boolean;
  onClose: () => void;
}) {
  const release = useReleaseHalt();
  return (
    <PasswordReasonDialog
      open={open}
      title={`Release halt ${halt.id}`}
      confirmLabel="Release the halt"
      intro={
        <Typography variant="body2" sx={{ mb: 1 }}>
          Halt {halt.id}, level {halt.level}, since {formatIstDateTime(halt.at)} IST: {halt.reason}
        </Typography>
      }
      pending={release.isPending}
      error={release.error}
      onClose={() => {
        release.reset();
        onClose();
      }}
      onSubmit={(body) => {
        release.mutate({ id: halt.id, body }, { onSuccess: onClose });
      }}
    />
  );
}
