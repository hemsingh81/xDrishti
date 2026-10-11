import { DialogActions } from '@mui/material';
import type { ReactNode } from 'react';

/** Footer band of an `AppDialog`: the actions, right-aligned. */
export function AppDialogFooter({ children }: { children: ReactNode }) {
  return <DialogActions>{children}</DialogActions>;
}
