import { Box, List, ListItem, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

const num = (v: number | null): string => (v === null ? 'n/a' : formatNumber(v));

/** The robustness block of a robust test: sensitivity per parameter, Monte Carlo, by year, the chance baseline. */
export function RobustnessSummary({ robustness: r }: { robustness: Schemas['RobustnessView'] }) {
  return (
    <Box component="section" aria-label="Robustness">
      <Typography variant="subtitle2">Robustness (in-sample exploration)</Typography>
      <List dense disablePadding aria-label="Parameter sensitivity">
        {r.sensitivity.parameters.map((p) => (
          <ListItem key={p.param} disableGutters>
            {p.param}: {p.verdict} - {p.reason}
          </ListItem>
        ))}
      </List>
      {r.sensitivity.label && (
        <Typography variant="caption" color="text.secondary" component="p">
          {r.sensitivity.label}
        </Typography>
      )}
      {r.monteCarlo && (
        <Typography>
          Monte Carlo ({r.monteCarlo.draws} draws): mean R between {num(r.monteCarlo.meanP05)} and{' '}
          {num(r.monteCarlo.meanP95)} (5th to 95th percentile); {formatNumber(r.monteCarlo.shareNotPositive * 100)}% of
          draws without an edge.
        </Typography>
      )}
      {r.signFlipBaseline && (
        <Typography>
          Chance baseline (sign flip): p = {num(r.signFlipBaseline.pValue)}. {r.signFlipBaseline.label}
        </Typography>
      )}
      {r.byYear.length > 0 && (
        <List dense disablePadding aria-label="Results by year">
          {r.byYear.map((y) => (
            <ListItem key={y.year} disableGutters>
              {y.year}: {y.trades} trades, mean {num(y.meanR)} R, total {num(y.totalR)} R (
              {y.positive ? 'positive' : 'not positive'})
            </ListItem>
          ))}
        </List>
      )}
      {r.limits.map((l) => (
        <Typography key={l} variant="caption" color="text.secondary" component="p">
          {l}
        </Typography>
      ))}
    </Box>
  );
}
