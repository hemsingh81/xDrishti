import { Box, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography, type TextFieldProps } from '@mui/material';
import { useId } from 'react';
import { todayIso } from '@/shared/lib/dates';
import { minTradeDate, type TradeForm, type TradeFormErrors, type TradeSide } from '../../lib/tradeForm';

interface AddTradeFieldsProps {
  form: TradeForm;
  errors: TradeFormErrors;
  onChange: (patch: Partial<TradeForm>) => void;
}

type TextKey = 'tradeDate' | 'tradeTime' | 'quantity' | 'price' | 'charges';

const sides: readonly { value: TradeSide; label: string }[] = [
  { value: 'buy', label: 'Buy' },
  { value: 'sell', label: 'Sell' },
];

/** Side, date and time, quantity, price and charges of a trade, each with the message of the check that failed. */
export function AddTradeFields({ form, errors, onChange }: AddTradeFieldsProps) {
  const sideLabelId = useId();
  const text = (key: TextKey, label: string, extra: Partial<TextFieldProps> = {}) => (
    <TextField
      data-field={key}
      label={label}
      value={form[key]}
      error={errors[key] !== undefined}
      helperText={errors[key]}
      onChange={(event) => {
        onChange({ [key]: event.target.value });
      }}
      sx={{ flex: 1 }}
      {...extra}
    />
  );
  return (
    <Stack spacing={2}>
      <Box>
        <Typography id={sideLabelId} variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
          Side
        </Typography>
        <ToggleButtonGroup
          exclusive
          size="small"
          aria-labelledby={sideLabelId}
          data-field="side"
          value={form.side === '' ? null : form.side}
          onChange={(_event, next: TradeSide | null) => {
            if (next) onChange({ side: next });
          }}
        >
          {sides.map((side) => (
            <ToggleButton key={side.value} value={side.value}>
              {side.label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
        {errors.side && (
          <Typography variant="caption" color="error" role="alert" sx={{ display: 'block', mt: 0.5 }}>
            {errors.side}
          </Typography>
        )}
      </Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5 }}>
        {text('tradeDate', 'Trade date', {
          type: 'date',
          slotProps: { inputLabel: { shrink: true }, htmlInput: { min: minTradeDate, max: todayIso() } },
        })}
        {text('tradeTime', 'Time (optional, IST)', {
          type: 'time',
          slotProps: { inputLabel: { shrink: true } },
        })}
      </Stack>
      <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5 }}>
        {text('quantity', 'Quantity', {
          slotProps: { htmlInput: { inputMode: 'numeric' } },
          helperText: errors.quantity ?? 'Whole shares',
        })}
        {text('price', 'Price per share (₹)', { slotProps: { htmlInput: { inputMode: 'decimal' } } })}
        {text('charges', 'Charges (₹, optional)', { slotProps: { htmlInput: { inputMode: 'decimal' } } })}
      </Stack>
    </Stack>
  );
}
