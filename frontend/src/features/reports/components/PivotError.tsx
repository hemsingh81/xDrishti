import { Alert, Button } from '@mui/material';
import { ApiError } from '@/shared/api';
import { DEFAULT_COLUMNS, DEFAULT_METRIC, DEFAULT_ROWS } from '../lib/pivotLimits';
import { ReportState } from './ReportState';

interface PivotErrorProps {
  error: unknown;
  set: (patch: Record<string, string | null>) => void;
}

const sizeCodes = new Set(['reports.invalid', 'reports.too_large']);

/**
 * A refused pivot: the server's message and a way back to the default valid pivot. A double count offers to group by exit as
 * well (the columns become exit); a too-large pivot adds "Choose a smaller dimension". Run-set and net refusals stay with
 * `ReportState`.
 */
export function PivotError({ error, set }: PivotErrorProps) {
  const code = error instanceof ApiError ? error.problem?.code : undefined;
  const handled =
    code === 'reports.double_count' || code === 'reports.dimension_unavailable' || (code && sizeCodes.has(code));
  if (!handled) return <ReportState error={error} />;
  const message = error instanceof Error ? error.message : 'The pivot was refused.';
  const back = () => {
    set({ rows: null, columns: null, metric: null });
  };
  return (
    <Alert
      severity="warning"
      sx={{ my: 1 }}
      action={
        <>
          {code === 'reports.double_count' && (
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                set({ columns: 'exit' });
              }}
              sx={{ minHeight: 44 }}
            >
              Group by exit as well
            </Button>
          )}
          <Button color="inherit" size="small" onClick={back} sx={{ minHeight: 44 }}>
            {`Back to ${DEFAULT_ROWS} by ${DEFAULT_COLUMNS}, ${DEFAULT_METRIC.replaceAll('_', ' ')}`}
          </Button>
        </>
      }
    >
      {message}
      {code && sizeCodes.has(code) ? ' Choose a smaller dimension.' : ''}
    </Alert>
  );
}
