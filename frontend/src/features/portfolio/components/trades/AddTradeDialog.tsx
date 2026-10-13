import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type SyntheticEvent } from 'react';
import type { Trade } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useAddTrade } from '../../api/tradeMutations';
import { tradeHoldingsQuery } from '../../api/tradeQueries';
import { fieldMessages, problemKind, problemText } from '../../lib/errors';
import {
  emptyTradeForm,
  placeServerErrors,
  toManualInput,
  tradeFieldOrder,
  validateTrade,
  type TradeField,
  type TradeForm,
  type TradeFormErrors,
} from '../../lib/tradeForm';
import { countOf } from '../../lib/tradeText';
import { AddTradeFields } from './AddTradeFields';
import { RebuildLotsDialog } from './RebuildLotsDialog';
import { TradeAdded } from './TradeAdded';
import { TradeSecurityField, type PickedHolding } from './TradeSecurityField';

export interface TradeAccount {
  id: string;
  name: string;
}

export interface TradeHolding {
  id: string;
  accountId: string;
  symbol: string;
  name: string;
}

interface AddTradeDialogProps {
  accounts: readonly TradeAccount[];
  /** The account chosen at the start; '' = the owner chooses. */
  initialAccountId: string;
  /** When the trade is for one holding (opened from the holding's drawer) the account and the security are given. */
  holding?: TradeHolding;
  onClose: () => void;
}

/**
 * Add one trade by hand: account, then the holding (or a typed symbol or ISIN), side, date, time, whole quantity, price and
 * charges. Checked here and again by the API, whose messages are shown at their fields. Adding changes no lot; afterwards
 * the dialog offers Rebuild lots, which shows its dry run first.
 */
export function AddTradeDialog({ accounts, initialAccountId, holding, onClose }: AddTradeDialogProps) {
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const add = useAddTrade();
  const [accountId, setAccountId] = useState(holding?.accountId ?? initialAccountId);
  const [form, setForm] = useState<TradeForm>(emptyTradeForm);
  const [picked, setPicked] = useState<PickedHolding | null>(null);
  const [clientErrors, setClientErrors] = useState<TradeFormErrors>({});
  const [added, setAdded] = useState<{ trade: Trade; holding: PickedHolding | null } | null>(null);
  const [rebuilding, setRebuilding] = useState(false);
  const bodyRef = useRef<HTMLDivElement>(null);
  const accountRef = useRef<HTMLSelectElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);

  const server =
    add.isError && problemKind(add.error) === 'invalid' ? placeServerErrors(fieldMessages(add.error)) : null;
  const errors: TradeFormErrors = { ...server?.errors, ...clientErrors };
  // A refusal that names no field the form has (or none at all) is said in words, so the owner never sees nothing.
  const nothingPlaced = server === null || (Object.keys(server.errors).length === 0 && server.unplaced.length === 0);
  const refusal =
    add.isError && nothingPlaced ? problemText(add.error, 'The trade could not be added. Nothing was changed.') : null;

  useEffect(() => {
    if (added) headingRef.current?.focus();
  }, [added]);

  /** The keyboard goes to the first control of a field (each field sits in an element marked `data-field`). */
  function focusField(field: TradeField) {
    bodyRef.current?.querySelector<HTMLElement>(`[data-field="${field}"] :is(input, button)`)?.focus();
  }

  function change(patch: Partial<TradeForm>, nextPicked?: PickedHolding | null) {
    setForm((previous) => ({ ...previous, ...patch }));
    if (nextPicked !== undefined) setPicked(nextPicked);
    setClientErrors({});
    add.reset();
  }

  function chooseAccount(id: string) {
    setAccountId(id);
    change({ holdingId: '', symbol: '', isin: '' }, null);
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateTrade(form, holding !== undefined);
    setClientErrors(found);
    const first = tradeFieldOrder.find((field) => found[field] !== undefined);
    if (first) {
      focusField(first);
      return;
    }
    if (accountId === '') {
      accountRef.current?.focus();
      return;
    }
    add.mutate(toManualInput(form, accountId, holding?.id ?? null), {
      onSuccess: (trade) => {
        notify(
          `Added the ${trade.side} of ${countOf(trade.qty, 'share')} of ${trade.symbol} on ${formatDate(trade.tradeDate)}.`,
        );
        const known = holding ?? picked;
        setAdded({ trade, holding: known });
        // A typed security may be one the account holds: Rebuild lots is offered once that is known.
        if (!known) {
          void heldSecurity(trade).then((found) => {
            // Only for the trade it was looked up for: the owner may have added another since.
            setAdded((previous) => (previous?.trade.id === trade.id ? { ...previous, holding: found } : previous));
          });
        }
      },
      onError: (error) => {
        // The keyboard goes to the first field the API found fault with (once its message is on screen).
        const placed = problemKind(error) === 'invalid' ? placeServerErrors(fieldMessages(error)).errors : {};
        const field = tradeFieldOrder.find((name) => placed[name] !== undefined);
        if (field)
          requestAnimationFrame(() => {
            focusField(field);
          });
      },
    });
  }

  /** The holding a typed security belongs to, if the account holds it: Rebuild lots needs it. */
  async function heldSecurity(trade: Trade): Promise<PickedHolding | null> {
    try {
      const rows = await queryClient.query(tradeHoldingsQuery(trade.accountId, trade.symbol));
      const match = rows.find(
        (row) =>
          row.symbol.toLowerCase() === trade.symbol.toLowerCase() || (trade.isin !== null && row.isin === trade.isin),
      );
      return match ? { id: match.id, symbol: match.symbol, name: match.name } : null;
    } catch {
      return null;
    }
  }

  if (rebuilding && added?.holding) {
    return <RebuildLotsDialog holdingId={added.holding.id} symbol={added.holding.symbol} onClose={onClose} />;
  }

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={add.isPending}
      maxWidth="sm"
      fullScreenOnMobile
      initialFocusRef={holding ? undefined : accountRef}
      onSubmit={added ? undefined : submit}
    >
      <AppDialogHeader subtitle={holding ? `${holding.symbol} · ${holding.name}` : undefined}>
        Add trade
      </AppDialogHeader>
      {added ? (
        <TradeAdded
          trade={added.trade}
          canRebuild={added.holding !== null}
          headingRef={headingRef}
          onRebuild={() => {
            setRebuilding(true);
          }}
          onAnother={() => {
            setAdded(null);
            setForm(emptyTradeForm);
            setPicked(null);
            add.reset();
          }}
          onClose={onClose}
        />
      ) : (
        <>
          <AppDialogBody ref={bodyRef}>
            <Stack spacing={2.5}>
              {!holding && (
                <TextField
                  select
                  label="Account"
                  value={accountId}
                  inputRef={accountRef}
                  onChange={(event) => {
                    chooseAccount(event.target.value);
                  }}
                  slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
                >
                  <option value="" disabled>
                    Choose an account
                  </option>
                  {accounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.name}
                    </option>
                  ))}
                </TextField>
              )}
              {!holding && (
                <TradeSecurityField
                  key={accountId}
                  accountId={accountId}
                  form={form}
                  error={errors.security}
                  onChange={change}
                />
              )}
              <AddTradeFields form={form} errors={errors} onChange={change} />
              {server && server.unplaced.length > 0 && <Alert severity="error">{server.unplaced.join(' ')}</Alert>}
              {refusal && (
                <Alert severity="error" role="alert">
                  {refusal}
                </Alert>
              )}
              <Typography variant="body2" color="text.secondary">
                Adding a trade changes no lot. Charges are stored for your records and are not used in cost or XIRR.
              </Typography>
            </Stack>
          </AppDialogBody>
          <AppDialogFooter>
            <Button onClick={onClose} disabled={add.isPending}>
              Cancel
            </Button>
            <Button type="submit" variant="contained" loading={add.isPending}>
              Add trade
            </Button>
          </AppDialogFooter>
        </>
      )}
    </AppDialog>
  );
}
