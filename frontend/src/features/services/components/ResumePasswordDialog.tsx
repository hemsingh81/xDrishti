import { Typography } from '@mui/material';
import { Suspense } from 'react';
import { PasswordReasonDialog } from '@/features/execution';
import type { ServiceView } from '@/shared/api';

interface Props {
  service: ServiceView | null;
  pending: boolean;
  error: unknown;
  onSubmit: (service: ServiceView, password: string) => void;
  onClose: () => void;
}

/** The fresh password a service with `resumeNeedsPassword` needs to be resumed (LC-01): no reason is asked. The server's refusal is shown in the dialog. */
export function ResumePasswordDialog({ service, pending, error, onSubmit, onClose }: Props) {
  if (!service) return null;
  return (
    <Suspense fallback={null}>
      <PasswordReasonDialog
        open
        askReason={false}
        title={`Resume ${service.name}`}
        confirmLabel="Resume"
        pending={pending}
        error={error}
        intro={
          <Typography variant="body2" sx={{ mb: 1 }}>
            This service writes a decision input with no one in between, so resuming it needs your password. It re-dates
            the same trades until a hold-out block is released and places no order.
          </Typography>
        }
        onSubmit={({ password }) => {
          onSubmit(service, password);
        }}
        onClose={onClose}
      />
    </Suspense>
  );
}
