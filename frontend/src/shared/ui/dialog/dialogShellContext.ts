import { createContext, useContext } from 'react';

/** What the header and footer of an `AppDialog` need from the dialog, so callers do not repeat it. */
export interface DialogShell {
  /** Id of the title element — the dialog's accessible name (`aria-labelledby`). */
  titleId: string;
  onClose: () => void;
  /** While busy (saving) the dialog cannot be closed. */
  busy: boolean;
}

export const DialogShellContext = createContext<DialogShell | null>(null);

export function useDialogShell(): DialogShell {
  const shell = useContext(DialogShellContext);
  if (!shell) {
    throw new Error('AppDialogHeader must be used inside AppDialog.');
  }
  return shell;
}
