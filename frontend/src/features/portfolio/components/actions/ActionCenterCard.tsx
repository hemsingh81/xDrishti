import { Box, Card, CardContent, Stack, Typography, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState } from 'react';
import { AddToBasketDialog } from '@/features/baskets';
import type { ActionItem } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { actionsQuery, type ActionScope } from '../../api/actionQueries';
import { useActionDecision } from '../../api/actionMutations';
import { invalidateActionCenter } from '../../api/portfolioMutations';
import type { HoldingRef } from '../../api/lookupQueries';
import { useActionsCollapsed } from '../../hooks/useActionsCollapsed';
import { useDecisionFocus } from '../../hooks/useDecisionFocus';
import type { Decision } from '../../lib/actionDecision';
import { coverageNotes, decidedText, decisionProblemText, emptyListText } from '../../lib/actionText';
import { holdingIdOf, needDecisionText, type ActionStatus, type RowActionKey } from '../../lib/actions';
import { alertTargetOfItem, type AlertTarget } from '../../lib/alertTarget';
import { problemKind } from '../../lib/errors';
import { RetryAlert } from '../RetryAlert';
import { ActionCenterHeader } from './ActionCenterHeader';
import { ActionList } from './ActionList';
import { ActionListSkeleton } from './ActionListSkeleton';
import { ActionStatusChips } from './ActionStatusChips';

export interface ActionCenterProps {
  /** The account and bucket of the scope bar. */
  scope: ActionScope;
  /** In words ("Test Dhan Main · Long term") when the scope narrows the items; null = all. */
  scopeLabel: string | null;
  /** Read the items again this often (ms) while the market is open; false = never. */
  pollMs: number | false;
  /** The owner came from the bell: unfold the card and put the keyboard on it. */
  focusRequested: boolean;
  onFocusHandled: () => void;
  onOpenHolding: (holding: HoldingRef) => void;
  /** The sector item: show the account's holdings grouped by sector. */
  onShowSector: (accountId: string) => void;
  onSetAlert: (target: AlertTarget) => void;
  onOpenAlerts: () => void;
}

const holdingRefOf = (item: ActionItem): HoldingRef | null => {
  const id = holdingIdOf(item);
  const symbol = item.symbol ?? item.numbers.largestSymbol;
  return id && symbol ? { id, accountId: item.accountId, symbol } : null;
};

/**
 * The Action center: the holdings that need a decision, by severity, each with its numbers and its buttons (open, set an
 * alert, add to a basket, snooze, done). It follows the scope bar's account and bucket, folds away (and starts folded when
 * nothing needs a decision), and says what its rules could not cover. The list is never the list of another status or
 * scope: while the right one loads there are placeholder rows.
 */
export function ActionCenterCard({
  scope,
  scopeLabel,
  pollMs,
  focusRequested,
  onFocusHandled,
  onOpenHolding,
  onShowSector,
  onSetAlert,
  onOpenAlerts,
}: ActionCenterProps) {
  const titleId = useId();
  const bodyId = useId();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const phone = useMediaQuery(useTheme().breakpoints.down('sm'));
  const cardRef = useRef<HTMLDivElement>(null);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const [status, setStatus] = useState<ActionStatus>('open');
  const [choice, setChoice] = useActionsCollapsed();
  const [basketFor, setBasketFor] = useState<{ instrumentId: string; symbol: string } | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const query = useQuery({ ...actionsQuery(scope, status), refetchInterval: pollMs });
  const decide = useActionDecision();
  const focus = useDecisionFocus(titleRef, query.data);
  // The counts are those of the scope, whatever the status: the placeholder of another status of the same scope has them
  // right (a placeholder of another scope is never given). Items are only those of this very list.
  const counts = query.data?.counts;
  const list = query.isPlaceholderData ? undefined : query.data;
  const collapsed = choice ?? (counts ? counts.open === 0 : false);

  // Arriving from the bell: unfold, scroll to the card and move the keyboard to its title (once the card has its answer).
  const ready = !query.isPending;
  useEffect(() => {
    if (!focusRequested || !ready) return;
    setChoice(false);
    onFocusHandled();
    if (typeof cardRef.current?.scrollIntoView === 'function') cardRef.current.scrollIntoView({ block: 'start' });
    titleRef.current?.focus({ preventScroll: true });
  }, [focusRequested, ready, onFocusHandled, setChoice]);

  async function makeDecision(item: ActionItem, decision: Decision) {
    focus.leaving(item.id);
    try {
      const result = await decide.mutateAsync({ id: item.id, decision });
      notify(decidedText(decision, item, result.snoozedUntil));
    } catch (error) {
      focus.returning(item.id, decision);
      notify(decisionProblemText(error, decision, item), 'error');
    }
  }

  function onAction(item: ActionItem, action: RowActionKey) {
    setNote(null);
    switch (action) {
      case 'open':
        if (item.rule === 'sector_concentration') {
          onShowSector(item.accountId);
          setNote(`Showing the holdings of ${item.accountName} by sector, below.`);
        } else openHolding(item);
        return;
      case 'set_alert': {
        const target = alertTargetOfItem(item);
        if (target) onSetAlert(target);
        return;
      }
      case 'add_to_basket':
        if (item.instrumentId) setBasketFor({ instrumentId: item.instrumentId, symbol: item.symbol ?? item.title });
        return;
      case 'snooze':
      case 'done':
      case 'reopen':
        void makeDecision(item, action);
        return;
    }
  }

  function openHolding(item: ActionItem) {
    const ref = holdingRefOf(item);
    if (ref) onOpenHolding(ref);
  }

  const failure = query.isError ? problemKind(query.error) : null;
  const retryable = failure !== 'invalid' && failure !== 'not_found';

  return (
    <Card ref={cardRef} component="section" aria-labelledby={titleId} sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <ActionCenterHeader
          titleId={titleId}
          bodyId={bodyId}
          titleRef={titleRef}
          countText={counts ? needDecisionText(counts.open) : null}
          scopeLabel={scopeLabel}
          collapsed={collapsed}
          onToggle={() => {
            setChoice(!collapsed);
          }}
          onOpenAlerts={onOpenAlerts}
        />
        {!collapsed && (
          <Box id={bodyId} sx={{ mt: 1.5 }}>
            <ActionStatusChips
              value={status}
              counts={counts}
              onChange={(next) => {
                setNote(null);
                setStatus(next);
              }}
            />
            {query.isError && (
              <RetryAlert
                error={query.error}
                fallback={
                  list
                    ? 'The Action center could not be refreshed: this is the last list that was loaded.'
                    : 'The Action center could not be loaded.'
                }
                severity={list ? 'warning' : 'error'}
                sx={{ mt: 1.5 }}
                onRetry={retryable ? query.refetch : undefined}
              />
            )}
            {counts && coverageNotes(counts).length > 0 && (
              <Stack spacing={0.25} sx={{ mt: 1.5 }}>
                {coverageNotes(counts).map((text) => (
                  <Typography key={text} variant="body2" color="text.secondary">
                    {text}
                  </Typography>
                ))}
              </Stack>
            )}
            <Typography role="status" aria-live="polite" variant="body2" sx={{ mt: note ? 1.5 : 0, fontWeight: 600 }}>
              {note}
            </Typography>
            {!list && !query.isError && (
              <Box sx={{ mt: 1.5 }}>
                <ActionListSkeleton />
              </Box>
            )}
            {list && (
              <Box ref={focus.listRef} sx={{ mt: 1.5, maxHeight: { xs: 480, md: 420 }, overflowY: 'auto' }}>
                {list.items.length === 0 ? (
                  <Typography color="text.secondary" sx={{ py: 2 }}>
                    {emptyListText[status]}
                  </Typography>
                ) : (
                  <ActionList items={list.items} phone={phone} onAction={onAction} onOpenHolding={openHolding} />
                )}
              </Box>
            )}
          </Box>
        )}
      </CardContent>
      {basketFor && (
        <AddToBasketDialog
          instrumentIds={[basketFor.instrumentId]}
          label={basketFor.symbol}
          onClose={() => {
            setBasketFor(null);
          }}
          onAdded={() => {
            setBasketFor(null);
            // The "not in a basket" item is gone now.
            void invalidateActionCenter(queryClient);
          }}
        />
      )}
    </Card>
  );
}
