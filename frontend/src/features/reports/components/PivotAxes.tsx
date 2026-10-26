import { Box, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { dimensionReasonQuery } from '../api/reportQueries';
import {
  pivotDimensions,
  pivotMetrics,
  refusedDimensions,
  type PivotDimension,
  type PivotMetric,
} from '../lib/pivotLimits';

interface PivotAxesProps {
  rows: PivotDimension;
  columns: PivotDimension;
  metric: PivotMetric;
  set: (patch: Record<string, string | null>) => void;
}

function Reason({ dimension }: { dimension: (typeof refusedDimensions)[number] }) {
  const reason = useQuery(dimensionReasonQuery(dimension));
  return (
    <Typography component="li" variant="body2" color="text.secondary">
      {`${dimension === 'regime' ? 'Regime' : 'Account'} (not offered): ${reason.data ?? 'not available.'}`}
    </Typography>
  );
}

/** Row, column and metric. The other axis' dimension is disabled in each select, so the same one cannot be chosen twice. */
export function PivotAxes({ rows, columns, metric, set }: PivotAxesProps) {
  const axis = (name: 'rows' | 'columns', label: string, value: PivotDimension, other: PivotDimension) => (
    <TextField
      select
      size="small"
      label={label}
      value={value}
      onChange={(event) => {
        set({ [name]: event.target.value });
      }}
      sx={{ minWidth: 160 }}
    >
      {pivotDimensions.map((d) => (
        <MenuItem key={d.id} value={d.id} disabled={d.id === other} sx={{ minHeight: 44 }}>
          {d.label}
        </MenuItem>
      ))}
    </TextField>
  );
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5 }}>
        {axis('rows', 'Rows', rows, columns)}
        {axis('columns', 'Columns', columns, rows)}
        <TextField
          select
          size="small"
          label="Metric"
          value={metric}
          onChange={(event) => {
            set({ metric: event.target.value });
          }}
          sx={{ minWidth: 220 }}
        >
          {pivotMetrics.map((m) => (
            <MenuItem key={m.id} value={m.id} sx={{ minHeight: 44 }}>
              {m.label}
            </MenuItem>
          ))}
        </TextField>
      </Box>
      <Box component="ul" aria-label="Dimensions not offered" sx={{ m: 0, pl: 2.5 }}>
        {refusedDimensions.map((d) => (
          <Reason key={d} dimension={d} />
        ))}
        <Typography component="li" variant="body2" color="text.secondary">
          Decisions are plan tickets: see Learning, Decisions
        </Typography>
      </Box>
    </Box>
  );
}
