import { Box, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ColumnBars, EvidenceBlock, formatCount, NonValue, type ColumnBin } from '@/shared/analytics';
import { distributionQuery } from '../api/reportQueries';
import { edge } from '../lib/binLabel';
import { evidenceOf, groupEvidenceOf } from '../lib/evidence';
import { asNonValue, metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { MAX_CURVES } from './EquityByGroup';
import { QueryPanel } from './Panel';

const lenses = [
  ['', 'All trades'],
  ['strategy', 'By strategy'],
  ['side', 'By side'],
  ['timeframe', 'By timeframe'],
] as const;

/** R9: one histogram of R per group (bins of half an R, open at each end); counts, no smoothing, no fitted curve. */
export function DistributionReport({ query, min }: ReportPanelProps) {
  const [lens, setLens] = useState('');
  const result = useQuery(distributionQuery(query, lens || undefined));
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <TextField
        select
        size="small"
        label="Group by"
        value={lens}
        onChange={(event) => {
          setLens(event.target.value);
        }}
        sx={{ maxWidth: 240 }}
      >
        {lenses.map(([value, label]) => (
          <MenuItem key={value} value={value}>
            {label}
          </MenuItem>
        ))}
      </TextField>
      <QueryPanel title="R distribution by group" query={result}>
        {({ groups, evidence }) => (
          <Box sx={{ display: 'grid', gap: 2 }}>
            {groups.length === 0 && <NonValue state={{ kind: 'empty', reason: 'No trades in this selection.' }} />}
            {groups.slice(0, MAX_CURVES).map((group) => {
              const name = group.parts.map((part) => part.value ?? 'Not recorded').join(' · ') || 'All trades';
              const bins: ColumnBin[] = group.bins.map((bin, index) => ({
                id: String(index),
                label: edge(bin.lowerR, bin.upperR),
                count: bin.count,
              }));
              return (
                <Box key={group.key} component="section" aria-label={name} sx={{ display: 'grid', gap: 1 }}>
                  <Typography variant="h3" component="h3" sx={{ fontSize: 'body1.fontSize' }}>
                    {name}
                  </Typography>
                  {bins.length === 0 ? (
                    <NonValue state={asNonValue(metricFromApi(group.meanR, group.n, min))} />
                  ) : (
                    <>
                      <Typography variant="body2" color="text.secondary">
                        {`${formatCount(group.n)} trades in bins of half an R.`}
                      </Typography>
                      <ColumnBars label={`R distribution, ${name}`} bins={bins} binHeader="R" />
                    </>
                  )}
                  <EvidenceBlock {...groupEvidenceOf(group.evidence, evidence)} compact />
                </Box>
              );
            })}
            {groups.length > MAX_CURVES && (
              <Typography variant="body2" color="text.secondary">
                {`Shown: the first ${String(MAX_CURVES)} of ${String(groups.length)} groups.`}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        )}
      </QueryPanel>
    </Box>
  );
}
