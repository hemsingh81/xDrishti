import { Box, Typography } from '@mui/material';
import { numeric } from '@/theme';
import { formatSigned } from './format';
import { carriesNumber, resolveMetric, type MetricState } from './metric';
import { NonValue } from './NonValue';

export interface MetricValueProps {
  state: MetricState;
  /** How a number is written. Called only for `value` and `provisional`; the default writes a signed 2-decimal figure. */
  format?: (value: number) => string;
  /**
   * `gain-loss` colours a positive figure `success` and a negative one `error`, only for a real `value` whose printed
   * figure is not zero; the sign is in the text either way. A non-value is never coloured by it.
   */
  tone?: 'neutral' | 'gain-loss';
}

/** One metric in one of its honest states, inline. The only way the kit draws a number is through a `value` or `provisional`. */
export function MetricValue({ state, format = formatSigned, tone = 'neutral' }: MetricValueProps) {
  const resolved = resolveMetric(state);
  if (!carriesNumber(resolved)) {
    return <NonValue state={resolved} />;
  }
  const text = format(resolved.value);
  // Gain or loss is read from the printed figure: 0.001 that prints "0.00" is zero, so it is not green.
  const printsZero = !/[1-9]/.test(text);
  const color =
    resolved.kind === 'value' && tone === 'gain-loss' && !printsZero
      ? resolved.value > 0
        ? 'success.main'
        : 'error.main'
      : 'text.primary';
  return (
    <Box component="span" data-state={resolved.kind} sx={{ display: 'inline-flex', flexDirection: 'column' }}>
      <Typography component="span" variant="inherit" sx={{ color, fontWeight: 600, ...numeric }}>
        {text}
      </Typography>
      {resolved.kind === 'provisional' && <NonValue state={resolved} />}
    </Box>
  );
}
