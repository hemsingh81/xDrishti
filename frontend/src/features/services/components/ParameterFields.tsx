import { FormControlLabel, FormHelperText, Stack, Switch, Typography } from '@mui/material';
import type { ServiceParameter } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { FieldErrors, ScheduleForm } from '../lib/scheduleForm';
import { NumberField } from './schedule/NumberField';

interface ParameterFieldsProps {
  parameters: readonly ServiceParameter[];
  form: ScheduleForm;
  errors: FieldErrors;
  onChange: (name: string, value: string | boolean) => void;
}

const helpOf = (p: ServiceParameter): string => {
  const range = `${formatNumber(p.min)}–${formatNumber(p.max)}${p.unit ? ` ${p.unit}` : ''}`;
  return p.description ? `${p.description} (${range})` : range;
};

/** The service's own settings, from the schema it declares: numbers with their bounds, and switches. */
export function ParameterFields({ parameters, form, errors, onChange }: ParameterFieldsProps) {
  if (parameters.length === 0) return null;
  return (
    <Stack sx={{ gap: 1.5 }} role="group" aria-labelledby="service-parameters">
      <Typography id="service-parameters" variant="subtitle2">
        Parameters
      </Typography>
      <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap' }}>
        {parameters
          .filter((p) => p.kind !== 'boolean')
          .map((p) => (
            <NumberField
              key={p.name}
              label={p.label}
              value={String(form.parameters[p.name] ?? '')}
              min={p.min}
              max={p.max}
              step={p.kind === 'integer' ? 1 : 'any'}
              help={helpOf(p)}
              error={errors[`parameters.${p.name}`]}
              onChange={(value) => {
                onChange(p.name, value);
              }}
            />
          ))}
      </Stack>
      {parameters
        .filter((p) => p.kind === 'boolean')
        .map((p) => (
          <Stack key={p.name}>
            <FormControlLabel
              control={
                <Switch
                  checked={form.parameters[p.name] === true}
                  onChange={(event) => {
                    onChange(p.name, event.target.checked);
                  }}
                />
              }
              label={p.label}
            />
            {p.description && <FormHelperText sx={{ mt: -0.5, ml: 6 }}>{p.description}</FormHelperText>}
          </Stack>
        ))}
    </Stack>
  );
}
