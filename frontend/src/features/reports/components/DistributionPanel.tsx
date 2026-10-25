import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ColumnBars, EvidenceBlock, formatCount, formatSigned, NonValue, type ColumnBin } from '@/shared/analytics';
import { distributionQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { asNonValue, metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

const edge = (lower: number | null, upper: number | null): string =>
  lower === null
    ? `below ${formatSigned(upper)}`
    : upper === null
      ? `${formatSigned(lower)} and above`
      : `${formatSigned(lower)} to ${formatSigned(upper)}`;

/** How many trades ended at each R (bins of half an R; an open bin at each end). A group too small for bins says why instead. */
export function DistributionPanel({ query, min }: ReportPanelProps) {
  const result = useQuery(distributionQuery(query));
  return (
    <QueryPanel title="R distribution" query={result}>
      {({ groups, evidence }) => {
        const all = groups[0];
        if (!all || all.bins.length === 0) {
          return <NonValue state={all ? asNonValue(metricFromApi(all.meanR, all.n, min)) : { kind: 'empty' }} />;
        }
        const bins: ColumnBin[] = all.bins.map((bin, index) => ({
          id: String(index),
          label: edge(bin.lowerR, bin.upperR),
          count: bin.count,
        }));
        return (
          <Box sx={{ display: 'grid', gap: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {`${formatCount(all.n)} trades in ${formatCount(bins.length)} bins of half an R.`}
            </Typography>
            <ColumnBars label="R distribution" bins={bins} binHeader="R" />
            <EvidenceBlock {...evidenceOf(evidence, { n: all.n })} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}
