import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { EvidenceBlock, NonValue } from '@/shared/analytics';
import { insightsQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

/** The eight server sentences, in order, as the server wrote them. One that states nothing says why, with its state's word. */
export function InsightsList({ query }: ReportPanelProps) {
  const result = useQuery(insightsQuery(query));
  return (
    <QueryPanel title="Auto-insights" query={result}>
      {({ insights, evidence }) => (
        <>
          <Box component="ol" aria-label="Insights" sx={{ m: 0, pl: 2.5, display: 'grid', gap: 1 }}>
            {insights.map((insight) => (
              <li key={insight.kind}>
                {insight.state === 'value' ? (
                  <Typography variant="body2">{insight.text}</Typography>
                ) : (
                  <NonValue
                    state={{
                      kind: insight.state === 'not_evaluable' ? 'not_evaluable' : 'unavailable',
                      reason: insight.text,
                    }}
                  />
                )}
              </li>
            ))}
          </Box>
          <Box sx={{ mt: 1.5 }}>
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        </>
      )}
    </QueryPanel>
  );
}
