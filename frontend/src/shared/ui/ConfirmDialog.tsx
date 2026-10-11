import { Button, DialogContentText } from '@mui/material';
import { useRef, type ReactNode } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from './dialog';

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  /** Destructive actions use the error colour (the label still says what happens — never colour alone). */
  destructive?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Modal "are you sure?" with Cancel / confirm buttons. Focus starts on Cancel, the safe choice. */
export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  return (
    <AppDialog open={open} onClose={onCancel} busy={busy} maxWidth="xs" initialFocusRef={cancelRef}>
      <AppDialogHeader>{title}</AppDialogHeader>
      <AppDialogBody>
        <DialogContentText component="div">{children}</DialogContentText>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onCancel} disabled={busy} ref={cancelRef}>
          Cancel
        </Button>
        <Button variant="contained" color={destructive ? 'error' : 'primary'} onClick={onConfirm} loading={busy}>
          {confirmLabel}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
