import { Alert, Box, Button, Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { SearchField } from '@/shared/ui/SearchField';
import { useToast } from '@/shared/ui/toastContext';
import { candidatesQuery, type CandidateScope } from '../api/basketsQueries';
import { useAddMembers } from '../api/memberMutations';
import { appliedText, basketProblemText, PartialAddError } from '../lib/errors';
import { capProblem } from '../lib/limits';
import { addedMessage, plural } from '../lib/messages';
import { CandidateRow } from './CandidateRow';

interface AddInstrumentsDialogProps {
  basket: Basket;
  /** Instrument ids that are members now (they are shown, ticked and disabled). */
  memberIds: ReadonlySet<string>;
  /** What was typed in the add row: the search the dialog opens with. */
  initialQuery?: string;
  onClose: () => void;
}

/**
 * Search the master list (or only the tracked instruments), tick any number of instruments, add them. The ticks are
 * kept across searches. Instruments that are not tracked are tracked on the way; retired ones cannot be added.
 */
export function AddInstrumentsDialog({ basket, memberIds, initialQuery = '', onClose }: AddInstrumentsDialogProps) {
  const { notify } = useToast();
  const add = useAddMembers();
  const [text, setText] = useState(initialQuery);
  const [scope, setScope] = useState<CandidateScope>('all');
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  const [labels, setLabels] = useState<ReadonlyMap<string, string>>(new Map());
  const query = useQuery(candidatesQuery(text, scope));
  const items = query.data?.items ?? [];
  const selectable = items.filter((i) => !memberIds.has(i.id) && !(i.retired && !i.tracked.active));

  const toggle = (id: string) => {
    const next = new Set(picked);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setPicked(next);
    const symbol = items.find((i) => i.id === id)?.symbol;
    if (symbol && !labels.has(id)) setLabels(new Map(labels).set(id, symbol));
  };
  const toggleShown = () => {
    const all = selectable.every((i) => picked.has(i.id));
    const next = new Set(picked);
    for (const i of selectable) {
      if (all) next.delete(i.id);
      else next.add(i.id);
    }
    setPicked(next);
    setLabels(new Map([...labels, ...selectable.map((i) => [i.id, i.symbol] as const)]));
  };

  // Ticked instruments that became members meanwhile (a half-done add) are done.
  const toAdd = [...picked].filter((id) => !memberIds.has(id));
  const submit = () => {
    const ids = toAdd;
    add.mutate(
      { basketId: basket.id, instrumentIds: ids },
      {
        onSuccess: (result) => {
          const only = ids.length === 1 ? (labels.get(ids[0] ?? '') ?? '') : '';
          notify(addedMessage(result, [basket.name], only));
          onClose();
        },
      },
    );
  };

  const count = toAdd.length;
  // Checked before sending: a long add goes out in several calls and would otherwise stop half way.
  const tooMany = capProblem(basket.name, basket.memberCount, count);
  const applied =
    add.error instanceof PartialAddError ? appliedText([{ basket: basket.name, added: add.error.applied.added }]) : '';
  return (
    <AppDialog open onClose={onClose} busy={add.isPending} maxWidth="md">
      <AppDialogHeader subtitle="Instruments that are not tracked yet are tracked for you.">
        {`Add instruments to ${basket.name}`}
      </AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { sm: 'center' } }}>
            <SearchField
              value={text}
              onCommit={setText}
              label="Search instruments"
              placeholder="Symbol, name or ISIN"
            />
            <ToggleButtonGroup
              exclusive
              size="small"
              value={scope}
              aria-label="Search in"
              onChange={(_event, next: CandidateScope | null) => {
                if (next) setScope(next);
              }}
            >
              <ToggleButton value="all">All instruments</ToggleButton>
              <ToggleButton value="tracked">Tracked</ToggleButton>
            </ToggleButtonGroup>
          </Stack>
          {add.isError && (
            <Alert severity="error">
              {[basketProblemText(add.error, 'The instruments could not be added.'), applied].filter(Boolean).join(' ')}
            </Alert>
          )}
          {tooMany && <Alert severity="warning">{tooMany}</Alert>}
          {query.isPending && <LoadingState label="Loading instruments" />}
          {query.isError && !query.data && (
            <Alert
              severity="error"
              action={
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    void query.refetch();
                  }}
                >
                  Retry
                </Button>
              }
            >
              {basketProblemText(query.error, 'The instruments could not be loaded.')}
            </Alert>
          )}
          {query.data && items.length === 0 && <Typography color="text.secondary">No instruments match.</Typography>}
          {items.length > 0 && (
            <Box>
              <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 0.5 }}>
                <Typography variant="body2" color="text.secondary">
                  {query.data && query.data.total > items.length
                    ? `Showing the first ${formatNumber(items.length)} of ${formatNumber(query.data.total)}. Type to narrow the search.`
                    : `${formatNumber(items.length)} found`}
                </Typography>
                <Button size="small" disabled={selectable.length === 0} onClick={toggleShown}>
                  {selectable.every((i) => picked.has(i.id)) && selectable.length > 0
                    ? 'Clear shown'
                    : 'Select all shown'}
                </Button>
              </Stack>
              <Box
                component="ul"
                aria-label="Instruments to add"
                aria-busy={query.isPlaceholderData}
                sx={{ m: 0, p: 0, maxHeight: 360, overflowY: 'auto', borderTop: 1, borderColor: 'divider' }}
              >
                {items.map((instrument) => (
                  <CandidateRow
                    key={instrument.id}
                    instrument={instrument}
                    checked={picked.has(instrument.id)}
                    member={memberIds.has(instrument.id)}
                    basketName={basket.name}
                    onToggle={toggle}
                  />
                ))}
              </Box>
            </Box>
          )}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Typography role="status" color="text.secondary" sx={{ flex: 1, pl: 1 }}>
          {count === 0 ? 'Nothing selected' : `${plural(count, 'instrument', 'instruments')} selected`}
        </Typography>
        <Button onClick={onClose} disabled={add.isPending}>
          Cancel
        </Button>
        <Button variant="contained" disabled={count === 0 || tooMany !== null} loading={add.isPending} onClick={submit}>
          {count === 0 ? 'Add' : `Add ${formatNumber(count)} to ${basket.name}`}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
