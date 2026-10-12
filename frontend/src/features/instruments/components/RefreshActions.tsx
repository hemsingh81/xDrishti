import RefreshIcon from '@mui/icons-material/Refresh';
import type { Ref } from 'react';
import { SplitButton } from '@/shared/ui/SplitButton';

interface RefreshActionsProps {
  /** A refresh is running or was just started. */
  busy: boolean;
  /** The status is not known yet, so starting would be a guess. */
  disabled?: boolean;
  onStart: (force: boolean) => void;
  /** The main button, so the page can move focus back to it. */
  buttonRef?: Ref<HTMLButtonElement>;
}

/** "Refresh master list" plus a menu with "Force full refresh" (see `SplitButton` for the busy behaviour). */
export function RefreshActions({ busy, disabled = false, onStart, buttonRef }: RefreshActionsProps) {
  return (
    <SplitButton
      label="Refresh master list"
      groupLabel="Refresh the master list"
      moreLabel="More refresh options"
      startIcon={<RefreshIcon />}
      busy={busy}
      disabled={disabled}
      buttonRef={buttonRef}
      onClick={() => {
        onStart(false);
      }}
      options={[
        {
          id: 'force',
          label: 'Force full refresh',
          description: "Re-import everything, even if Dhan's file has not changed",
          onSelect: () => {
            onStart(true);
          },
        },
      ]}
    />
  );
}
