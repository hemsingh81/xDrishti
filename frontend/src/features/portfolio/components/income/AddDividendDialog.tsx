import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useAddDividend } from '../../api/dividendMutations';
import {
  dividendFieldOrder,
  emptyDividendForm,
  placeDividendErrors,
  toDividendInput,
  validateDividend,
  type DividendField,
  type DividendForm,
  type DividendFormErrors,
} from '../../lib/dividendForm';
import { fieldMessages, problemKind, problemText } from '../../lib/errors';
import { DividendFields } from './DividendFields';
import { DividendHoldingField } from './DividendHoldingField';

export interface DividendAccount {
  id: string;
  name: string;
}

export interface DividendHolding {
  id: string;
  symbol: string;
  name: string;
}

interface AddDividendDialogProps {
  accounts: readonly DividendAccount[];
  /** The account chosen at the start; '' = the owner chooses. */
  initialAccountId: string;
  /** When the dividend is for one holding (opened from its drawer) the account and the holding are given. */
  holding?: DividendHolding;
  onClose: () => void;
}

/**
 * Add a dividend you received: pay date, amount, per share and note; for a holding, or, from the Cash & dividends panel, for an
 * account with a holding left open (an unallocated dividend). Checked here and again by the API, whose messages are shown at
 * their fields. The same account, holding, date and amount twice is allowed (two genuine payments) but asks first: the
 * API answers 409 `portfolio.possible_duplicate` and the request is repeated with `confirm` once the owner said yes.
 */
export function AddDividendDialog({ accounts, initialAccountId, holding, onClose }: AddDividendDialogProps) {
  const { notify } = useToast();
  const add = useAddDividend();
  const [accountId, setAccountId] = useState(initialAccountId);
  const [form, setForm] = useState<DividendForm>({ ...emptyDividendForm, holdingId: holding?.id ?? '' });
  const [clientErrors, setClientErrors] = useState<DividendFormErrors>({});
  const bodyRef = useRef<HTMLDivElement>(null);
  const accountRef = useRef<HTMLSelectElement>(null);
  const dateRef = useRef<HTMLInputElement>(null);

  const kind = add.isError ? problemKind(add.error) : null;
  const server = kind === 'invalid' ? placeDividendErrors(fieldMessages(add.error)) : null;
  const errors: DividendFormErrors = { ...server?.errors, ...clientErrors };
  const duplicate = kind === 'possible_duplicate';
  // A refusal that names no field the form has (or none at all) is said in words, so the owner never sees nothing.
  const nothingPlaced = server === null || (Object.keys(server.errors).length === 0 && server.unplaced.length === 0);
  const refusal =
    add.isError && !duplicate && nothingPlaced
      ? problemText(add.error, 'The dividend could not be added. Nothing was changed.')
      : null;

  /** The keyboard goes to the first control of a field (each field sits in an element marked `data-field`). */
  function focusField(field: DividendField) {
    if (field === 'account') accountRef.current?.focus();
    else bodyRef.current?.querySelector<HTMLElement>(`[data-field="${field}"] :is(input, textarea)`)?.focus();
  }

  function change(patch: Partial<DividendForm>) {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    add.reset();
  }

  function send(confirm: boolean) {
    add.mutate(toDividendInput(form, accountId, confirm), {
      onSuccess: (dividend) => {
        notify(`Added the dividend of ${formatInrPrecise(dividend.amount)} on ${formatDate(dividend.payDate)}.`);
        onClose();
      },
      onError: (error) => {
        // The keyboard goes to the first field the API found fault with (once its message is on screen).
        const placed = problemKind(error) === 'invalid' ? placeDividendErrors(fieldMessages(error)).errors : {};
        const field = dividendFieldOrder.find((name) => placed[name] !== undefined);
        if (field)
          requestAnimationFrame(() => {
            focusField(field);
          });
      },
    });
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateDividend(form, accountId);
    setClientErrors(found);
    const first = dividendFieldOrder.find((field) => found[field] !== undefined);
    if (first) {
      focusField(first);
      return;
    }
    send(false);
  }

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={add.isPending}
      maxWidth="sm"
      fullScreenOnMobile
      initialFocusRef={holding ? dateRef : accountRef}
      onSubmit={submit}
    >
      <AppDialogHeader subtitle={holding ? `${holding.symbol} · ${holding.name}` : undefined}>
        Add dividend
      </AppDialogHeader>
      <AppDialogBody ref={bodyRef}>
        <Stack spacing={2.5}>
          {!holding && (
            <TextField
              select
              label="Account"
              value={accountId}
              inputRef={accountRef}
              error={errors.account !== undefined}
              helperText={errors.account}
              onChange={(event) => {
                setAccountId(event.target.value);
                change({ holdingId: '' });
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
            <DividendHoldingField
              key={accountId}
              accountId={accountId}
              value={form.holdingId}
              onChange={(holdingId) => {
                change({ holdingId });
              }}
            />
          )}
          <DividendFields form={form} errors={errors} onChange={change} dateRef={dateRef} />
          {server && server.unplaced.length > 0 && <Alert severity="error">{server.unplaced.join(' ')}</Alert>}
          {refusal && (
            <Alert severity="error" role="alert">
              {refusal}
            </Alert>
          )}
          {duplicate && (
            <Alert
              severity="warning"
              role="alert"
              action={
                <Stack direction="row" sx={{ gap: 0.5 }}>
                  <Button
                    color="inherit"
                    size="small"
                    onClick={() => {
                      send(true);
                    }}
                  >
                    Yes, add it again
                  </Button>
                  <Button color="inherit" size="small" onClick={add.reset}>
                    No, change it
                  </Button>
                </Stack>
              }
            >
              A dividend of the same amount on the same date is already entered{holding ? ' for this holding' : ''}. Is
              this a second payment?
            </Alert>
          )}
          <Typography variant="body2" color="text.secondary">
            Dividends are entered by you: Dhan credits them to your bank account, so they are not in its ledger. They
            count in total return and net XIRR from the date you enter.
          </Typography>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={add.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={add.isPending}>
          Add dividend
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
