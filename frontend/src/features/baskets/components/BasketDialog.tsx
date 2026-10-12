import { Alert, Button, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { Basket } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useSaveBasket } from '../api/basketMutations';
import { basketFieldErrors, basketProblemText } from '../lib/errors';
import {
  basketFormFrom,
  maxDescriptionLength,
  maxNameLength,
  toBasketInput,
  validateBasket,
  type BasketForm,
} from '../lib/basketForm';
import { purposeOptions } from '../lib/labels';
import { ColourField } from './ColourField';

interface BasketDialogProps {
  /** Absent: a new basket. */
  basket?: Basket;
  onClose: () => void;
  /** Called with the saved basket (to select a new one). */
  onSaved: (basket: Basket) => void;
}

const shownFields = ['name', 'colour', 'purpose', 'description'];

/** Create or edit a basket: name, colour (palette or hex), purpose, description. The server's field messages show on the field. */
export function BasketDialog({ basket, onClose, onSaved }: BasketDialogProps) {
  const { notify } = useToast();
  const save = useSaveBasket();
  const nameRef = useRef<HTMLInputElement>(null);
  const [form, setForm] = useState<BasketForm>(() => basketFormFrom(basket));
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
  const serverErrors = basketFieldErrors(save.error);
  const errors = { ...serverErrors, ...clientErrors };
  const others = Object.entries(serverErrors).filter(([field]) => !shownFields.includes(field));

  const change = (patch: Partial<BasketForm>) => {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  };

  const submit = (event: SyntheticEvent) => {
    event.preventDefault();
    const found = validateBasket(form);
    setClientErrors(found);
    if (Object.keys(found).length > 0) return;
    save.mutate(
      { id: basket?.id, body: toBasketInput(form) },
      {
        onSuccess: (saved) => {
          notify(basket ? 'Basket saved.' : 'Basket created — add instruments to it.');
          onSaved(saved);
          onClose();
        },
      },
    );
  };

  const problem =
    save.isError && Object.keys(serverErrors).length === 0
      ? basketProblemText(save.error, 'The basket could not be saved.')
      : null;

  return (
    <AppDialog open onClose={onClose} busy={save.isPending} initialFocusRef={nameRef} onSubmit={submit}>
      <AppDialogHeader>{basket ? `Edit ${basket.name}` : 'New basket'}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2.5}>
          {problem && <Alert severity="error">{problem}</Alert>}
          {others.length > 0 && (
            <Alert severity="error">{others.map(([field, message]) => `${field}: ${message}`).join(' ')}</Alert>
          )}
          <TextField
            required
            fullWidth
            inputRef={nameRef}
            label="Name"
            value={form.name}
            placeholder="e.g. MyChoice"
            error={Boolean(errors.name)}
            helperText={errors.name}
            onChange={(event) => {
              change({ name: event.target.value });
            }}
            slotProps={{ htmlInput: { maxLength: maxNameLength } }}
          />
          <TextField
            select
            fullWidth
            label="Purpose"
            value={form.purpose}
            error={Boolean(errors.purpose)}
            helperText={errors.purpose ?? 'Drives defaults, e.g. long-term baskets are left out of intraday scans.'}
            onChange={(event) => {
              change({ purpose: event.target.value as BasketForm['purpose'] });
            }}
            slotProps={{ select: { native: true } }}
          >
            {purposeOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </TextField>
          <ColourField
            value={form.colour}
            error={errors.colour}
            onChange={(colour) => {
              change({ colour });
            }}
          />
          <TextField
            fullWidth
            multiline
            minRows={2}
            maxRows={5}
            label="Description"
            value={form.description}
            error={Boolean(errors.description)}
            helperText={errors.description ?? `${String(form.description.length)} / ${String(maxDescriptionLength)}`}
            onChange={(event) => {
              change({ description: event.target.value });
            }}
          />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={save.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={save.isPending}>
          {basket ? 'Save' : 'Create'}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
