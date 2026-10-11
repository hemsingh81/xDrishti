import {
  FormControl,
  FormControlLabel,
  FormHelperText,
  FormLabel,
  Radio,
  RadioGroup,
  Stack,
  TextField,
} from '@mui/material';
import { useId } from 'react';
import type { FieldErrors } from '../lib/fieldErrors';
import type { AutoRefreshState, RefreshMode } from '../lib/autoRefresh';

interface AutoRefreshSettingsProps {
  /** Prefix for element ids, so two instances on one page do not clash. */
  idPrefix: string;
  value: AutoRefreshState;
  /** Messages keyed `refreshMode`, `refreshDailyTimeIst`. */
  errors?: FieldErrors;
  disabled?: boolean;
  onChange: (patch: Partial<AutoRefreshState>) => void;
}

/** How an account's holdings refresh by themselves: Off, or once a day at a time (India time). */
export function AutoRefreshSettings({
  idPrefix,
  value,
  errors = {},
  disabled = false,
  onChange,
}: AutoRefreshSettingsProps) {
  const labelId = useId();
  const helpId = useId();
  const timeError = errors.refreshDailyTimeIst;

  return (
    <FormControl component="fieldset" error={Boolean(errors.refreshMode)} disabled={disabled} sx={{ width: '100%' }}>
      <FormLabel component="legend" id={labelId} sx={{ fontWeight: 600, color: 'text.primary' }}>
        Auto refresh
      </FormLabel>
      <RadioGroup
        aria-labelledby={labelId}
        aria-describedby={helpId}
        value={value.mode}
        onChange={(event) => {
          onChange({ mode: event.target.value as RefreshMode });
        }}
      >
        <FormControlLabel value="off" control={<Radio />} label="Off" />
        <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', columnGap: 1 }}>
          <FormControlLabel value="daily" control={<Radio />} label="Daily at" sx={{ mr: 0 }} />
          <TextField
            type="time"
            size="small"
            id={`${idPrefix}-time`}
            label="Time of day (IST)"
            value={value.dailyTime}
            disabled={disabled || value.mode !== 'daily'}
            error={Boolean(timeError)}
            helperText={timeError}
            onChange={(event) => {
              onChange({ dailyTime: event.target.value });
            }}
            slotProps={{ inputLabel: { shrink: true } }}
          />
          <span>IST</span>
        </Stack>
      </RadioGroup>
      <FormHelperText id={helpId} error={Boolean(errors.refreshMode)}>
        {errors.refreshMode ?? 'A daily refresh runs once a day, and catches up once if it was missed.'}
      </FormHelperText>
    </FormControl>
  );
}
