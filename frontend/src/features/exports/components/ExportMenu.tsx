import { Alert, Box, Button, Typography } from '@mui/material';
import { useCapability } from '@/features/owner-acts';
import { exportProblemText, useExport, type ExportKind, type ExportQuery } from '../api/exportApi';
import { exportWording } from '../wording';

interface ExportMenuProps {
  kind: ExportKind;
  /** The fields of the screen's own GET. */
  query: ExportQuery;
}

/**
 * Saves the table on the screen as a CSV file (spec P8-20). Draws nothing while the switch `exports` is off or not yet known. The POST carries
 * the same query as the screen's GET; the file comes back as bytes, is saved through a temporary link and is kept nowhere else.
 */
export function ExportMenu({ kind, query }: ExportMenuProps) {
  const { enabled } = useCapability('exports');
  const mutation = useExport();
  if (!enabled) {
    return null;
  }
  return (
    <Box role="group" aria-label={exportWording.groupLabel} sx={{ display: 'grid', gap: 0.5, justifyItems: 'start' }}>
      <Button
        size="small"
        variant="outlined"
        disabled={mutation.isPending}
        onClick={() => {
          mutation.mutate({ kind, query });
        }}
      >
        {mutation.isPending ? exportWording.busy : exportWording.button}
      </Button>
      <Typography variant="caption" color="text.secondary">
        {exportWording.notice}
      </Typography>
      {mutation.isError && <Alert severity="warning">{exportProblemText(mutation.error)}</Alert>}
      {mutation.isSuccess && (
        <Typography variant="caption" role="status">
          {exportWording.done}
        </Typography>
      )}
    </Box>
  );
}
