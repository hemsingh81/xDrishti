import { Alert, Button, Stack } from '@mui/material';
import { ErrorState, LoadingState } from '@/shared/ui';
import type { FirstImport } from '../../hooks/useFirstImport';
import { BulkDateBar } from './BulkDateBar';
import { ImportTable } from './ImportTable';

interface FirstImportListProps {
  firstImport: FirstImport;
  /** The first import date as typed: the date rows keep until the owner gives them another. */
  defaultDate: string;
  /** Messages from the API for a row's date, by row key. */
  serverErrors: Readonly<Record<string, string>>;
  disabled: boolean;
  /** The owner changed a date: any earlier refusal no longer applies. */
  onEdit: () => void;
}

/** What Dhan holds, one date per holding, with a bulk date control. Loading and failure are shown in place. */
export function FirstImportList({ firstImport, defaultDate, serverErrors, disabled, onEdit }: FirstImportListProps) {
  const { preview, draft } = firstImport;
  if (preview.isPending) {
    return <LoadingState label="Fetching holdings from Dhan" />;
  }
  if (preview.isError) {
    return (
      <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
        <ErrorState error={preview.error} />
        <Button
          onClick={() => {
            void preview.refetch();
          }}
        >
          Retry
        </Button>
      </Stack>
    );
  }
  if (draft.importable.length === 0) {
    return <Alert severity="info">Dhan reports no holdings to import for this account.</Alert>;
  }
  return (
    <Stack spacing={2}>
      <BulkDateBar
        selectedCount={draft.selectedCount}
        rowCount={draft.importable.length}
        initialDate={defaultDate}
        onApply={(date, to) => {
          draft.applyDate(date, to);
          onEdit();
        }}
      />
      <ImportTable
        rows={preview.data.rows}
        draft={draft}
        serverErrors={serverErrors}
        disabled={disabled}
        onDateChange={(key, date) => {
          draft.setDate(key, date);
          onEdit();
        }}
      />
    </Stack>
  );
}
