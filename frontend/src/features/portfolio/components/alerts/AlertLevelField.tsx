import { Box, Chip, Stack, TextField, Typography } from '@mui/material';
import { useId, type Ref } from 'react';
import { formatIstDate, formatInrPrecise } from '@/shared/lib/format';
import type { AlertPrice } from '../../hooks/useAlertPrice';
import { presetLevel, presetPercents, type AlertDirection } from '../../lib/alerts';

interface AlertLevelFieldProps {
  value: string;
  onChange: (value: string) => void;
  direction: AlertDirection;
  price: AlertPrice;
  error: string | undefined;
  inputRef?: Ref<HTMLInputElement>;
}

/** The hint under the field: the last price the quick levels start from, and where it comes from. */
function priceHint(price: AlertPrice): string {
  if (price.price === null) return price.loading ? 'Looking up the last price…' : 'The last price is not known.';
  const amount = formatInrPrecise(price.price);
  return price.source === 'close' && price.at
    ? `Last close ${amount} on ${formatIstDate(price.at)}`
    : `Last price ${amount}`;
}

/** The price to be alerted at: a field with the last price beside it and quick levels around that price. */
export function AlertLevelField({ value, onChange, direction, price, error, inputRef }: AlertLevelFieldProps) {
  const hintId = useId();
  const below = direction === 'at_or_below';
  return (
    <Stack spacing={1}>
      <TextField
        label="Alert price (₹)"
        required
        fullWidth
        value={value}
        inputRef={inputRef}
        onChange={(event) => {
          onChange(event.target.value);
        }}
        error={error !== undefined}
        helperText={error}
        slotProps={{
          htmlInput: { inputMode: 'decimal', autoComplete: 'off', 'aria-describedby': hintId },
        }}
      />
      <Typography id={hintId} variant="body2" color="text.secondary" className="numeric">
        {priceHint(price)}
      </Typography>
      <Stack direction="row" role="group" aria-label="Quick levels" sx={{ flexWrap: 'wrap', gap: 1 }}>
        {presetPercents.map((percent) => {
          const level = price.price === null ? null : presetLevel(direction, price.price, percent);
          // A low price can round the level to nothing or back to the price itself: such a level means nothing.
          const useless =
            level !== null &&
            price.price !== null &&
            (level <= 0 || (below ? level >= price.price : level <= price.price));
          const text = `${below ? '−' : '+'}${String(percent)} %`;
          const words = `${String(percent)} % ${below ? 'below' : 'above'} the last price`;
          return (
            <Box
              key={percent}
              component="span"
              title={useless ? 'The price is too low for this step to make a different level.' : undefined}
            >
              <Chip
                clickable
                variant="outlined"
                disabled={level === null || useless}
                label={text}
                aria-label={level === null ? `${text} (${words})` : `${text}, ${formatInrPrecise(level)} (${words})`}
                onClick={() => {
                  if (level !== null) onChange(level.toFixed(2));
                }}
              />
            </Box>
          );
        })}
      </Stack>
    </Stack>
  );
}
