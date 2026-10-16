import { Box } from '@mui/material';
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FocusEvent as ReactFocusEvent,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { Toast } from './Toast';
import { ToastContext, type ToastAction, type ToastApi, type ToastOptions } from './toastContext';
import { buildToast, closeToast, openToasts, pushToast, type ToastItem } from './toastQueue';
import { useAudibleAboveModals } from './useAudibleAboveModals';
import { useInputModality } from './useInputModality';

const isAction = (value: ToastAction | ToastOptions): value is ToastAction => 'label' in value;

/** Typing in these keeps its own Ctrl/Cmd+Z (undo of the text). */
const keepsOwnUndo = (target: EventTarget | null): boolean =>
  target instanceof HTMLElement &&
  (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));

/** Listens to key presses on the document while `active`. */
function useDocumentKeyDown(active: boolean, onKey: (event: KeyboardEvent) => void) {
  useEffect(() => {
    if (!active) return undefined;
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, [active, onKey]);
}

/**
 * Owns the stack of toasts; descendants call `useToast().notify(...)`. Up to three show at once, bottom right on wide
 * screens and the full width above the bottom edge on phones; the newest is at the bottom. A fourth makes the oldest
 * leave (a timed toast before a persistent one). Each toast is announced once, by its own role (see `Toast`).
 *
 * A toast with an undo button is reachable without a mouse in two ways: when the action behind it was started with the
 * keyboard, focus moves to the button and returns to where it was when the toast closes; and Ctrl/Cmd+Z runs it for as
 * long as the toast is open (the newest one that has an undo; not while typing in a field, which keeps its own undo).
 * Hovering (a mouse pointer; a touch does not count) or focusing any toast pauses every countdown, and Escape closes the
 * newest toast unless something else handled that Escape. The stack is in its own portal on the body and stays announced
 * above an open dialog (see `useAudibleAboveModals`).
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const modality = useInputModality();
  const returnTo = useRef<HTMLElement | null>(null);
  const stackRef = useRef<HTMLDivElement | null>(null);
  const cards = useRef(new Map<string, HTMLElement>());
  const counter = useRef(0);
  useAudibleAboveModals(stackRef);

  // A stack that is empty has nothing under the pointer or focus any more (no leave/blur event comes from a removed node).
  if (toasts.length === 0 && (hovered || focused)) {
    setHovered(false);
    setFocused(false);
  }

  /** Whether focus is inside an open toast other than `except` (the one that is leaving). */
  const toastHoldsFocus = useCallback((except?: string) => {
    for (const [id, card] of cards.current) {
      if (id !== except && card.contains(document.activeElement)) return true;
    }
    return false;
  }, []);

  /**
   * Gives focus back to where it was before a toast took it, if focus is still in that toast. The remembered place is
   * dropped as soon as no other toast holds focus: it was only meaningful while a toast had it (a toast that timed out with
   * focus elsewhere must not send the next one's focus to a stale place).
   */
  const leaveToast = useCallback(
    (id: string) => {
      const target = returnTo.current;
      if (target !== null && cards.current.get(id)?.contains(document.activeElement)) {
        returnTo.current = null;
        if (target.isConnected) target.focus();
      }
      if (!toastHoldsFocus(id)) returnTo.current = null;
    },
    [toastHoldsFocus],
  );

  const close = useCallback(
    (id: string) => {
      leaveToast(id);
      setToasts((current) => closeToast(current, id));
    },
    [leaveToast],
  );

  const exited = useCallback(
    (id: string) => {
      setToasts((current) => current.filter((toast) => !(toast.id === id && toast.closing)));
      cards.current.delete(id);
      if (!toastHoldsFocus()) returnTo.current = null;
      // Focus can have been in the card that went away.
      setFocused(stackRef.current?.contains(document.activeElement) ?? false);
    },
    [toastHoldsFocus],
  );

  const api = useMemo<ToastApi>(
    () => ({
      notify: (message, severity = 'success', actionOrOptions) => {
        const options: ToastOptions =
          actionOrOptions === undefined
            ? {}
            : isAction(actionOrOptions)
              ? { action: actionOrOptions }
              : actionOrOptions;
        const focusAction = options.action !== undefined && modality.current === 'keyboard';
        if (focusAction) {
          // A second toast keeps the first place to return to while a toast holds focus (the place before any toast took
          // it); with no toast holding focus, the place to return to is where focus is now.
          if (!toastHoldsFocus()) {
            const active = document.activeElement;
            returnTo.current = active instanceof HTMLElement && active !== document.body ? active : null;
          }
        }
        counter.current += 1;
        const next = buildToast(
          options.id ?? `toast-${counter.current}`,
          counter.current,
          message,
          severity,
          options,
          focusAction,
        );
        setToasts((current) => pushToast(current, next));
      },
      dismiss: close,
    }),
    [modality, close, toastHoldsFocus],
  );

  const open = openToasts(toasts);
  const newestId = open.at(-1)?.id;
  const undoable = open.findLast((toast) => toast.undo !== undefined);
  const undo = undoable?.undo;
  const undoId = undoable?.id;

  const onUndoKey = useCallback(
    (event: KeyboardEvent) => {
      const pressed =
        (event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'z';
      if (!pressed || event.defaultPrevented || keepsOwnUndo(event.target) || !undo || !undoId) return;
      event.preventDefault();
      undo.onClick();
      close(undoId);
    },
    [undo, undoId, close],
  );
  useDocumentKeyDown(undo !== undefined, onUndoKey);

  const onEscape = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && newestId) close(newestId);
    },
    [newestId, close],
  );
  useDocumentKeyDown(newestId !== undefined, onEscape);

  const onBlur = (event: ReactFocusEvent<HTMLDivElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget)) setFocused(false);
  };

  const paused = hovered || focused;
  return (
    <ToastContext value={api}>
      {children}
      {/* In its own portal on the body, so a dialog's own portal and the app root are not its parents (and it floats above both). */}
      {createPortal(
        <Box
          ref={stackRef}
          component="section"
          // A labelled region while there is something in it (to find the notifications by); nothing to announce otherwise.
          aria-label={toasts.length > 0 ? 'Notifications' : undefined}
          // A mouse hovers; a finger does not (a touch fires enter and no leave until the next touch, which would pause it for good).
          onPointerEnter={(event) => {
            if (event.pointerType === 'mouse') setHovered(true);
          }}
          onPointerLeave={(event) => {
            if (event.pointerType === 'mouse') setHovered(false);
          }}
          onFocus={() => {
            setFocused(true);
          }}
          onBlur={onBlur}
          sx={(theme) => ({
            position: 'fixed',
            zIndex: theme.zIndex.snackbar,
            display: 'flex',
            flexDirection: 'column',
            gap: 1,
            // Phones: the full width above the bottom edge (and the home indicator); wide screens: a column at the corner.
            left: 12,
            right: 12,
            bottom: 'calc(env(safe-area-inset-bottom, 0px) + 12px)',
            pointerEvents: 'none',
            '& > *': { pointerEvents: 'auto' },
            [theme.breakpoints.up('md')]: { left: 'auto', right: 24, bottom: 24, width: 400 },
          })}
        >
          {toasts.map((toast) => (
            <Toast
              key={toast.id}
              ref={(element: HTMLDivElement | null) => {
                if (element) cards.current.set(toast.id, element);
              }}
              toast={toast}
              paused={paused}
              onClose={close}
              onExited={exited}
            />
          ))}
        </Box>,
        document.body,
      )}
    </ToastContext>
  );
}
