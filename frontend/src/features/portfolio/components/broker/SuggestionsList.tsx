import {
  Box,
  Button,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useEffect, useState } from 'react';
import type { LedgerEntry } from '@/shared/api';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { useDismissSuggestion } from '../../api/dividendMutations';
import { suggestionsList } from '../../api/dividendQueries';
import { useFocusRequest } from '../../hooks/useFocusRequest';
import { problemKind, problemText } from '../../lib/errors';
import { AcceptSuggestionDialog } from './AcceptSuggestionDialog';
import { SuggestionRow } from './SuggestionRow';
import { RetryAlert } from '../RetryAlert';

/**
 * Ledger entries of class dividend that were not accepted or dismissed yet (Dhan's documented ledger has none for an ordinary
 * dividend, so this is usually empty and then not shown at all). Nothing is added without the owner: Accept asks for the
 * holding, Dismiss asks to be sure - both decisions are final.
 */
export function SuggestionsList({ accountId, focusOnShow = false }: { accountId: string; focusOnShow?: boolean }) {
  const { notify } = useToast();
  const list = useInfiniteRows(suggestionsList(accountId));
  const dismiss = useDismissSuggestion(accountId);
  const [accepting, setAccepting] = useState<LedgerEntry | null>(null);
  const [dismissing, setDismissing] = useState<LedgerEntry | null>(null);
  // Once a decision emptied the list the section stays, saying so: the keyboard has somewhere to be.
  const [decided, setDecided] = useState(false);
  const [headingRef, focusHeading] = useFocusRequest<HTMLHeadingElement>();
  // A link to the suggestions (the bell) puts the keyboard on their title and shows it, once they are listed.
  const listed = !list.isPending && !list.isError && list.rows.length > 0;
  useEffect(() => {
    if (!focusOnShow || !listed) return;
    if (typeof headingRef.current?.scrollIntoView === 'function') headingRef.current.scrollIntoView({ block: 'start' });
    headingRef.current?.focus({ preventScroll: true });
  }, [focusOnShow, listed, headingRef]);

  if (list.isPending) return <LoadingState label="Loading suggested dividends" />;
  if (list.isError) {
    return (
      <RetryAlert error={list.error} fallback="The suggested dividends could not be loaded." onRetry={list.refetch} />
    );
  }
  if (list.rows.length === 0 && !decided) return null;

  return (
    <Box component="section" aria-label="Suggested dividends">
      <Typography ref={headingRef} variant="h3" tabIndex={-1} sx={{ mb: 0.5 }}>
        Suggested dividends
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {list.rows.length === 0
          ? 'All suggestions are decided.'
          : `${formatNumber(list.total)} ${list.total === 1 ? 'entry' : 'entries'} of Dhan's ledger look like a dividend. Nothing is added until you accept it.`}
      </Typography>
      {list.rows.length > 0 && (
        <TableContainer role="region" aria-label="Suggested dividends, scrolls sideways" tabIndex={0}>
          <Table size="small" aria-label="Suggested dividends" sx={{ minWidth: 520 }}>
            <TableHead>
              <TableRow>
                <TableCell sx={{ fontWeight: 600 }}>Date</TableCell>
                <TableCell sx={{ fontWeight: 600 }}>Details</TableCell>
                <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Amount</TableCell>
                <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Decide</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {list.rows.map((entry) => (
                <SuggestionRow key={entry.id} entry={entry} onAccept={setAccepting} onDismiss={setDismissing} />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {list.hasMore && !list.loadMoreError && (
        <Button size="small" sx={{ mt: 1 }} loading={list.isFetchingNextPage} onClick={list.loadMore}>
          Show more
        </Button>
      )}
      {accepting && (
        <AcceptSuggestionDialog
          accountId={accountId}
          entry={accepting}
          onClose={() => {
            setAccepting(null);
            focusHeading();
          }}
          onAccepted={() => {
            setDecided(true);
          }}
        />
      )}
      {dismissing && (
        <ConfirmDialog
          open
          title="Dismiss this suggestion?"
          confirmLabel="Dismiss"
          busy={dismiss.isPending}
          onCancel={() => {
            setDismissing(null);
          }}
          onConfirm={() => {
            dismiss.mutate(dismissing.id, {
              onSuccess: () => {
                notify(
                  `Dismissed the suggested dividend of ${formatInrPrecise(dismissing.amount)} of ${formatDate(dismissing.date)}.`,
                );
                setDismissing(null);
                setDecided(true);
                focusHeading();
              },
              onError: (error) => {
                notify(
                  problemKind(error) === 'conflict'
                    ? 'This suggestion was decided meanwhile. The list is up to date now.'
                    : problemText(error, 'Could not dismiss the suggestion. Nothing was changed.'),
                  'error',
                );
                setDismissing(null);
              },
            });
          }}
        >
          It will not be offered again and nothing is added. This cannot be undone.
        </ConfirmDialog>
      )}
    </Box>
  );
}
