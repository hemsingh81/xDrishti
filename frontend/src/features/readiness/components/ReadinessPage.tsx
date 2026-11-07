import ChecklistIcon from '@mui/icons-material/FactCheckOutlined';
import { Alert, Box, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { scorecardsQuery, type ScorecardList } from '../api/readinessQueries';
import { allZero } from '../lib/words';
import { EnforceCard } from './EnforceCard';
import { Panel } from './Panel';
import { ScorecardDetail } from './ScorecardDetail';
import { StrategyTable } from './StrategyTable';
import { SystemCard } from './SystemCard';

const wholeNumber = /^\d+$/;

/** The strategy and version the address names, else the weakest strategy that has a Lab version. */
function chosen(
  data: ScorecardList,
  strategy: string | null,
  version: string | null,
): { id: string | null; version: number | null } {
  const first = data.strategies.find((row) => row.version !== null);
  const id = strategy ?? first?.strategy ?? null;
  if (id === null) return { id, version: null };
  const listed = data.strategies.find((row) => row.strategy === id)?.version ?? null;
  const asked = version !== null && wholeNumber.test(version) ? Number(version) : null;
  return { id, version: asked ?? listed };
}

function Detail({ data }: { data: ScorecardList }) {
  const url = useUrlState();
  const requested = url.text('strategy');
  const { id, version } = chosen(data, requested, url.text('version'));
  const options = data.strategies.filter((row) => row.version !== null).map((row) => row.strategy);
  const all = requested !== null && !options.includes(requested) ? [requested, ...options] : options;
  return (
    <>
      <Panel
        title="Strategy scorecard"
        actions={
          all.length > 0 && (
            <TextField
              select
              size="small"
              label="Strategy"
              value={id ?? ''}
              onChange={(event) => {
                url.update({ strategy: event.target.value, version: null });
              }}
              slotProps={{ select: { native: true } }}
              sx={{ minWidth: 200 }}
            >
              {all.map((strategy) => (
                <option key={strategy} value={strategy}>
                  {strategy}
                </option>
              ))}
            </TextField>
          )
        }
      >
        {id === null && (
          <NonValue state={{ kind: 'empty', reason: 'No strategy with a Lab version to show a scorecard for.' }} />
        )}
        {id !== null && version === null && (
          <NonValue
            state={{ kind: 'unavailable', reason: `${id} has no Lab version, so there is no scorecard for it.` }}
          />
        )}
      </Panel>
      {id !== null && version !== null && <ScorecardDetail strategy={id} version={version} />}
    </>
  );
}

function Loaded({ data }: { data: ScorecardList }) {
  return (
    <>
      {allZero(data.strategies.map((row) => row.score)) && (
        <Alert severity="info">
          Every score reads 0 today and no strategy can pass Gate R. That is the designed outcome, not a defect: an
          input that does not exist yet counts as a fail, never as a pass and never as a measured 0. Open a strategy
          below to see which producers each card is waiting for.
        </Alert>
      )}
      <SystemCard system={data.system} />
      <StrategyTable strategies={data.strategies} />
      <Detail data={data} />
      {data.statements.length > 0 && (
        <Box
          component="ul"
          aria-label="What to keep in mind"
          sx={{ m: 0, pl: 3, typography: 'body2', color: 'text.secondary' }}
        >
          {data.statements.map((sentence) => (
            <li key={sentence}>{sentence}</li>
          ))}
        </Box>
      )}
    </>
  );
}

/**
 * Readiness (CF-11): the score of each strategy and of the system, the 15-row edge checklist and the gates that guard the
 * lifecycle moves. Derived and read only, except the owner's enforce switch (EnforceCard). Places no order.
 */
export function ReadinessPage() {
  const query = useQuery(scorecardsQuery);
  return (
    <>
      <PageHeader
        title="Readiness"
        icon={ChecklistIcon}
        hue="learning"
        description="Is the evidence strong enough to move on? Each score is the minimum of six sub-scores; an input that is not available counts as a fail. Derived from stored results on every read."
      />
      <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
        {query.isPending && <LoadingState label="Loading the scorecards…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data && <Loaded data={query.data} />}
        <EnforceCard />
      </Box>
    </>
  );
}
