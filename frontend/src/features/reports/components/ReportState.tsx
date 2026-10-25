import { Alert, Button } from '@mui/material';
import { ApiError } from '@/shared/api';
import { useReportFilter } from '../hooks/useReportFilter';

const runSetCodes = new Set(['reports.run_overlap', 'reports.mixed_basis', 'reports.too_large']);
const netCodes = new Set(['reports.net_not_confirmed', 'reports.net_unavailable']);

/**
 * A failed report query: the server's own message (text, never markup) and, where the cause is the chosen runs or the basis,
 * the action that gets back to a state that works.
 */
export function ReportState({ error }: { error: unknown }) {
  const { set } = useReportFilter();
  const code = error instanceof ApiError ? error.problem?.code : undefined;
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  const action =
    code && runSetCodes.has(code) ? (
      <Button
        color="inherit"
        size="small"
        onClick={() => {
          set({ runs: null });
        }}
        sx={{ minHeight: 44 }}
      >
        Use the default run set
      </Button>
    ) : code && netCodes.has(code) ? (
      <Button
        color="inherit"
        size="small"
        onClick={() => {
          set({ basis: null });
        }}
        sx={{ minHeight: 44 }}
      >
        Use gross R
      </Button>
    ) : undefined;
  return (
    <Alert severity={action ? 'warning' : 'error'} action={action} sx={{ my: 1 }}>
      {message}
    </Alert>
  );
}
