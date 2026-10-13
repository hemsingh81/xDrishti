import CheckIcon from '@mui/icons-material/Check';
import { Box, Button, Chip, FormHelperText, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import { tradingWeekdays, weekdayLong, weekdays, weekdayShort, type Weekday } from '../../lib/labels';

interface DayChipsProps {
  days: readonly Weekday[];
  /** "Trading days only" is on: the days are Monday to Friday, whatever the chips say. */
  locked: boolean;
  error?: string;
  /** Trading days sets Monday to Friday and switches "only on trading days" on; Every day switches it off. */
  onChange: (days: readonly Weekday[], tradingDaysOnly: boolean | undefined) => void;
}

/** The days of the week as toggle chips with the shortcuts "Trading days" and "Every day". */
export function DayChips({ days, locked, error, onChange }: DayChipsProps) {
  const labelId = useId();
  const shown = locked ? tradingWeekdays : days;
  return (
    <Box>
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography id={labelId} variant="subtitle2">
          Days
        </Typography>
        <Box role="group" aria-labelledby={labelId} sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
          {weekdays.map((day) => {
            const on = shown.includes(day);
            return (
              <Chip
                key={day}
                size="small"
                label={weekdayShort[day]}
                aria-label={weekdayLong[day]}
                aria-pressed={on}
                clickable
                disabled={locked}
                color={on ? 'primary' : 'default'}
                variant={on ? 'filled' : 'outlined'}
                icon={on ? <CheckIcon /> : undefined}
                onClick={() => {
                  onChange(
                    on ? days.filter((d) => d !== day) : weekdays.filter((d) => d === day || days.includes(d)),
                    undefined,
                  );
                }}
              />
            );
          })}
        </Box>
        <Button
          size="small"
          onClick={() => {
            onChange(tradingWeekdays, true);
          }}
        >
          Trading days
        </Button>
        <Button
          size="small"
          onClick={() => {
            onChange(weekdays, false);
          }}
        >
          Every day
        </Button>
      </Stack>
      {error && <FormHelperText error>{error}</FormHelperText>}
    </Box>
  );
}
