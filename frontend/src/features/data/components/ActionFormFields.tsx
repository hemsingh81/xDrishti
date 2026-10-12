import { Stack, TextField, Typography } from '@mui/material';
import type { Ref } from 'react';
import { adjustsCsvTwice, factorPreview, maxActionNote, ratioHelp, type ActionKind } from '../lib/actions';
import { formFactor, type ActionForm } from '../lib/actionForm';
import { CsvAdjustedWarning } from './CsvAdjustedWarning';

const partHints: Readonly<Record<ActionKind, { numerator: string; denominator: string }>> = {
  split: { numerator: 'Old shares', denominator: 'New shares' },
  bonus: { numerator: 'Bonus shares', denominator: 'Shares held' },
};

interface ActionFormFieldsProps {
  form: ActionForm;
  errors: Readonly<Record<string, string>>;
  /** The day the instrument's CSV files reach, when known (editing); an ex-date on or before it gets a warning. */
  csvAdjustedThrough?: string | null;
  onChange: (patch: Partial<ActionForm>) => void;
  dateRef?: Ref<HTMLInputElement>;
}

/** Ex-date, kind, ratio with the factor it gives, the warning for CSV history, and the note. */
export function ActionFormFields({ form, errors, csvAdjustedThrough, onChange, dateRef }: ActionFormFieldsProps) {
  const factor = formFactor(form);
  const hints = partHints[form.kind];
  return (
    <>
      <Stack direction="row" spacing={2}>
        <TextField
          required
          fullWidth
          type="date"
          label="Ex-date"
          inputRef={dateRef}
          value={form.exDate}
          error={Boolean(errors.exDate)}
          helperText={errors.exDate ?? 'The first day the shares trade at the new price'}
          onChange={(event) => {
            onChange({ exDate: event.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          select
          fullWidth
          label="Kind"
          value={form.kind}
          error={Boolean(errors.kind)}
          helperText={errors.kind}
          onChange={(event) => {
            onChange({ kind: event.target.value === 'bonus' ? 'bonus' : 'split' });
          }}
          slotProps={{ select: { native: true } }}
        >
          <option value="split">Split</option>
          <option value="bonus">Bonus</option>
        </TextField>
      </Stack>
      <Stack direction="row" spacing={2}>
        <TextField
          required
          fullWidth
          label="Numerator"
          value={form.numerator}
          error={Boolean(errors.numerator)}
          helperText={errors.numerator ?? hints.numerator}
          onChange={(event) => {
            onChange({ numerator: event.target.value });
          }}
          slotProps={{ htmlInput: { inputMode: 'numeric' } }}
        />
        <TextField
          required
          fullWidth
          label="Denominator"
          value={form.denominator}
          error={Boolean(errors.denominator)}
          helperText={errors.denominator ?? hints.denominator}
          onChange={(event) => {
            onChange({ denominator: event.target.value });
          }}
          slotProps={{ htmlInput: { inputMode: 'numeric' } }}
        />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        {ratioHelp[form.kind]}
      </Typography>
      <Typography role="status" sx={{ fontWeight: 600 }}>
        {factor !== null && form.exDate !== ''
          ? factorPreview(form.exDate, factor)
          : 'Enter the ex-date and the ratio to see the price factor.'}
      </Typography>
      {csvAdjustedThrough && adjustsCsvTwice(form.exDate, csvAdjustedThrough) && (
        <CsvAdjustedWarning through={csvAdjustedThrough} />
      )}
      <TextField
        fullWidth
        label="Note"
        value={form.note}
        error={Boolean(errors.note)}
        helperText={errors.note ?? `${String(form.note.length)} / ${String(maxActionNote)}`}
        onChange={(event) => {
          onChange({ note: event.target.value });
        }}
      />
    </>
  );
}
