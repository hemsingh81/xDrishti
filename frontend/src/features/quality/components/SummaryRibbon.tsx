import { Box, Typography } from '@mui/material';
import type { QualityOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric, rem } from '@/theme';
import type { QualityStatus } from '../lib/filters';
import { limitsText, windowText } from '../lib/summary';
import { GateChip } from './GateChip';
import { StatusFilterChips } from './StatusFilterChips';

interface SummaryRibbonProps {
  overview: QualityOverview;
  status: QualityStatus | null;
  onStatusChange: (status: QualityStatus | null) => void;
}

function Score({ score }: { score: number | null }) {
  const tone =
    score === null ? 'text.primary' : score >= 90 ? 'success.main' : score < 70 ? 'error.main' : 'text.primary';
  return (
    <Box sx={{ display: 'inline-flex', alignItems: 'baseline', gap: 0.75 }}>
      <Typography variant="body2" color="text.secondary">
        Score
      </Typography>
      <Typography
        component="strong"
        sx={{ fontSize: rem(20), fontWeight: 700, lineHeight: 1, color: tone, ...numeric }}
      >
        {score === null ? '—' : formatNumber(Math.round(score))}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {score === null ? 'not scored yet' : 'of 100'}
      </Typography>
    </Box>
  );
}

/**
 * One wrapping row instead of five tiles and a gate card: the score, the instruments by result as filter chips, the
 * gate, and the window and limits the checks use.
 */
export function SummaryRibbon({ overview, status, onStatusChange }: SummaryRibbonProps) {
  return (
    <Box
      component="section"
      aria-label="Quality summary"
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        columnGap: 2,
        rowGap: 1,
        mb: 1,
        px: 1.5,
        py: 1,
        border: 1,
        borderColor: 'divider',
        borderRadius: 1,
        bgcolor: 'background.paper',
      }}
    >
      <Score score={overview.counts.score} />
      <StatusFilterChips counts={overview.counts} value={status} onChange={onStatusChange} />
      <GateChip gate={overview.gate} />
      <Box sx={{ ml: { lg: 'auto' }, minWidth: 0 }}>
        <Typography variant="body2" color="text.secondary" sx={numeric}>
          {windowText(overview)}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {limitsText(overview)}
        </Typography>
      </Box>
    </Box>
  );
}
