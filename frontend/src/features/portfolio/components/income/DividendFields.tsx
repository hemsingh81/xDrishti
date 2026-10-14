import { Stack, TextField, type TextFieldProps } from '@mui/material';
import type { Ref } from 'react';
import { todayIso } from '@/shared/lib/dates';
import { maxNoteLength, type DividendForm, type DividendFormErrors } from '../../lib/dividendForm';
import { minTradeDate } from '../../lib/tradeForm';

interface DividendFieldsProps {
  form: DividendForm;
  errors: DividendFormErrors;
  onChange: (patch: Partial<DividendForm>) => void;
  /** The pay date input, e.g. to put the keyboard on it when the dialog opens. */
  dateRef?: Ref<HTMLInputElement>;
}

type TextKey = 'payDate' | 'amount' | 'perShare' | 'note';

/** Pay date, amount received, amount per share and note of a dividend, each with the message of the check that failed. */
export function DividendFields({ form, errors, onChange, dateRef }: DividendFieldsProps) {
  const text = (key: TextKey, label: string, helper: string | undefined, extra: Partial<TextFieldProps> = {}) => (
    <TextField
      data-field={key}
      label={label}
      value={form[key]}
      error={errors[key] !== undefined}
      helperText={errors[key] ?? helper}
      onChange={(event) => {
        onChange({ [key]: event.target.value });
      }}
      sx={{ flex: 1 }}
      {...extra}
    />
  );
  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5 }}>
        {text('payDate', 'Pay date', 'The day the money was credited', {
          type: 'date',
          inputRef: dateRef,
          slotProps: { inputLabel: { shrink: true }, htmlInput: { min: minTradeDate, max: todayIso() } },
        })}
        {text('amount', 'Amount received (₹)', 'Rupees and paise, as credited', {
          slotProps: { htmlInput: { inputMode: 'decimal' } },
        })}
      </Stack>
      {text('perShare', 'Per share (₹, optional)', undefined, { slotProps: { htmlInput: { inputMode: 'decimal' } } })}
      {text('note', 'Note (optional)', `${String(form.note.length)} of ${String(maxNoteLength)} characters`, {
        multiline: true,
        minRows: 1,
        maxRows: 3,
      })}
    </Stack>
  );
}
