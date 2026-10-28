import { Alert, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { accountsQuery } from '@/features/accounts';
import { useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { executionGapQuery, liveVsPaperQuery } from '../api/reportQueries';
import { readOnlyNote } from '../lib/reportLabels';
import { ReportCharts } from './ReportCharts';
import { ReportEvidence } from './ReportEvidence';
import { ReportGap } from './ReportGap';
import { ReportPairs } from './ReportPairs';
import { ReportSummary } from './ReportSummary';

const groupings = ['strategy', 'month'] as const;

/**
 * The Live vs paper tab of the Live screen (spec P11-03): the same tickets on your live account and the paper account, the shortfall in R with its
 * interval and verdict, the pairs and what was left out, the overlay and the histogram, and the plan-to-actual gap by strategy or month. It reads only
 * GET endpoints; a response without its Evidence block is refused, not drawn.
 */
export function LiveVsPaperTab() {
  const url = useUrlState();
  const by = url.choice('by', groupings, 'strategy');
  const accounts = useQuery(accountsQuery);
  const [picked, setPicked] = useState<string | null>(null);
  const [basis, setBasis] = useState<'gross' | 'net'>('gross');
  const live = (accounts.data ?? []).filter((a) => a.roles.trading.enabled && a.roles.trading.mode === 'manual_live');
  const account = live.find((a) => a.id === picked) ?? live[0];
  const id = account?.id ?? '';
  const report = useQuery({ ...liveVsPaperQuery(id, basis), enabled: account !== undefined });
  const gap = useQuery({ ...executionGapQuery(id, by), enabled: account !== undefined });
  // A response without its Evidence block is refused, not drawn.
  const data = (report.data?.evidence as unknown) ? report.data : undefined;
  if (accounts.isPending) return <LoadingState label="Loading the report…" />;
  if (accounts.isError) return <ErrorState error={accounts.error} />;
  return (
    <Stack sx={{ gap: 2 }}>
      <Alert severity="info">{readOnlyNote}</Alert>
      {!account ? (
        <Typography>No account is in manual-live mode, so there is nothing to compare.</Typography>
      ) : (
        <>
          <Stack direction="row" sx={{ gap: 2, flexWrap: 'wrap' }}>
            <TextField
              select
              size="small"
              label="Account"
              value={account.id}
              onChange={(e) => {
                setPicked(e.target.value);
              }}
              sx={{ minWidth: 220 }}
            >
              {live.map((a) => (
                <MenuItem key={a.id} value={a.id}>
                  {a.name}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              size="small"
              label="Basis"
              value={basis}
              onChange={(e) => {
                setBasis(e.target.value === 'net' ? 'net' : 'gross');
              }}
              sx={{ width: 120 }}
            >
              <MenuItem value="gross">Gross</MenuItem>
              <MenuItem value="net">Net</MenuItem>
            </TextField>
            <TextField
              select
              size="small"
              label="Group the gap by"
              value={by}
              onChange={(e) => {
                url.update({ by: e.target.value === 'strategy' ? null : e.target.value });
              }}
              sx={{ width: 170 }}
            >
              <MenuItem value="strategy">Strategy</MenuItem>
              <MenuItem value="month">Month</MenuItem>
            </TextField>
          </Stack>
          {report.isPending && <LoadingState label="Loading the pairs…" />}
          {report.isError && <ErrorState error={report.error} />}
          {report.data && !data && (
            <ErrorState error={new Error('The response had no Evidence block, so it is not shown.')} />
          )}
          {data?.state === 'unavailable' && (
            <Alert severity="warning">The paper record could not be read, so no comparison is shown.</Alert>
          )}
          {data?.state === 'empty' && <Typography>No live round trip yet.</Typography>}
          {data && data.state !== 'unavailable' && data.state !== 'empty' && (
            <>
              <ReportSummary view={data} />
              <ReportCharts view={data} />
              <ReportPairs view={data} />
            </>
          )}
          {gap.isError && <ErrorState error={gap.error} />}
          {gap.data && (
            <section aria-label="Execution gap">
              <Typography variant="h3" component="h2">
                Plan to actual, by {by}
              </Typography>
              <ReportGap view={gap.data} />
            </section>
          )}
          {data && <ReportEvidence evidence={data.evidence} />}
        </>
      )}
    </Stack>
  );
}
