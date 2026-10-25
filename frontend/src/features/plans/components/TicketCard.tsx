import { Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import type { PlanItem, ReviewTicket } from '../api/planQueries';
import type { Decisions } from '../hooks/useDecisions';
import { stateLabel } from '../lib/planText';
import { LevelsRow } from './LevelsRow';
import { TicketActions } from './TicketActions';
import { TicketChecklist } from './TicketChecklist';

interface Props {
  item: PlanItem;
  /** The review row of a primary or reserve ticket; absent for a rejected candidate. */
  ticket: ReviewTicket | undefined;
  reviewOpen: boolean;
  planOpen: boolean;
  names: Record<string, string>;
  decisions: Decisions;
}

/** One candidate: what it is, the evidence with its limits, the sizes per paper account, the checks and the actions. */
export function TicketCard({ item, ticket, reviewOpen, planOpen, names, decisions }: Props) {
  const e = item.evidence;
  const pWin = e.pWin === null ? 'not available' : `${Math.round(e.pWin * 100)}%`;
  return (
    <Card component="article" aria-label={`${item.symbol} ${item.side} ${item.group}`}>
      <CardContent>
        <Stack spacing={1.25}>
          <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
            <Typography variant="h3" component="h4">
              {item.symbol}
            </Typography>
            <Chip size="small" variant="outlined" label={item.side} />
            {item.state !== null && <Chip size="small" label={`Ticket: ${stateLabel(item.state)}`} />}
            <Typography variant="caption" color="text.secondary">
              #{item.rank} · {item.strategyId}
            </Typography>
          </Stack>
          <Typography variant="body2">
            Trigger: {item.trigger} · Stop: {item.stop} · Target: {item.target} · Valid: {item.validity}
          </Typography>
          <LevelsRow levels={item.levels} />
          <Typography variant="body2">
            <strong>Evidence:</strong> cell {e.cellKey}, {e.trades} trades. {e.dataRange}. Win probability {pWin}
            {e.conservativeExpectancyR === null
              ? ''
              : `, conservative expectancy ${e.conservativeExpectancyR.toFixed(2)} R`}{' '}
            ({e.pWinBasis}).
            {e.advisoryNote ? ` Advisory only: ${e.advisoryNote}` : ''}
          </Typography>
          {e.unavailable.length > 0 && (
            <Typography variant="body2">Unavailable inputs: {e.unavailable.join(', ')}</Typography>
          )}
          {item.reasons.length > 0 && (
            <Typography variant="body2">
              {item.group === 'primary' ? 'Notes' : 'Why not primary'}: {item.reasons.join(', ')}
            </Typography>
          )}
          {item.allocations.length > 0 && (
            <Typography variant="body2">
              Sizes:{' '}
              {item.allocations.map((a) => `${names[a.accountId] ?? a.accountId} (${a.mode}) ${a.quantity}`).join('; ')}
            </Typography>
          )}
          {ticket && <TicketChecklist accounts={ticket.accounts} names={names} />}
          {ticket?.modification && <Typography variant="body2">Your size change: {ticket.modification}</Typography>}
          {ticket?.confirmedDespite && (
            <Typography variant="body2">Approved despite: {ticket.confirmedDespite}</Typography>
          )}
          {ticket && (
            <TicketActions
              ticket={ticket}
              reviewOpen={reviewOpen}
              planOpen={planOpen}
              names={names}
              decisions={decisions}
            />
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}
