import { Box, Dialog, useMediaQuery, useTheme, type Breakpoint } from '@mui/material';
import { useId, useMemo, type ReactNode, type RefObject, type SyntheticEvent } from 'react';
import { DialogShellContext } from './dialogShellContext';

export interface AppDialogProps {
  open: boolean;
  onClose: () => void;
  /** Saving: Escape, the backdrop and the close button do nothing. */
  busy?: boolean;
  maxWidth?: Breakpoint;
  /** Fill the screen on phones (below the `sm` breakpoint) — for long forms such as wizards. */
  fullScreenOnMobile?: boolean;
  /** Focused once the opening transition has finished (e.g. the first field, or Cancel in a confirmation). */
  initialFocusRef?: RefObject<HTMLElement | null>;
  /** When given, header, body and footer are wrapped in a form: Enter in a field or a submit button submits. */
  onSubmit?: (event: SyntheticEvent) => void;
  /** `AppDialogHeader`, `AppDialogBody`, `AppDialogFooter`. */
  children: ReactNode;
}

/**
 * Modal dialog shell: a header band (title + close button), a scrolling body and a footer band for the actions.
 * The bands' colours come from the theme (MuiDialogTitle / MuiDialogActions overrides).
 */
export function AppDialog({
  open,
  onClose,
  busy = false,
  maxWidth = 'sm',
  fullScreenOnMobile = false,
  initialFocusRef,
  onSubmit,
  children,
}: AppDialogProps) {
  const theme = useTheme();
  const phone = useMediaQuery(theme.breakpoints.down('sm'));
  const titleId = useId();
  const shell = useMemo(() => ({ titleId, onClose, busy }), [titleId, onClose, busy]);

  return (
    <Dialog
      open={open}
      onClose={busy ? undefined : onClose}
      fullScreen={fullScreenOnMobile && phone}
      maxWidth={maxWidth}
      fullWidth
      aria-labelledby={titleId}
      slotProps={{
        transition: {
          onEntered: () => {
            initialFocusRef?.current?.focus();
          },
        },
      }}
    >
      <DialogShellContext value={shell}>
        {onSubmit ? (
          // display: contents keeps header, body and footer as direct flex items of the paper, so the body scrolls.
          <Box component="form" noValidate onSubmit={onSubmit} sx={{ display: 'contents' }}>
            {children}
          </Box>
        ) : (
          children
        )}
      </DialogShellContext>
    </Dialog>
  );
}
