import CloseIcon from '@mui/icons-material/Close';
import { Box, DialogTitle, IconButton, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';
import { useDialogShell } from './dialogShellContext';

interface AppDialogHeaderProps {
  /** The title — also the dialog's accessible name. */
  children: ReactNode;
  subtitle?: ReactNode;
}

/** Header band of an `AppDialog`: title, optional subtitle and a close (×) button. */
export function AppDialogHeader({ children, subtitle }: AppDialogHeaderProps) {
  const { titleId, onClose, busy } = useDialogShell();
  // The band gets its own id: MUI would otherwise give it the title id, and the close button's label would then
  // become part of the dialog's name.
  const bandId = useId();
  return (
    <DialogTitle id={bandId} component="div" sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography id={titleId} component="h2" variant="h2">
          {children}
        </Typography>
        {subtitle && (
          <Typography variant="body2" color="text.primary" sx={{ mt: 0.25 }}>
            {subtitle}
          </Typography>
        )}
      </Box>
      <IconButton aria-label="Close" onClick={onClose} disabled={busy} size="small" edge="end">
        <CloseIcon fontSize="small" />
      </IconButton>
    </DialogTitle>
  );
}
