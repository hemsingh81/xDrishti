import { Alert, Button, Snackbar, Stack, Typography } from '@mui/material';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { IconHueName } from '@/theme';
import { IconChip } from './IconChip';
import { ToastContext, type ToastAction, type ToastApi, type ToastSeverity } from './toastContext';
import { useInputModality } from './useInputModality';

interface Toast {
  id: number;
  message: string;
  severity: ToastSeverity;
  action?: ToastAction;
  /** The action was started with the keyboard, so focus moves to the button: it would take many Tabs to reach. */
  focusAction: boolean;
}

const plainDuration = 5000;
// A toast with a button gives time to read it and decide.
const actionDuration = 10_000;

const statusChip = {
  success: { icon: CheckCircleIcon, hue: 'green' },
  info: { icon: InfoIcon, hue: 'indigo' },
  warning: { icon: WarningIcon, hue: 'amber' },
  error: { icon: ErrorIcon, hue: 'rose' },
} as const satisfies Record<ToastSeverity, { icon: typeof InfoIcon; hue: IconHueName }>;

/** Typing in these keeps its own Ctrl/Cmd+Z (undo of the text). */
const keepsOwnUndo = (target: EventTarget | null): boolean =>
  target instanceof HTMLElement &&
  (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));

/**
 * Owns one snackbar; descendants call `useToast().notify(...)`. Announced to screen readers (role "alert").
 *
 * A toast with a button (Undo) is reachable without a mouse in two ways: when the action behind it was started with the
 * keyboard, focus moves to the button and returns to where it was when the toast closes; and Ctrl/Cmd+Z runs it for as
 * long as the toast is open (not while typing in a field, which keeps its own undo). The toast stays while it has focus
 * or the pointer is over it (the snackbar pauses its timer), and Escape closes it (the snackbar does that).
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<Toast | null>(null);
  const modality = useInputModality();
  const returnTo = useRef<HTMLElement | null>(null);
  const alertRef = useRef<HTMLDivElement | null>(null);
  const actionRef = useRef<HTMLButtonElement | null>(null);

  /** Gives focus back to where it was before the toast took it, if it is still in the toast. */
  const leaveToast = useCallback(() => {
    const target = returnTo.current;
    returnTo.current = null;
    if (target?.isConnected && alertRef.current?.contains(document.activeElement)) target.focus();
  }, []);

  const close = useCallback(() => {
    leaveToast();
    setToast(null);
  }, [leaveToast]);

  const api = useMemo<ToastApi>(
    () => ({
      notify: (message, severity = 'success', action) => {
        const focusAction = action !== undefined && modality.current === 'keyboard';
        if (focusAction) {
          // A second toast keeps the first place to return to: the one before any toast took focus.
          const active = document.activeElement;
          returnTo.current ??= active instanceof HTMLElement && active !== document.body ? active : null;
        } else {
          leaveToast();
        }
        setToast((previous) => ({ id: (previous?.id ?? 0) + 1, message, severity, action, focusAction }));
      },
    }),
    [modality, leaveToast],
  );

  useEffect(() => {
    if (toast?.focusAction) actionRef.current?.focus();
  }, [toast?.id, toast?.focusAction]);

  const action = toast?.action;
  useEffect(() => {
    if (!action) return undefined;
    const onKeyDown = (event: KeyboardEvent) => {
      const undo =
        (event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'z';
      if (!undo || event.defaultPrevented || keepsOwnUndo(event.target)) return;
      event.preventDefault();
      action.onClick();
      close();
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [action, close]);

  const chip = statusChip[toast?.severity ?? 'success'];
  return (
    <ToastContext value={api}>
      {children}
      <Snackbar
        key={toast?.id}
        open={toast !== null}
        autoHideDuration={toast?.action ? actionDuration : plainDuration}
        onClose={(_event, reason) => {
          if (reason !== 'clickaway') close();
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          ref={alertRef}
          severity={toast?.severity ?? 'success'}
          variant="outlined"
          icon={<IconChip icon={chip.icon} hue={chip.hue} />}
          sx={{ bgcolor: 'background.paper', color: 'text.primary', alignItems: 'center', boxShadow: 3 }}
          action={
            toast?.action && (
              <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
                <Button
                  ref={actionRef}
                  color="inherit"
                  size="small"
                  aria-keyshortcuts="Control+Z Meta+Z"
                  onClick={() => {
                    toast.action?.onClick();
                    close();
                  }}
                >
                  {toast.action.label}
                </Button>
                {/* The button's aria-keyshortcuts tells assistive technology; this tells everyone else. */}
                <Typography aria-hidden component="span" variant="caption" color="text.secondary">
                  Ctrl/⌘+Z
                </Typography>
              </Stack>
            )
          }
          onClose={toast?.action ? undefined : close}
        >
          {toast?.message}
        </Alert>
      </Snackbar>
    </ToastContext>
  );
}
