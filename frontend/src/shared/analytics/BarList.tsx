import { Box, Typography } from '@mui/material';
import { numeric } from '@/theme';
import { formatCount } from './format';
import { carriesNumber, resolveMetric, type MetricState } from './metric';
import { MetricValue, type MetricValueProps } from './MetricValue';

export interface BarRow {
  id: string;
  label: string;
  state: MetricState;
  /** Observations behind the figure. */
  n?: number | null;
}

interface BarListProps extends Pick<MetricValueProps, 'format'> {
  /** Names the list. */
  label: string;
  rows: readonly BarRow[];
  /** `true` (default): bars grow right for a gain and left for a loss from a centre line. `false`: bars grow from the left. */
  diverging?: boolean;
}

/**
 * Horizontal bars, one `<li>` per row: the label, a decorative bar, the figure as text and n. The bar only repeats the text
 * (it is `aria-hidden`); a row that is not a value prints its state (icon, word, reason) and draws no bar, so there is never a
 * zero-length bar standing for "no data".
 */
export function BarList({ label, rows, format, diverging = true }: BarListProps) {
  const scale = Math.max(
    0,
    ...rows.map((row) => {
      const state = resolveMetric(row.state);
      return carriesNumber(state) ? Math.abs(state.value) : 0;
    }),
  );
  return (
    <Box
      component="ul"
      aria-label={label}
      sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1.25, minWidth: 0 }}
    >
      {rows.map((row) => {
        const state = resolveMetric(row.state);
        const share = carriesNumber(state) && scale > 0 ? Math.abs(state.value) / scale : 0;
        const negative = carriesNumber(state) && state.value < 0;
        return (
          <Box
            component="li"
            key={row.id}
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: 'minmax(0, 1fr) minmax(0, 1.4fr)', sm: '140px minmax(0, 1fr) 150px' },
              alignItems: 'center',
              columnGap: 1.5,
              rowGap: 0.25,
              minWidth: 0,
            }}
          >
            <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
              {row.label}
            </Typography>
            {carriesNumber(state) ? (
              <>
                <Box
                  aria-hidden
                  sx={{
                    position: 'relative',
                    height: 12,
                    borderRadius: 0.5,
                    backgroundColor: 'action.hover',
                  }}
                >
                  <Box
                    sx={{
                      position: 'absolute',
                      top: 0,
                      bottom: 0,
                      borderRadius: 0.5,
                      width: `${(diverging ? share * 50 : share * 100).toFixed(1)}%`,
                      left: diverging ? (negative ? `${(50 - share * 50).toFixed(1)}%` : '50%') : 0,
                      backgroundColor: diverging ? (negative ? 'error.main' : 'success.main') : 'primary.main',
                    }}
                  />
                  {diverging && (
                    <Box
                      sx={{
                        position: 'absolute',
                        top: -2,
                        bottom: -2,
                        left: '50%',
                        // 1px: a bare `1` is 100 % in MUI's sx, which drew a track-wide band from the centre and overflowed the card.
                        width: '1px',
                        backgroundColor: 'divider',
                      }}
                    />
                  )}
                </Box>
                <Box
                  sx={{
                    gridColumn: { xs: '1 / span 2', sm: 3 },
                    gridRow: { sm: 1 },
                    display: 'flex',
                    gap: 1,
                    alignItems: 'baseline',
                    ...numeric,
                  }}
                >
                  <MetricValue state={state} format={format} tone={diverging ? 'gain-loss' : 'neutral'} />
                  {typeof row.n === 'number' && (
                    <Typography component="span" variant="caption" color="text.secondary" sx={numeric}>
                      n {formatCount(row.n)}
                    </Typography>
                  )}
                </Box>
              </>
            ) : (
              <Box sx={{ gridColumn: { sm: '2 / span 2' } }}>
                <MetricValue state={state} />
                {typeof row.n === 'number' && state.kind !== 'insufficient' && (
                  <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1, ...numeric }}>
                    n {formatCount(row.n)}
                  </Typography>
                )}
              </Box>
            )}
          </Box>
        );
      })}
    </Box>
  );
}
