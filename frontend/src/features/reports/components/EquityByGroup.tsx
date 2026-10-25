import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { carriesNumber, EvidenceBlock, formatR, NonValue } from '@/shared/analytics';
import { TimeSeriesChart, type ChartColor, type ChartDash, type ChartSeries } from '@/shared/ui/chart';
import { equityQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { asNonValue, metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

/** The most curves the screen draws; the server allows more (40), the rest are not drawn and the page says so. */
export const MAX_CURVES = 12;
const colours: readonly ChartColor[] = ['accent', 'info', 'good', 'warn', 'bad', 'neutral'];
const dashes: readonly ChartDash[] = ['solid', 'dashed'];

interface EquityByGroupProps extends ReportPanelProps {
  title: string;
  lens: string;
}

/** Running R of each group of a lens as one line each (at most 12). A group too small for a curve is listed with its reason. */
export function EquityByGroup({ query, min, title, lens }: EquityByGroupProps) {
  const result = useQuery(equityQuery(query, lens));
  return (
    <QueryPanel title={title} query={result}>
      {({ series, evidence }) => {
        const label = (key: string, parts: readonly { value: string | null }[]) => parts[0]?.value ?? key;
        const drawable = series.filter((s) => carriesNumber(metricFromApi(s.totalR, s.n, min)) && s.points.length > 0);
        const rest = series.filter((s) => !drawable.includes(s));
        const chart: ChartSeries[] = drawable.slice(0, MAX_CURVES).map((s, i) => ({
          id: s.key,
          name: label(s.key, s.parts),
          color: colours[i % colours.length] ?? 'accent',
          dash: dashes[Math.floor(i / colours.length) % dashes.length],
          data: s.points.map((p) => [p.date, p.cumulativeR]),
        }));
        return (
          <Box sx={{ display: 'grid', gap: 1 }}>
            {chart.length === 0 ? (
              <NonValue state={{ kind: 'empty', reason: 'No group has enough trades for a curve.' }} />
            ) : (
              <TimeSeriesChart label={`${title} in R`} series={chart} valueFormatter={formatR} height={260} />
            )}
            {drawable.length > MAX_CURVES && (
              <Typography variant="body2" color="text.secondary">
                {`Drawn: the first ${String(MAX_CURVES)} of ${String(drawable.length)} curves.`}
              </Typography>
            )}
            {rest.length > 0 && (
              <Box component="ul" aria-label="Groups without a curve" sx={{ m: 0, pl: 2.5 }}>
                {rest.map((s) => {
                  const state = asNonValue(metricFromApi(s.totalR, s.n, min));
                  return (
                    <li key={s.key}>
                      <Typography variant="body2">
                        {`${label(s.key, s.parts)}: ${String(s.n)} trades, ${state.kind === 'insufficient' ? 'too few for a curve' : 'reason' in state ? (state.reason ?? state.kind) : state.kind}`}
                      </Typography>
                    </li>
                  );
                })}
              </Box>
            )}
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}
