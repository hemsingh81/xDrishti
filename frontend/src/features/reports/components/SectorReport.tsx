import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { BarList, EvidenceBlock, formatCount, formatR, NonValue, type BarRow } from '@/shared/analytics';
import { groupsQuery, type GroupView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { GroupBars } from './GroupBars';
import { QueryPanel } from './Panel';

const TOP = 10;

/** The ten best and ten worst instruments by total R, among those that reach the minimum; the rest are not ranked here. */
function Symbols({ query, min }: ReportPanelProps) {
  const result = useQuery(groupsQuery(query, 'instrument'));
  return (
    <QueryPanel title="Top and bottom instruments by total R" query={result}>
      {({ groups, evidence }) => {
        const row = (g: GroupView): BarRow => ({
          id: g.key,
          label: g.parts[0]?.value ?? 'Not recorded',
          state: metricFromApi(g.totalR, g.n, min),
          n: g.n,
        });
        const ranked = groups
          .filter((g) => g.totalR.state === 'value' && typeof g.totalR.number === 'number')
          .sort((a, b) => (b.totalR.number ?? 0) - (a.totalR.number ?? 0));
        const top = ranked.slice(0, TOP);
        const bottom = ranked.slice(TOP).slice(-TOP);
        const thin = groups.length - ranked.length;
        return (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            {ranked.length === 0 ? (
              <NonValue state={{ kind: 'empty', reason: 'No instrument has enough trades to be ranked.' }} />
            ) : (
              <BarList label="Instruments by total R" rows={[...top, ...bottom].map(row)} format={formatR} />
            )}
            {thin > 0 && (
              <Typography variant="body2" color="text.secondary">
                {`${formatCount(thin)} instruments have too few trades and are not ranked here.`}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

/** R11: sectors by the lower bound of expectancy, then instruments by total R. Sector is the master's sector as of today. */
export function SectorReport(props: ReportPanelProps) {
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <GroupBars
        {...props}
        title="Expectancy lower bound by sector"
        lens="sector"
        figure={(g) => g.expectancyLowerR}
        format={formatR}
      />
      <Symbols {...props} />
      <Typography variant="body2" color="text.secondary">
        Sector as of today, not at the time of the trade; an instrument without one is Unclassified and has no figure.
        Ranked views look at many groups and the best looks better than it is: a selection effect, not a forecast.
      </Typography>
    </Box>
  );
}
