import { DialogContent } from '@mui/material';
import type { ReactNode, Ref } from 'react';

interface AppDialogBodyProps {
  children: ReactNode;
  /** The scrolling content element — e.g. to move focus to the first invalid field. */
  ref?: Ref<HTMLDivElement>;
}

/** Body of an `AppDialog`: padded content that scrolls while the header and footer stay visible. */
export function AppDialogBody({ children, ref }: AppDialogBodyProps) {
  return <DialogContent ref={ref}>{children}</DialogContent>;
}
