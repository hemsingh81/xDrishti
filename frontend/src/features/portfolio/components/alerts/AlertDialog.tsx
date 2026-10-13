import { Alert, Box, Button, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useEffect, useId, useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useCreateAlert } from '../../api/alertMutations';
import { useAlertPrice } from '../../hooks/useAlertPrice';
import {
  alertDirections,
  checkingText,
  conditionText,
  directionLabels,
  maxNoteLength,
  toAlertInput,
  validateAlert,
  type AlertDirection,
  type AlertForm,
  type AlertFormErrors,
} from '../../lib/alerts';
import type { AlertTarget } from '../../lib/alertTarget';
import { fieldMessages, problemKind, problemText } from '../../lib/errors';
import { AlertInstrumentPicker } from './AlertInstrumentPicker';
import { AlertLevelField } from './AlertLevelField';

interface AlertDialogProps {
  /** The instrument to set the alert on; null = the owner picks a share or ETF. */
  target: AlertTarget | null;
  onClose: () => void;
}

const fieldsOf = ['instrumentId', 'direction', 'level', 'note'] as const;

/**
 * Set a price alert: the instrument (given, or picked among shares and ETFs), at or below / at or above, the price (with
 * the last price beside it and quick levels around it) and an optional note. The API has the last word: its messages are
 * shown at the field they name, a full list of alerts as its own message.
 */
export function AlertDialog({ target, onClose }: AlertDialogProps) {
  const { notify } = useToast();
  const create = useCreateAlert();
  const directionLabelId = useId();
  const levelRef = useRef<HTMLInputElement>(null);
  const pickerRef = useRef<HTMLInputElement>(null);
  const refusalRef = useRef<HTMLDivElement>(null);
  const [form, setForm] = useState<AlertForm>({
    instrumentId: target?.instrumentId ?? '',
    direction: 'at_or_below',
    level: '',
    note: '',
  });
  const [picked, setPicked] = useState<{ symbol: string; name: string } | null>(null);
  const [clientErrors, setClientErrors] = useState<AlertFormErrors>({});
  const price = useAlertPrice(target, form.instrumentId, target?.symbol ?? picked?.symbol ?? '');

  const serverMessages = create.isError ? fieldMessages(create.error) : {};
  const errors: AlertFormErrors = {
    instrumentId: serverMessages.instrumentId,
    direction: serverMessages.direction,
    level: serverMessages.level,
    note: serverMessages.note,
    ...clientErrors,
  };
  // What the API said about fields this dialog has no place for, and refusals that are not about a field.
  const unplaced = Object.entries(serverMessages)
    .filter(([field]) => !fieldsOf.some((known) => known === field))
    .map(([, message]) => message);
  const refusal = !create.isError
    ? null
    : problemKind(create.error) === 'invalid'
      ? unplaced.join(' ') || null
      : problemText(create.error, 'The alert could not be set. Nothing was changed.');
  const symbol = target?.symbol ?? picked?.symbol ?? '';
  // With the instrument given there is no field to put the API's refusal of it at (retired, not a share or ETF…): say it on top.
  const instrumentRefusal = target === null ? undefined : errors.instrumentId;

  // After a refusal the keyboard goes to the price (once the messages are on screen).
  const refused = create.isError;
  useEffect(() => {
    if (!refused) return;
    (instrumentRefusal ? refusalRef : levelRef).current?.focus();
  }, [refused, instrumentRefusal]);

  function change(patch: Partial<AlertForm>) {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    create.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateAlert(form);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      (found.instrumentId ? pickerRef : levelRef).current?.focus();
      return;
    }
    create.mutate(toAlertInput(form), {
      onSuccess: (alert) => {
        notify(`Alert set: ${alert.symbol} ${conditionText(alert)}. The bell tells you when it triggers.`);
        onClose();
      },
    });
  }

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={create.isPending}
      initialFocusRef={target ? levelRef : pickerRef}
      onSubmit={submit}
    >
      <AppDialogHeader subtitle={symbol ? `${symbol}${target?.name ? ` · ${target.name}` : ''}` : undefined}>
        Set price alert
      </AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2.5}>
          {instrumentRefusal && (
            <Alert severity="error" ref={refusalRef} tabIndex={-1}>
              {instrumentRefusal}
            </Alert>
          )}
          {target === null && (
            <AlertInstrumentPicker
              value={form.instrumentId}
              inputRef={pickerRef}
              error={errors.instrumentId}
              onChange={(instrument) => {
                setPicked(instrument);
                change({ instrumentId: instrument?.id ?? '', level: '' });
              }}
            />
          )}
          <Box>
            <Typography id={directionLabelId} variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
              Alert me when the price is
            </Typography>
            <ToggleButtonGroup
              exclusive
              size="small"
              aria-labelledby={directionLabelId}
              value={form.direction}
              onChange={(_event, next: AlertDirection | null) => {
                if (next) change({ direction: next });
              }}
            >
              {alertDirections.map((direction) => (
                <ToggleButton key={direction} value={direction}>
                  {directionLabels[direction]}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
            {errors.direction && (
              <Typography variant="caption" color="error" role="alert" sx={{ display: 'block', mt: 0.5 }}>
                {errors.direction}
              </Typography>
            )}
          </Box>
          <AlertLevelField
            value={form.level}
            onChange={(level) => {
              change({ level });
            }}
            direction={form.direction}
            price={price}
            error={errors.level}
            inputRef={levelRef}
          />
          <TextField
            label="Note (optional)"
            fullWidth
            value={form.note}
            onChange={(event) => {
              change({ note: event.target.value });
            }}
            error={errors.note !== undefined}
            helperText={errors.note ?? `${String(form.note.length)} of ${String(maxNoteLength)} characters`}
          />
          {refusal && <Alert severity="error">{refusal}</Alert>}
          <Typography variant="body2" color="text.secondary">
            {checkingText}
          </Typography>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={create.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={create.isPending}>
          Set alert
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
