import CloseIcon from '@mui/icons-material/Close';
import { Box, Button, IconButton, Stack, Typography, useMediaQuery } from '@mui/material';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { useEffect, useRef, type Ref } from 'react';
import { iconChip, motion, shape } from '@/theme';
import { IconChip } from './IconChip';
import type { ToastItem } from './toastQueue';
import { resolveToastTone, toastRole } from './toastTone';
import { useToastTimer } from './useToastTimer';

/** The exit animation's length (ms); the card is removed from the page once it is over. */
export const toastExitMs = 160;
const accentBarWidth = 5;

const enter = keyframes`
  from { opacity: 0; transform: translateX(24px); }
  to { opacity: 1; transform: none; }
`;
const leave = keyframes`
  from { opacity: 1; transform: none; }
  to { opacity: 0; transform: translateX(24px); }
`;
const drain = keyframes`
  from { transform: scaleX(1); }
  to { transform: scaleX(0); }
`;

export interface ToastProps {
  toast: ToastItem;
  /** The pointer or focus is on the stack: no toast counts down. */
  paused: boolean;
  /** The card element, for the stack to see where focus is. */
  ref?: Ref<HTMLDivElement>;
  /** Time is up, the close button or a button was pressed: the stack starts the exit (stable between renders). */
  onClose: (id: string) => void;
  /** The exit animation is over: take it out of the stack (stable between renders). */
  onExited: (id: string) => void;
}

/**
 * One toast: a rounded card with a coloured accent bar on the left and the severity's soft tint, an icon chip, an
 * optional bold title, the message, up to two text buttons in the severity colour, a close button and, for a timed
 * toast, a thin countdown bar. Under `prefers-reduced-motion` nothing moves (no slide, no countdown bar); the toast
 * still stays its full time. Its role is `alert` for errors and warnings, `status` otherwise.
 */
export function Toast({ toast, paused, ref, onClose, onExited }: ToastProps) {
  const theme = useTheme();
  const reduced = useMediaQuery('(prefers-reduced-motion: reduce)');
  const tone = resolveToastTone(toast.severity, theme.palette.mode);
  const undoRef = useRef<HTMLButtonElement | null>(null);
  const { id, closing, durationMs, revision } = toast;

  useToastTimer({
    durationMs: closing ? null : durationMs,
    paused,
    restartKey: revision,
    onExpire: () => {
      onClose(id);
    },
  });

  useEffect(() => {
    if (!closing) return undefined;
    const timer = setTimeout(
      () => {
        onExited(id);
      },
      reduced ? 0 : toastExitMs,
    );
    return () => {
      clearTimeout(timer);
    };
  }, [id, closing, reduced, onExited]);

  // The keyboard started the action behind this toast, so its button is where focus belongs: Tab would take long.
  useEffect(() => {
    if (toast.focusAction) undoRef.current?.focus();
  }, [toast.focusAction, revision]);

  const animation = reduced
    ? undefined
    : `${closing ? leave : enter} ${closing ? toastExitMs : theme.transitions.duration.shorter}ms ${motion.easing} both`;
  const hasTitle = toast.title !== undefined && toast.title !== '';

  return (
    <Box
      ref={ref}
      role={toastRole(toast.severity)}
      aria-atomic="true"
      aria-hidden={closing ? true : undefined}
      inert={closing}
      data-severity={toast.severity}
      sx={{
        position: 'relative',
        overflow: 'hidden',
        display: 'flex',
        alignItems: 'flex-start',
        gap: 1.5,
        width: '100%',
        boxSizing: 'border-box',
        padding: '12px 8px 12px 21px',
        borderRadius: `${shape.radius}px`,
        border: `1px solid ${alpha(tone.color, 0.3)}`,
        bgcolor: tone.soft,
        color: 'text.primary',
        boxShadow: 3,
        animation,
        '&::before': {
          content: '""',
          position: 'absolute',
          insetBlock: 0,
          left: 0,
          width: accentBarWidth,
          bgcolor: tone.color,
        },
      }}
    >
      {/* The chip is a tint of its hue: on the surface (as everywhere) its icon keeps 3:1, on the card's tint it would not. */}
      <Box
        component="span"
        sx={{
          flex: 'none',
          display: 'inline-flex',
          bgcolor: 'background.paper',
          borderRadius: `${iconChip.md.radius}px`,
        }}
      >
        <IconChip icon={tone.icon} hue={tone.hue} size="md" />
      </Box>
      <Stack sx={{ flex: 1, minWidth: 0, gap: 0.25, pt: 0.25 }}>
        {hasTitle && (
          <Typography variant="subtitle2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
            {toast.title}
          </Typography>
        )}
        <Typography
          variant="body2"
          sx={{ color: hasTitle ? 'text.secondary' : 'text.primary', overflowWrap: 'anywhere' }}
        >
          {toast.message}
        </Typography>
        {toast.actions.length > 0 && (
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap', mt: 0.5, ml: -1.25 }}>
            {toast.actions.map((action, index) => {
              const undo = toast.undo !== undefined && index === 0;
              return (
                <Stack key={action.label} direction="row" sx={{ gap: 1, alignItems: 'center' }}>
                  <Button
                    ref={undo ? undoRef : undefined}
                    size="small"
                    aria-keyshortcuts={undo ? 'Control+Z Meta+Z' : undefined}
                    onClick={() => {
                      action.onClick();
                      onClose(id);
                    }}
                    sx={{
                      color: tone.color,
                      '&:hover': { bgcolor: alpha(tone.color, 0.1) },
                    }}
                  >
                    {action.label}
                  </Button>
                  {/* The button's aria-keyshortcuts tells assistive technology; this tells everyone else. */}
                  {undo && (
                    <Typography aria-hidden component="span" variant="caption" color="text.secondary">
                      Ctrl/⌘+Z
                    </Typography>
                  )}
                </Stack>
              );
            })}
          </Stack>
        )}
      </Stack>
      <IconButton
        size="small"
        aria-label="Close notification"
        onClick={() => {
          onClose(id);
        }}
        sx={{ color: tone.color, flex: 'none' }}
      >
        <CloseIcon fontSize="small" />
      </IconButton>
      {durationMs !== null && !closing && !reduced && (
        <Box
          key={revision}
          aria-hidden
          data-countdown
          sx={{
            position: 'absolute',
            insetInline: 0,
            bottom: 0,
            height: 3,
            bgcolor: alpha(tone.color, 0.55),
            transformOrigin: 'left',
            animation: `${drain} ${durationMs}ms linear forwards`,
            animationPlayState: paused ? 'paused' : 'running',
          }}
        />
      )}
    </Box>
  );
}
