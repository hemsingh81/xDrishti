import { Button, Stack, Typography } from '@mui/material';
import { useId, useState } from 'react';
import type { Decisions } from '../hooks/useDecisions';
import type { ReviewTicket } from '../api/planQueries';
import { approveBlock, confirmableCodes, decidable } from '../lib/checks';
import { noOrderWording } from '../lib/planText';
import { ConfirmDespiteDialog } from './ConfirmDespiteDialog';
import { ModifyDialog } from './ModifyDialog';
import { RejectDialog } from './RejectDialog';

interface Props {
  ticket: ReviewTicket;
  /** Whether approving and changing are still open (plan open and before the cutoff). */
  reviewOpen: boolean;
  /** Whether the plan still takes a rejection (until the cutoff job has closed it). */
  planOpen: boolean;
  names: Record<string, string>;
  decisions: Decisions;
}

/** Approve / Confirm despite / Modify / Reject for one primary or reserve ticket, with the no-order wording. */
export function TicketActions({ ticket, reviewOpen, planOpen, names, decisions }: Props) {
  const [dialog, setDialog] = useState<'despite' | 'modify' | 'reject' | null>(null);
  const reasonId = useId();
  const block = approveBlock(ticket, reviewOpen);
  const canConfirm =
    block !== null &&
    reviewOpen &&
    decidable(ticket.state) &&
    ticket.checklistRecorded &&
    ticket.accounts.some((a) => confirmableCodes(a).length > 0) &&
    !ticket.accounts.some((a) => a.checks.some((c) => c.standing.toLowerCase() === 'refused'));
  const done = () => {
    setDialog(null);
  };
  const close = () => {
    if (!decisions.busy) setDialog(null);
  };

  return (
    <Stack spacing={1}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button
          variant="contained"
          size="small"
          disabled={block !== null || decisions.busy}
          aria-describedby={block ? reasonId : undefined}
          onClick={() => {
            decisions.approve.mutate(
              { itemId: ticket.itemId, symbol: ticket.symbol, confirmations: [] },
              { onSettled: done },
            );
          }}
        >
          Approve
        </Button>
        {canConfirm && (
          <Button
            variant="outlined"
            size="small"
            onClick={() => {
              setDialog('despite');
            }}
          >
            Confirm despite…
          </Button>
        )}
        <Button
          size="small"
          disabled={!reviewOpen || ticket.state.toLowerCase() !== 'proposed' || decisions.busy}
          onClick={() => {
            setDialog('modify');
          }}
        >
          Modify
        </Button>
        <Button
          size="small"
          color="error"
          disabled={!planOpen || !decidable(ticket.state) || decisions.busy}
          onClick={() => {
            setDialog('reject');
          }}
        >
          Reject
        </Button>
      </Stack>
      {block && (
        <Typography id={reasonId} variant="caption" color="text.secondary">
          Approve is off: {block}
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary">
        {noOrderWording}
      </Typography>
      {dialog === 'despite' && (
        <ConfirmDespiteDialog
          symbol={ticket.symbol}
          accounts={ticket.accounts}
          names={names}
          busy={decisions.busy}
          onClose={close}
          onConfirm={(confirmations) => {
            decisions.approve.mutate(
              { itemId: ticket.itemId, symbol: ticket.symbol, confirmations },
              { onSettled: done },
            );
          }}
        />
      )}
      {dialog === 'modify' && (
        <ModifyDialog
          symbol={ticket.symbol}
          accounts={ticket.accounts}
          names={names}
          busy={decisions.busy}
          onClose={close}
          onSubmit={(reason, quantities) => {
            decisions.modify.mutate(
              { itemId: ticket.itemId, symbol: ticket.symbol, reason, quantities },
              { onSettled: done },
            );
          }}
        />
      )}
      {dialog === 'reject' && (
        <RejectDialog
          symbol={ticket.symbol}
          busy={decisions.busy}
          onClose={close}
          onSubmit={(reason) => {
            decisions.reject.mutate({ itemId: ticket.itemId, symbol: ticket.symbol, reason }, { onSettled: done });
          }}
        />
      )}
    </Stack>
  );
}
