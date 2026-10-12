import { useId } from 'react';
import { Box, Card, CardContent, Stack, Typography } from '@mui/material';
import type { DataOverview } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { currentPhase, phaseTitles, type PhaseKey } from '../lib/phases';
import { AggregatesStep } from './AggregatesStep';
import { CsvStep } from './CsvStep';
import { DhanStep } from './DhanStep';

const doing: Readonly<Record<PhaseKey, string>> = {
  csv: 'loading the CSV files',
  dhan: 'fetching from the Dhan Data API',
  aggregate: 'building the aggregates',
};

function liveLine(overview: DataOverview): string {
  const job = overview.runningJob;
  if (!job) return '';
  const phase = job.currentPhase as PhaseKey | null;
  const what = phase && phase in doing ? doing[phase] : 'starting';
  return `Update in progress, ${what}. Started ${formatIstDateTime(job.startedAt)}.`;
}

/** The three steps of the pipeline with their last results; a step that is running shows its progress. */
export function PipelineCard({ overview }: { overview: DataOverview }) {
  const titleId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ mb: 2 }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Box>
            <Typography id={titleId} variant="h2">
              Pipeline
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {phaseTitles.csv}, then {phaseTitles.dhan}, then {phaseTitles.aggregate}. Update data runs all three.
            </Typography>
            {/* Always on the page, so a screen reader announces the step changes of a running update. */}
            <Typography role="status" variant="body2" sx={{ fontWeight: 600 }}>
              {liveLine(overview)}
            </Typography>
          </Box>
          <Box
            component="ol"
            aria-label="Pipeline steps"
            sx={{
              m: 0,
              p: 0,
              display: 'grid',
              gap: 2,
              gridTemplateColumns: { xs: '1fr', lg: 'repeat(3, minmax(0, 1fr))' },
            }}
          >
            <CsvStep csv={overview.csv} phase={currentPhase(overview, 'csv')} />
            <DhanStep dhan={overview.dhan} phase={currentPhase(overview, 'dhan')} />
            <AggregatesStep aggregates={overview.aggregates} phase={currentPhase(overview, 'aggregate')} />
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
}
