import BiotechIcon from '@mui/icons-material/BiotechOutlined';
import { Alert, Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { TrainingBanner, TrainingReport, type ReportQuery } from '@/features/reports';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { simJobQuery } from '../api/simulationApi';
import { SimJobPanel } from './SimJobPanel';
import { SimulationTabs } from './SimulationTabs';
import { choiceProblem, type SimChoice } from '../lib/choice';
import { SimReportPicker } from './SimReportPicker';

const NOT_EVIDENCE =
  'Training simulations, not evidence. They teach the model; no figure here is a result you can rely on.';
const initial: SimChoice = { scope: 'all', symbol: '', basket: '', from: '', to: '', type: 'key-figures' };

const toQuery = (c: SimChoice): ReportQuery => {
  const query: ReportQuery = { purpose: 'training' };
  if (c.scope === 'stock') query.symbol = c.symbol;
  if (c.scope === 'basket') query.basket = c.basket;
  if (c.from) query.from = c.from;
  if (c.to) query.to = c.to;
  return query;
};

/** Reports over the simulate-everything job (AU-15): start and follow the job, read any report while it runs. */
export function SimulationReportPage() {
  const job = useQuery(simJobQuery);
  const queryClient = useQueryClient();
  const [choice, setChoice] = useState(initial);
  const header = (
    <>
      <PageHeader
        title="Simulation reports"
        icon={BiotechIcon}
        hue="reports"
        description="Reports over the simulate-everything job."
      />
      <SimulationTabs current="/reports/simulation" />
    </>
  );
  const notice = (
    <Alert severity="warning" sx={{ mb: 2 }}>
      {NOT_EVIDENCE}
    </Alert>
  );
  if (job.isPending) {
    return (
      <>
        {header}
        {notice}
        <LoadingState label="Loading the simulation job…" />
      </>
    );
  }
  if (job.isError) {
    return (
      <>
        {header}
        {notice}
        <ErrorState error={job.error} />
      </>
    );
  }
  const query = toQuery(choice);
  const ready = job.data !== null && choiceProblem(choice) === undefined;
  return (
    <>
      {header}
      {notice}
      <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
        <SimJobPanel job={job.data} />
        {job.data === null ? (
          <Alert severity="info">No simulation has run yet, so there is no report to read. Start the job above.</Alert>
        ) : (
          <>
            <Card component="section" aria-labelledby="sim-pick-title">
              <CardContent>
                <Typography id="sim-pick-title" variant="h2" component="h2" sx={{ mb: 1.5 }}>
                  Choose a report
                </Typography>
                <SimReportPicker value={choice} onChange={setChoice} />
              </CardContent>
            </Card>
            {ready && (
              <>
                <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                  <Button
                    variant="outlined"
                    size="small"
                    onClick={() => {
                      void queryClient.invalidateQueries({ queryKey: ['reports'] });
                    }}
                    sx={{ minHeight: 44 }}
                  >
                    Refresh report
                  </Button>
                  <Typography variant="body2" color="text.secondary">
                    The report does not refresh by itself while the job runs.
                  </Typography>
                </Stack>
                <TrainingBanner query={query} />
                <TrainingReport type={choice.type} query={query} />
              </>
            )}
          </>
        )}
      </Box>
    </>
  );
}
