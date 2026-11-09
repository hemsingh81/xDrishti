import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useId, useState, type ReactNode, type SyntheticEvent } from 'react';
import { problemText } from '../api/confirmApi';

interface Props {
  open: boolean;
  title: string;
  confirmLabel: string;
  intro?: ReactNode;
  /** False when the act takes no reason (the reason is then sent empty and not asked). */
  askReason?: boolean;
  /** Starts the reason field with this text (a caller that remounts the dialog after a refusal keeps what was typed). */
  initialReason?: string;
  /** Longest reason the server takes; shown beside the field and enforced in it. */
  maxReasonLength?: number;
  pending: boolean;
  error: unknown;
  /** Called with the typed password and reason; the dialog stays open until the caller closes it. */
  onSubmit: (v: { password: string; reason: string }) => void;
  onClose: () => void;
}

/** The password and the reason, asked for one act. The server's refusal is shown as it came. */
export function PasswordReasonDialog({
  open,
  title,
  confirmLabel,
  intro,
  askReason = true,
  initialReason = '',
  maxReasonLength,
  pending,
  error,
  onSubmit,
  onClose,
}: Props) {
  const id = useId();
  const [password, setPassword] = useState('');
  const [reason, setReason] = useState(initialReason);
  // The typed secrets do not outlive the dialog (adjusting state while rendering, not in an effect).
  if (!open && (password !== '' || reason !== '')) {
    setPassword('');
    setReason('');
  }
  const submit = (e: SyntheticEvent) => {
    e.preventDefault();
    if (password === '' || (askReason && reason.trim() === '') || pending) return;
    onSubmit({ password, reason: reason.trim() });
  };
  return (
    <Dialog open={open} onClose={onClose} aria-labelledby={`${id}-title`} fullWidth maxWidth="sm">
      <form onSubmit={submit}>
        <DialogTitle id={`${id}-title`}>{title}</DialogTitle>
        <DialogContent>
          {intro}
          {askReason && (
            <TextField
              label="Reason"
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
              }}
              margin="dense"
              fullWidth
              required
              multiline
              minRows={2}
              {...(maxReasonLength === undefined
                ? {}
                : {
                    slotProps: { htmlInput: { maxLength: maxReasonLength } },
                    helperText: `${String(reason.length)} of ${String(maxReasonLength)} characters`,
                  })}
            />
          )}
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => {
              setPassword(e.target.value);
            }}
            margin="dense"
            fullWidth
            required
          />
          {Boolean(error) && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {problemText(error)}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button
            type="submit"
            variant="contained"
            disabled={pending || password === '' || (askReason && reason.trim() === '')}
          >
            {confirmLabel}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
