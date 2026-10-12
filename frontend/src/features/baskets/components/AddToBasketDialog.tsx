import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import type { AddMembersResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { basketsQuery } from '../api/basketsQueries';
import { useAddMembers } from '../api/memberMutations';
import { manualBaskets } from '../lib/groups';
import { appliedText, basketProblemText, PartialAddError } from '../lib/errors';
import { capProblem } from '../lib/limits';
import { addedMessage } from '../lib/messages';
import { purposeLabel } from '../lib/labels';
import { BasketDot } from './BasketDot';

interface AddToBasketDialogProps {
  /** The instruments to add (any number: more than 500 go out in several calls). */
  instrumentIds: readonly string[];
  /** How the instruments are named in messages: a symbol for one, "12 instruments" for several. */
  label: string;
  onClose: () => void;
  /** Everything was added. */
  onAdded: () => void;
}

/**
 * "Add to basket" for the Instruments screen: tick one or more baskets and add the instruments to each. The ones that
 * are not tracked yet are tracked on the way (the toast says how many).
 */
export function AddToBasketDialog({ instrumentIds, label, onClose, onAdded }: AddToBasketDialogProps) {
  const { notify } = useToast();
  const list = useQuery(basketsQuery);
  const { mutateAsync, isPending } = useAddMembers();
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set());
  const [problem, setProblem] = useState<string | null>(null);
  // Portfolio (system) baskets follow the holdings and are never a target.
  const baskets = useMemo(() => manualBaskets(list.data ?? []), [list.data]);

  const toggle = (id: string) => {
    const next = new Set(chosen);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setChosen(next);
    setProblem(null);
  };

  const submit = async () => {
    const targets = baskets.filter((b) => chosen.has(b.id));
    // Refused before anything is sent: a long add goes out in several calls and would stop half way.
    const tooMany = targets.flatMap((b) => capProblem(b.name, b.memberCount, instrumentIds.length, true) ?? []);
    if (tooMany.length > 0) {
      setProblem(tooMany.join(' '));
      return;
    }
    let newlyTracked = 0;
    let last: AddMembersResult | null = null;
    const applied: { basket: string; added: number }[] = [];
    for (const basket of targets) {
      try {
        last = await mutateAsync({ basketId: basket.id, instrumentIds });
        applied.push({ basket: basket.name, added: last.added });
        // Tracked by the first basket that got them; the next ones find them tracked.
        newlyTracked += last.newlyTracked;
      } catch (error) {
        // What was applied stays; the lists and counts were reloaded when each call settled.
        if (error instanceof PartialAddError) applied.push({ basket: basket.name, added: error.applied.added });
        const where = targets.length > 1 ? `${basket.name}: ` : '';
        setProblem(
          [`${where}${basketProblemText(error, 'The instruments could not be added.')}`, appliedText(applied)]
            .filter(Boolean)
            .join(' '),
        );
        return;
      }
    }
    if (last)
      notify(
        addedMessage(
          last,
          targets.map((b) => b.name),
          label,
          newlyTracked,
        ),
      );
    onAdded();
  };

  const count = chosen.size;
  return (
    <AppDialog open onClose={onClose} busy={isPending}>
      <AppDialogHeader subtitle="Instruments that are not tracked yet are tracked for you.">
        {`Add ${label} to baskets`}
      </AppDialogHeader>
      <AppDialogBody>
        {list.isPending && <LoadingState label="Loading baskets" />}
        {list.isError && !list.data && (
          <Alert
            severity="error"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  void list.refetch();
                }}
              >
                Retry
              </Button>
            }
          >
            {basketProblemText(list.error, 'The baskets could not be loaded.')}
          </Alert>
        )}
        {list.data && baskets.length === 0 && (
          <Typography color="text.secondary">
            There are no baskets yet. <RouterLink to="/baskets">Create one on the Baskets screen.</RouterLink>
          </Typography>
        )}
        {problem && (
          <Alert severity="error" sx={{ mb: 1.5 }}>
            {problem}
          </Alert>
        )}
        {baskets.length > 0 && (
          <Stack component="ul" aria-label="Baskets" sx={{ m: 0, p: 0 }}>
            {baskets.map((basket) => (
              <Stack component="li" key={basket.id} sx={{ listStyle: 'none' }}>
                <FormControlLabel
                  sx={{ m: 0, py: 0.25 }}
                  control={
                    <Checkbox
                      size="small"
                      checked={chosen.has(basket.id)}
                      onChange={() => {
                        toggle(basket.id);
                      }}
                    />
                  }
                  label={
                    <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                      <BasketDot colour={basket.colour} />
                      <Typography component="span" sx={{ fontWeight: 600 }}>
                        {basket.name}
                      </Typography>
                      <Typography component="span" variant="body2" color="text.secondary">
                        {`${purposeLabel(basket.purpose)} · ${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
                      </Typography>
                    </Stack>
                  }
                />
              </Stack>
            ))}
          </Stack>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={isPending}>
          Cancel
        </Button>
        <Button
          variant="contained"
          disabled={count === 0}
          loading={isPending}
          onClick={() => {
            void submit();
          }}
        >
          {count === 0 ? 'Add' : `Add to ${formatNumber(count)} ${count === 1 ? 'basket' : 'baskets'}`}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
