import { Alert, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { capacityQuery, executionsQuery, slippageQuery } from '../api/executionQueries';
import { emptySentence } from '../lib/executionLabels';
import { CapacityTable } from './CapacityTable';
import { ExecutionEvidence } from './ExecutionEvidence';
import { ExecutionTrades } from './ExecutionTrades';
import { SlippageTable } from './SlippageTable';

/**
 * The Execution tab of the Live screen (spec P11-02): how far your live entries landed from the plan (slippage per liquidity bucket, with its n), the share of
 * your orders that filled in full, and each entry with its waterfall. It reads only stored figures; nothing here calls a broker, places an order or changes a setting.
 */
export function ExecutionTab() {
  const slippage = useQuery(slippageQuery);
  const capacity = useQuery(capacityQuery);
  const executions = useQuery(executionsQuery);
  if (slippage.isPending || capacity.isPending || executions.isPending)
    return <LoadingState label="Loading the execution feedback…" />;
  if (slippage.isError) return <ErrorState error={slippage.error} />;
  if (capacity.isError) return <ErrorState error={capacity.error} />;
  if (executions.isError) return <ErrorState error={executions.error} />;
  return (
    <Stack sx={{ gap: 2 }}>
      <Alert severity="info">
        Read only: this tab measures what you did against the plan. It changes no setting and nothing is applied to the
        simulator or the selection.
      </Alert>
      {slippage.data.state === 'empty' ? (
        <Typography>{emptySentence}</Typography>
      ) : (
        <SlippageTable view={slippage.data} />
      )}
      {capacity.data.state !== 'empty' && <CapacityTable view={capacity.data} />}
      <ExecutionTrades page={executions.data} />
      <ExecutionEvidence
        evidence={slippage.data.state === 'empty' ? executions.data.evidence : slippage.data.evidence}
      />
    </Stack>
  );
}
