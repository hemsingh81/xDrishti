import { FormControlLabel, FormHelperText, Stack, Switch } from '@mui/material';
import { type FieldErrors, retriesRange, timeoutRange, type ScheduleForm } from '../lib/scheduleForm';
import { NumberField } from './schedule/NumberField';

interface ExecutionFieldsProps {
  form: ScheduleForm;
  errors: FieldErrors;
  onChange: (patch: Partial<ScheduleForm>) => void;
}

/** How the service runs: retries, timeout, catch-up of a missed run, and the notice in the bell when it fails. */
export function ExecutionFields({ form, errors, onChange }: ExecutionFieldsProps) {
  return (
    <Stack sx={{ gap: 1 }}>
      <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap' }}>
        <NumberField
          label="Retries"
          value={form.retries}
          min={retriesRange.min}
          max={retriesRange.max}
          help="After a failed attempt"
          error={errors.retries}
          onChange={(retries) => {
            onChange({ retries });
          }}
        />
        <NumberField
          label="Timeout (min)"
          value={form.timeoutMinutes}
          min={timeoutRange.min}
          max={timeoutRange.max}
          help="Per attempt"
          error={errors.timeoutMinutes}
          onChange={(timeoutMinutes) => {
            onChange({ timeoutMinutes });
          }}
        />
      </Stack>
      <FormControlLabel
        control={
          <Switch
            checked={form.catchUp}
            onChange={(event) => {
              onChange({ catchUp: event.target.checked });
            }}
          />
        }
        label="Catch up missed runs"
      />
      <FormHelperText sx={{ mt: -0.5, ml: 6 }}>
        When the application was not running at the scheduled time, run once as soon as it is back.
      </FormHelperText>
      <FormControlLabel
        control={
          <Switch
            checked={form.notifyOnFailure}
            onChange={(event) => {
              onChange({ notifyOnFailure: event.target.checked });
            }}
          />
        }
        label="Notify on failure"
      />
      <FormHelperText sx={{ mt: -0.5, ml: 6 }}>
        A failed run shows in the notification bell until the next run succeeds.
      </FormHelperText>
    </Stack>
  );
}
