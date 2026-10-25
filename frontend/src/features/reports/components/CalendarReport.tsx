import { Box, Typography } from '@mui/material';
import { formatR } from '@/shared/analytics';
import type { ReportPanelProps } from '../lib/reportFilter';
import { GroupBars } from './GroupBars';

const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const at = (value: string | undefined) => {
  const index = weekdays.indexOf(value ?? '');
  return index < 0 ? weekdays.length : index;
};

/** R10: expectancy by weekday and by entry hour, total R by month. Bars only: no calendar heatmap in this slice. */
export function CalendarReport(props: ReportPanelProps) {
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <GroupBars
        {...props}
        title="Expectancy by weekday"
        lens="weekday"
        figure={(g) => g.expectancyR}
        format={formatR}
        sort={(a, b) => at(a.parts[0]?.value ?? undefined) - at(b.parts[0]?.value ?? undefined)}
      />
      <GroupBars
        {...props}
        title="Expectancy by entry hour, intraday trades only"
        lens="hour"
        figure={(g) => g.expectancyR}
        format={formatR}
      />
      <GroupBars {...props} title="Total R by month" lens="month" figure={(g) => g.totalR} format={formatR} />
      <Typography variant="body2" color="text.secondary">
        Hours are India time of the entry fill and exist only for intraday runs; a swing trade has no hour. The hour and
        the weekday reflect each strategy&apos;s entry rules (an opening-range strategy enters early by construction).
        Descriptive, not a ranking.
      </Typography>
    </Box>
  );
}
