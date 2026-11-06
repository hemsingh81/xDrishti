import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount, formatRate } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { loopQuery } from '../api/confidenceQueries';
import { formatFine } from '../lib/figure';
import { LoopArmsPanel } from './LoopArmsPanel';
import { CurvePanel, LiftPanel, SlopePanel } from './LoopSectionPanels';
import { Panel } from './Panel';

/**
 * Loop (CF-12 12c-2), read only: is the learning loop helping? The three arms, the learning curve, the reliability slope and
 * the decision lift. A section whose input has no source says "Not available" and why; nothing is shown as 0, an empty chart
 * or a pass. No order exists.
 */
export function LoopTab() {
  const query = useQuery(loopQuery);
  if (query.isPending) return <LoadingState label="Loading the loop report…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const report = query.data;
  const s = report.settings;
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        {report.line}
      </Typography>
      <LoopArmsPanel arms={report.arms} />
      <CurvePanel curve={report.curve} />
      <SlopePanel slope={report.slope} />
      <LiftPanel lift={report.lift} />
      <Panel title="Settings and notes">
        <Facts
          items={[
            { label: 'Fewest distinct days', value: formatCount(s.minDays) },
            { label: 'Bootstrap draws', value: formatCount(s.draws) },
            { label: 'Interval coverage', value: formatRate(s.level) },
            { label: '"Helps" needs P(better) of', value: formatRate(s.helpsAt) },
            { label: 'Flat-curve limit per 50 rows', value: formatFine(s.minSlopePer50Rows) },
            { label: 'Reliability window (outcomes)', value: formatCount(s.slopeWindow) },
            { label: 'Slope band', value: `${formatFine(s.slopeLow)} to ${formatFine(s.slopeHigh)}` },
            { label: 'Settings hash', value: s.settingsHash },
          ]}
        />
        {report.statements.length > 0 && (
          <Box component="ul" aria-label="What to keep in mind" sx={{ mt: 1.5, mb: 0, pl: 3 }}>
            {report.statements.map((sentence) => (
              <Typography key={sentence} component="li" variant="body2" color="text.secondary">
                {sentence}
              </Typography>
            ))}
          </Box>
        )}
      </Panel>
    </Box>
  );
}
