import { TableCell, Typography } from '@mui/material';
import { numeric } from '@/theme';
import { formatCount } from './format';
import { carriesNumber, resolveMetric } from './metric';
import { MetricValue, type MetricValueProps } from './MetricValue';

export interface MetricCellProps extends MetricValueProps {
  /** Observations behind the figure, printed small under it ("n 42"); only beside a figure, a non-value says its own n. */
  n?: number | null;
}

/** A table cell holding one metric: right-aligned, tabular figures, the same six honest states as `MetricValue`. */
export function MetricCell({ n, ...metric }: MetricCellProps) {
  return (
    <TableCell align="right" sx={numeric}>
      <MetricValue {...metric} />
      {carriesNumber(resolveMetric(metric.state)) && typeof n === 'number' && (
        <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block', ...numeric }}>
          n {formatCount(n)}
        </Typography>
      )}
    </TableCell>
  );
}
