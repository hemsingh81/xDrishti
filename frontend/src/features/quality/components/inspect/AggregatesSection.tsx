import { Box, Chip, FormControlLabel, MenuItem, Stack, Switch, TextField, Typography } from '@mui/material';
import type { AggregatesCheck, QualityBuckets } from '@/shared/api';
import { formatIstDayAndTime, formatNumber } from '@/shared/lib/format';
import { stateAlpha } from '@/theme';
import type { DetailOptions } from '../../api/qualityQueries';
import { aggregateTimeframes, checkTitles, isAggregateTimeframe } from '../../lib/labels';
import { BucketsTable } from './BucketsTable';
import { CheckSection } from './CheckSection';

interface AggregatesSectionProps {
  check: AggregatesCheck;
  buckets: QualityBuckets;
  options: DetailOptions;
  onOptionsChange: (options: DetailOptions) => void;
  /** The table is being reloaded for new options; the old rows stay, dimmed. */
  loading: boolean;
}

const all = 'all';

/** Aggregates against the 1-minute bars: the result per timeframe and the stored-vs-recomputed table. */
export function AggregatesSection({ check, buckets, options, onOptionsChange, loading }: AggregatesSectionProps) {
  return (
    <CheckSection title={checkTitles.aggregates} status={check.status}>
      <Typography>
        {formatNumber(check.compared)} buckets compared with the ones recomputed from the 1-minute bars;{' '}
        {check.differences === 0 ? 'none differs' : `${formatNumber(check.differences)} differ`}.
      </Typography>
      <Stack
        direction="row"
        component="ul"
        aria-label="Aggregates by timeframe"
        sx={{ m: 0, p: 0, gap: 0.75, flexWrap: 'wrap' }}
      >
        {aggregateTimeframes.map((tf) => {
          const result = check.byTimeframe[tf];
          if (!result) return null;
          return (
            <Box component="li" key={tf} sx={{ listStyle: 'none' }}>
              <Chip
                size="small"
                variant="outlined"
                color={result.differences > 0 ? 'error' : 'success'}
                label={`${tf}: ${result.differences > 0 ? `${formatNumber(result.differences)} of ${formatNumber(result.compared)} differ` : 'matches'}`}
              />
            </Box>
          );
        })}
      </Stack>

      <Typography variant="h4" sx={{ pt: 1 }}>
        Stored vs recomputed
      </Typography>
      <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          select
          size="small"
          label="Timeframe"
          value={options.timeframe ?? all}
          onChange={(event) => {
            const value = event.target.value;
            onOptionsChange({ ...options, timeframe: isAggregateTimeframe(value) ? value : null });
          }}
          sx={{ minWidth: 180 }}
        >
          <MenuItem value={all}>All timeframes</MenuItem>
          {aggregateTimeframes.map((tf) => (
            <MenuItem key={tf} value={tf}>
              {tf}
            </MenuItem>
          ))}
        </TextField>
        <FormControlLabel
          label="Differences only"
          control={
            <Switch
              checked={options.differencesOnly}
              onChange={(event) => {
                onOptionsChange({ ...options, differencesOnly: event.target.checked });
              }}
            />
          }
        />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        Computed now, {formatIstDayAndTime(buckets.computedAt, { weekday: false })} IST:{' '}
        {formatNumber(buckets.differences)} of {formatNumber(buckets.total)} buckets differ. Stored values that differ
        are struck through, the recomputed value follows in bold.
      </Typography>
      <Box aria-busy={loading} sx={{ opacity: loading ? stateAlpha.busy : 1 }}>
        {buckets.items.length === 0 ? (
          <Typography>{options.differencesOnly ? 'No differences.' : 'No buckets in the window.'}</Typography>
        ) : (
          <BucketsTable items={buckets.items} />
        )}
      </Box>
      {buckets.truncated && (
        <Typography variant="body2" color="text.secondary">
          The list is cut at {formatNumber(buckets.items.length)} buckets; narrow it by timeframe.
        </Typography>
      )}
    </CheckSection>
  );
}
