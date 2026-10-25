import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { BarList, EvidenceBlock, NonValue, type BarRow, type MetricValueProps } from '@/shared/analytics';
import { groupsQuery, type GroupView } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

const labelOf = (lens: string, value: string | null, part: (dimension: string, value: string | null) => string) =>
  lens === 'sector'
    ? (value ?? 'Unclassified')
    : lens === 'hour' && value
      ? `${value}:00 to ${value}:59`
      : part(lens, value);

interface GroupBarsProps extends ReportPanelProps, Pick<MetricValueProps, 'format'> {
  title: string;
  lens: 'month' | 'basket' | 'weekday' | 'hour' | 'sector';
  figure: (group: GroupView) => GroupView['totalR'];
  diverging?: boolean;
  /** Orders the groups (the server's order when absent). */
  sort?: (a: GroupView, b: GroupView) => number;
}

/** One figure per group of a lens as a bar list: monthly total R, expectancy by basket, win rate by weekday. */
export function GroupBars({ query, min, title, lens, figure, format, diverging, sort }: GroupBarsProps) {
  const result = useQuery(groupsQuery(query, lens));
  const baskets = useBasketNames();
  return (
    <QueryPanel title={title} query={result}>
      {({ groups, nonAdditive, evidence }) => {
        const ordered = sort ? [...groups].sort(sort) : groups;
        const rows: BarRow[] = ordered.map((group) => ({
          id: group.key,
          label: labelOf(lens, group.parts[0]?.value ?? null, baskets.part),
          state: metricFromApi(figure(group), group.n, min),
          n: group.n,
        }));
        return (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            {rows.length === 0 ? (
              <NonValue state={{ kind: 'empty', reason: 'No trades in this selection.' }} />
            ) : (
              <BarList label={title} rows={rows} format={format} diverging={diverging} />
            )}
            {nonAdditive && (
              <Typography variant="body2" color="text.secondary">
                {NON_ADDITIVE_NOTE}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}
