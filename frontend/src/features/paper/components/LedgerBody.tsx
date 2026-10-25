import { Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperPositionsQuery } from '../api/paperQueries';
import { BasisNote } from './BasisNote';
import { ClosedTable } from './ClosedTable';
import { EquityLine } from './EquityLine';
import { PositionsTable } from './PositionsTable';
import { ReconcileLine } from './ReconcileLine';

export const EMPTY_TEXT = 'no paper trade yet: a ticket must be approved, armed and captured';

/** Positions, closed trades, equity line and reconcile status of one account. */
export function LedgerBody({ accountId }: { accountId: string }) {
  const positions = useQuery(paperPositionsQuery(accountId));
  if (positions.isPending) return <LoadingState label="Loading positions…" />;
  if (positions.isError) return <ErrorState error={positions.error} />;
  const { open, closed } = positions.data;
  const none = open.length === 0 && closed.length === 0;
  return (
    <Stack spacing={3}>
      <BasisNote basis={positions.data.basis} limits={positions.data.limits} />
      {none ? (
        <Typography>{EMPTY_TEXT}</Typography>
      ) : (
        <>
          <PositionsTable open={open} asOfSession={positions.data.asOfSession} />
          <ClosedTable closed={closed} />
        </>
      )}
      <EquityLine accountId={accountId} />
      <ReconcileLine accountId={accountId} />
    </Stack>
  );
}
