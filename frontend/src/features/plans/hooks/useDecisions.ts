import { useMutation, useQueryClient } from '@tanstack/react-query';
import { approveAll, approveTicket, modifyTicket, planKeys, rejectTicket, type Confirmation } from '../api/planQueries';
import { decisionError, noOrderWording } from '../lib/planText';

/** What the last decision said, shown in words (plain text) until the next one. */
export interface Notice {
  tone: 'success' | 'error';
  text: string;
  /** Confirmations (or codes) that covered nothing, as the server lists them. */
  ignored?: string[];
  /** Tickets approve-all left to the owner, with the reason. */
  skipped?: string[];
}

/**
 * The owner's decisions on one plan. No optimistic update: the screen changes only from the server's answer
 * (the queries are read again after every call, whatever it answered).
 */
export function useDecisions(planId: number, report: (notice: Notice) => void) {
  const queryClient = useQueryClient();
  const settle = () => queryClient.invalidateQueries({ queryKey: planKeys.all });
  const fail = (e: unknown) => {
    report({ tone: 'error', text: decisionError(e) });
  };

  const approve = useMutation({
    mutationFn: (v: { itemId: number; symbol: string; confirmations: Confirmation[] }) =>
      approveTicket(planId, v.itemId, v.confirmations),
    onSuccess: (o, v) => {
      const despite = o.overriddenCodes.length > 0 ? ` Approved despite: ${o.overriddenCodes.join(', ')}.` : '';
      report({
        tone: 'success',
        text: `${v.symbol}: ${o.notice || noOrderWording}${despite}`,
        ignored: o.ignored ?? [],
      });
    },
    onError: fail,
    onSettled: settle,
  });

  const modify = useMutation({
    mutationFn: (v: {
      itemId: number;
      symbol: string;
      reason: string;
      quantities: { accountId: string; quantity: number }[];
    }) => modifyTicket(planId, v.itemId, v.reason, v.quantities),
    onSuccess: (_o, v) => {
      report({
        tone: 'success',
        text: `${v.symbol}: size lowered. It is Modified, not approved yet. ${noOrderWording}`,
      });
    },
    onError: fail,
    onSettled: settle,
  });

  const reject = useMutation({
    mutationFn: (v: { itemId: number; symbol: string; reason: string }) => rejectTicket(planId, v.itemId, v.reason),
    onSuccess: (_o, v) => {
      report({ tone: 'success', text: `${v.symbol}: rejected. No order is placed and none exists.` });
    },
    onError: fail,
    onSettled: settle,
  });

  const all = useMutation({
    mutationFn: () => approveAll(planId),
    onSuccess: (o) => {
      report({
        tone: 'success',
        text: `Approved ${o.approved.length} ticket(s). ${o.notice || noOrderWording}`,
        skipped: o.skipped.map((s) => `${s.symbol}: ${s.reason}`),
      });
    },
    onError: fail,
    onSettled: settle,
  });

  return {
    approve,
    modify,
    reject,
    all,
    busy: approve.isPending || modify.isPending || reject.isPending || all.isPending,
  };
}

export type Decisions = ReturnType<typeof useDecisions>;
