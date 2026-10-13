import { Alert, Box, Button, Chip, Grid, Stack, TextField, Typography } from '@mui/material';
import { useEffect, useRef, useState, type SyntheticEvent } from 'react';
import type { ActionThresholdsView } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { useToast } from '@/shared/ui/toastContext';
import { useResetThresholds, useSaveThresholds } from '../api/thresholdsApi';
import { fieldMessages, problemText } from '../lib/problems';
import {
  formFrom,
  rangeHint,
  sameValues,
  thresholdFields,
  thresholdGroups,
  toInput,
  validateThresholds,
  type ThresholdKey,
} from '../lib/thresholds';

/** The ten thresholds, grouped by rule, with Save and Restore defaults. Starts from the saved values it is given. */
export function ThresholdsForm({ view }: { view: ActionThresholdsView }) {
  const { notify } = useToast();
  const save = useSaveThresholds();
  const reset = useResetThresholds();
  const formRef = useRef<HTMLFormElement>(null);
  const [form, setForm] = useState(() => formFrom(view.values));
  const clientErrors = validateThresholds(form);
  const serverMessages = save.isError ? fieldMessages(save.error) : {};
  const errorOf = (key: ThresholdKey): string | undefined => clientErrors[key] ?? serverMessages[key];
  const known = new Set<string>(thresholdFields.map((field) => field.key));
  const otherMessages = Object.entries(serverMessages)
    .filter(([field]) => !known.has(field))
    .map(([, message]) => message);
  const invalid = Object.keys(clientErrors).length > 0;
  const unchanged = sameValues(form, view.values);
  const failure =
    save.isError && Object.keys(serverMessages).length === 0
      ? problemText(save.error, 'The thresholds could not be saved.')
      : null;
  const busy = save.isPending || reset.isPending;
  // Restoring needs a request when the saved values are customised; when only the form was edited it just goes back.
  const canRestore = !view.isDefault || !sameValues(form, view.defaults);

  // After a refusal the keyboard goes to the first field with a message (once the messages are on screen).
  const refused = save.isError;
  useEffect(() => {
    if (refused) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [refused]);

  function change(key: ThresholdKey, value: string) {
    setForm((previous) => ({ ...previous, [key]: value }));
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (invalid || unchanged) return;
    save.mutate(toInput(form), {
      onSuccess: (saved) => {
        setForm(formFrom(saved.values));
        notify('Action center thresholds saved. The Action center uses them from now on.');
      },
    });
  }

  function restore() {
    save.reset();
    if (view.isDefault) {
      setForm(formFrom(view.defaults));
      return;
    }
    reset.mutate(undefined, {
      onSuccess: (restored) => {
        setForm(formFrom(restored.values));
        notify('Action center thresholds restored to the defaults.');
      },
      onError: (error) => {
        notify(problemText(error, 'The defaults could not be restored. Nothing was changed.'), 'error');
      },
    });
  }

  return (
    <Box component="form" ref={formRef} noValidate onSubmit={submit} aria-label="Action center thresholds">
      <Stack spacing={3}>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <Chip
            size="small"
            variant={view.isDefault ? 'outlined' : 'filled'}
            color={view.isDefault ? 'default' : 'primary'}
            label={view.isDefault ? 'Defaults' : 'Customised'}
          />
          {!view.isDefault && view.updatedBy && view.updatedAt && (
            <Typography variant="body2" color="text.secondary">
              {`Changed by ${view.updatedBy} on ${formatIstDateTime(view.updatedAt)}`}
            </Typography>
          )}
        </Stack>
        {thresholdGroups.map((group) => (
          <Box key={group.title} component="fieldset" sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
            <Typography component="legend" variant="h3" sx={{ mb: 0.5, p: 0 }}>
              {group.title}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
              {group.description}
            </Typography>
            <Grid container spacing={2}>
              {thresholdFields
                .filter((field) => group.keys.includes(field.key))
                .map((field) => (
                  <Grid key={field.key} size={{ xs: 12, sm: 6, md: 4 }}>
                    <TextField
                      fullWidth
                      label={field.label}
                      value={form[field.key]}
                      onChange={(event) => {
                        change(field.key, event.target.value);
                      }}
                      error={errorOf(field.key) !== undefined}
                      helperText={errorOf(field.key) ?? rangeHint(field)}
                      slotProps={{
                        htmlInput: { inputMode: field.integer ? 'numeric' : 'decimal', autoComplete: 'off' },
                      }}
                    />
                  </Grid>
                ))}
            </Grid>
          </Box>
        ))}
        {(failure ?? otherMessages.join(' ')) && <Alert severity="error">{failure ?? otherMessages.join(' ')}</Alert>}
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button type="submit" variant="contained" disabled={invalid || unchanged || busy} loading={save.isPending}>
            Save thresholds
          </Button>
          <Button onClick={restore} disabled={!canRestore || busy} loading={reset.isPending}>
            Restore defaults
          </Button>
        </Stack>
      </Stack>
    </Box>
  );
}
