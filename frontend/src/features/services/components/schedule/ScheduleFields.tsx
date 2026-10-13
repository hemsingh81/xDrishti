import { Stack, TextField, Typography } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import {
  isScheduleType,
  scheduleTypeLabels,
  weekdayLong,
  weekdays,
  isWeekday,
  type ScheduleType,
} from '../../lib/labels';
import { followable } from '../../lib/followable';
import { everyRange, type FieldErrors, type ScheduleForm } from '../../lib/scheduleForm';
import { DayChips } from './DayChips';
import { NumberField } from './NumberField';
import { TimeField } from './TimeField';
import { TradingDaysSwitch } from './TradingDaysSwitch';

interface ScheduleFieldsProps {
  form: ScheduleForm;
  service: ServiceView;
  /** All services: the ones a run-after schedule can follow. */
  services: readonly ServiceView[];
  errors: FieldErrors;
  onChange: (patch: Partial<ScheduleForm>) => void;
}

/** The schedule type (only the types the service allows) and the fields that type needs. */
export function ScheduleFields({ form, service, services, errors, onChange }: ScheduleFieldsProps) {
  const types: ScheduleType[] = service.allowedScheduleTypes.filter(isScheduleType);
  if (!types.includes(form.type)) types.unshift(form.type);
  const others = followable(service, services);
  const days = (
    <DayChips
      days={form.days}
      locked={form.tradingDaysOnly}
      error={errors['schedule.days']}
      onChange={(next, tradingDaysOnly) => {
        onChange({ days: next, ...(tradingDaysOnly === undefined ? {} : { tradingDaysOnly }) });
      }}
    />
  );
  const tradingDays = (
    <TradingDaysSwitch
      checked={form.tradingDaysOnly}
      onChange={(tradingDaysOnly) => {
        onChange({ tradingDaysOnly });
      }}
    />
  );

  return (
    <Stack sx={{ gap: 2 }}>
      <TextField
        select
        label="Schedule type"
        value={form.type}
        error={Boolean(errors['schedule.type'])}
        helperText={errors['schedule.type']}
        onChange={(event) => {
          const type = event.target.value;
          if (!isScheduleType(type)) return;
          onChange({ type, ...(type === 'after' && form.after === '' ? { after: others[0]?.id ?? '' } : {}) });
        }}
        slotProps={{ select: { native: true } }}
        sx={{ maxWidth: 360 }}
      >
        {types.map((type) => (
          <option key={type} value={type}>
            {scheduleTypeLabels[type]}
          </option>
        ))}
      </TextField>

      {form.type === 'daily' && (
        <>
          <TimeField
            label="Time"
            value={form.time}
            error={errors['schedule.time']}
            onChange={(time) => {
              onChange({ time });
            }}
          />
          {days}
          {tradingDays}
        </>
      )}

      {form.type === 'weekly' && (
        <>
          <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap' }}>
            <TextField
              select
              label="Day"
              value={form.weekday}
              error={Boolean(errors['schedule.weekday'])}
              helperText={errors['schedule.weekday']}
              onChange={(event) => {
                if (isWeekday(event.target.value)) onChange({ weekday: event.target.value });
              }}
              slotProps={{ select: { native: true } }}
              sx={{ minWidth: 150 }}
            >
              {weekdays.map((day) => (
                <option key={day} value={day}>
                  {weekdayLong[day]}
                </option>
              ))}
            </TextField>
            <TimeField
              label="Time"
              value={form.time}
              error={errors['schedule.time']}
              onChange={(time) => {
                onChange({ time });
              }}
            />
          </Stack>
          {tradingDays}
        </>
      )}

      {form.type === 'after' && (
        <>
          <TextField
            select
            label="Run after"
            value={form.after}
            error={Boolean(errors['schedule.after'])}
            helperText={errors['schedule.after']}
            onChange={(event) => {
              onChange({ after: event.target.value });
            }}
            slotProps={{ select: { native: true } }}
            sx={{ maxWidth: 360 }}
          >
            {form.after === '' && <option value="">Choose a service</option>}
            {others.map((other) => (
              <option key={other.id} value={other.id}>
                {other.name}
              </option>
            ))}
          </TextField>
          <Typography variant="body2" color="text.secondary">
            Runs automatically each time that service finishes successfully.
          </Typography>
        </>
      )}

      {(form.type === 'window' || form.type === 'interval') && (
        <>
          <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap' }}>
            <TimeField
              label="From"
              value={form.from}
              error={errors['schedule.from']}
              onChange={(from) => {
                onChange({ from });
              }}
            />
            <TimeField
              label="To"
              value={form.to}
              error={errors['schedule.to']}
              onChange={(to) => {
                onChange({ to });
              }}
            />
            {form.type === 'interval' && (
              <NumberField
                label="Every (minutes)"
                value={form.everyMinutes}
                min={everyRange.min}
                max={everyRange.max}
                error={errors['schedule.everyMinutes']}
                onChange={(everyMinutes) => {
                  onChange({ everyMinutes });
                }}
              />
            )}
          </Stack>
          {days}
          {tradingDays}
        </>
      )}

      {form.type === 'continuous' && (
        <Typography variant="body2" color="text.secondary">
          Runs continuously (it is always up).
        </Typography>
      )}
    </Stack>
  );
}
