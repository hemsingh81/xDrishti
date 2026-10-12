import SyncIcon from '@mui/icons-material/Sync';
import type { StartDataJobInput } from '@/shared/api';
import { SplitButton } from '@/shared/ui/SplitButton';

interface UpdateDataProps {
  /** A job is running or was just started. */
  busy: boolean;
  /** The overview is not known yet, so starting would be a guess. */
  disabled: boolean;
  onStart: (body: StartDataJobInput) => void;
}

/** "Update data" runs all three steps; the menu runs one step alone, fetches the gaps again or loads every CSV file again. */
export function UpdateData({ busy, disabled, onStart }: UpdateDataProps) {
  return (
    <SplitButton
      label="Update data"
      groupLabel="Update the data"
      moreLabel="More update options"
      startIcon={<SyncIcon />}
      busy={busy}
      disabled={disabled}
      onClick={() => {
        onStart({});
      }}
      options={[
        {
          id: 'csv',
          label: 'Load CSV files only',
          description: 'Read new or changed files from the CSV folder',
          onSelect: () => {
            onStart({ steps: ['csv'] });
          },
        },
        {
          id: 'dhan',
          label: 'Fetch from Dhan only',
          description: 'Add the 1-minute bars missing since the last bar',
          onSelect: () => {
            onStart({ steps: ['dhan'] });
          },
        },
        {
          id: 'aggregate',
          label: 'Rebuild aggregates only',
          description: 'Build 5m to 1M bars from the 1-minute bars',
          onSelect: () => {
            onStart({ steps: ['aggregate'] });
          },
        },
        {
          id: 'refetch-gaps',
          label: 'Re-fetch gaps',
          description: 'Fetch the sessions with missing bars again, unfillable ones too, then rebuild the aggregates',
          onSelect: () => {
            onStart({ steps: ['dhan', 'aggregate'], refetchGaps: true });
          },
        },
        {
          id: 'reimport',
          label: 'Re-import all CSV files',
          description: 'Load every file again, even if unchanged (CSV step only)',
          onSelect: () => {
            onStart({ steps: ['csv'], forceCsv: true });
          },
        },
      ]}
    />
  );
}
