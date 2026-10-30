import { Alert, Stack } from '@mui/material';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useConfirmTickets } from '../api/confirmApi';
import { ConfirmTicketCard } from './ConfirmTicketCard';

/** The "Confirm" section: open tickets first, then the decided ones of the day. */
export function ConfirmSection() {
  const { query, confirm, skip } = useConfirmTickets();
  const tickets = [...(query.data ?? [])].sort((a, b) => Number(b.state === 'open') - Number(a.state === 'open'));
  return (
    <Stack component="section" aria-label="Confirm" sx={{ gap: 2, mb: 3 }}>
      {query.isPending && <LoadingState label="Loading orders waiting" />}
      {query.error && <ErrorState error={query.error} />}
      {query.data?.length === 0 && <Alert severity="info">No order is waiting</Alert>}
      {tickets.map((t) => {
        const mine = confirm.variables?.ticket.id === t.id;
        const mineSkip = skip.variables?.id === t.id;
        return (
          <ConfirmTicketCard
            key={t.id}
            ticket={t}
            pending={(mine && confirm.isPending) || (mineSkip && skip.isPending)}
            error={mine ? confirm.error : mineSkip ? skip.error : null}
            justConfirmed={mine && confirm.isSuccess}
            onConfirm={(v) => {
              confirm.mutate({ ticket: t, ...v });
            }}
            onSkip={() => {
              skip.mutate(t);
            }}
            onRetry={() => {
              confirm.reset();
              skip.reset();
            }}
          />
        );
      })}
    </Stack>
  );
}
