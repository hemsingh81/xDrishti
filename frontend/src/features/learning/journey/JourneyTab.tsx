import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Box, Typography } from '@mui/material';
import { skipToken, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { EvidenceBlock, formatCount, metricOf, MetricValue, NonValue } from '@/shared/analytics';
import type { EvidenceProps } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { changelogQuery, counterfactualQuery, journeyQuery, lessonsQuery, modelsQuery } from '../api/learningQueries';
import type { Counterfactual, Journey } from '../api/learningQueries';
import { Panel } from '../components/Panel';
import { headlineOf } from './headline';
import { PlanPriceSection } from './PlanPriceSection';
import { TimelineSection } from './TimelineSection';
import { WindowsSection } from './WindowsSection';

const command = 'scripts/xd.sh learn counterfactual --run RUNKEY:STYLE:EXIT … --store';

const gain = (v: number): string => v.toFixed(4);

function evidenceOf(journey: Journey, report: Counterfactual | undefined): EvidenceProps {
  const e = journey.evidence;
  const basis = e.basis === 'net_confirmed' ? 'net' : e.basis === 'gross' ? 'gross' : 'unknown';
  return {
    n: e.n,
    range: e.from !== null && e.to !== null ? { from: e.from, to: e.to } : null,
    basis,
    basisText:
      e.basis === 'costed_unconfirmed'
        ? 'R after the stored charge sheet; costed on the stored sheet, not confirmed as yours'
        : undefined,
    population: e.population.map((key) => key.slice(0, 12)),
    limits: report?.limits ?? e.limits,
    label: 'Journey evidence',
  };
}

function CounterfactualPanels({ report, journey }: { report: Counterfactual; journey: Journey }) {
  const headline = headlineOf(report);
  const pooled = report.judged;
  return (
    <>
      <Panel title="Is the system getting better?">
        <Box sx={{ display: 'grid', gap: 1.5 }}>
          <Typography component="p" variant="h3" sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
            {headline.tone === 'neutral' && <InfoIcon aria-hidden fontSize="small" />}
            {headline.text}
          </Typography>
          <Box component="ul" aria-label="Reasons" sx={{ m: 0, pl: 2.5 }}>
            {report.reasons.map((reason, index) => (
              <li key={index}>{reason}</li>
            ))}
          </Box>
          {pooled !== null && (
            <Typography component="p">
              Pooled paired log loss gain of retraining:{' '}
              <MetricValue
                state={metricOf(pooled.gain)}
                format={gain}
                tone={headline.colourGain ? 'gain-loss' : 'neutral'}
              />
              {' (standard error by day '}
              <MetricValue state={metricOf(pooled.gainStandardError)} format={gain} />
              {')'}
            </Typography>
          )}
        </Box>
      </Panel>
      <Panel title="Period and knobs">
        <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
          <Box>
            Frozen at {report.cut === null ? 'no date (no period could be made)' : formatDate(report.cut)} on{' '}
            {formatCount(report.frozenTrainRows)} trades; {report.frozenAgeMonths} months of stopped learning to{' '}
            {formatDate(report.to)}.
          </Box>
          <Box>
            Retrain every {report.knobs.retrainMonths} months, frozen months {report.knobs.frozenMonths}, hold-out{' '}
            {report.knobs.holdOutMonths} months, minimum judged windows {report.knobs.minJudgedWindows}, trials{' '}
            {report.knobs.trials}.
          </Box>
          <Box>
            Stored counterfactuals for the same runs: {report.sameRunsStored} (every re-run is another trial). Basis:{' '}
            {report.basis}.
          </Box>
        </Box>
      </Panel>
      <Panel title="Windows">
        <WindowsSection report={report} headline={headline} />
      </Panel>
      <Box component="div">
        <EvidenceBlock {...evidenceOf(journey, report)} />
      </Box>
    </>
  );
}

/**
 * Journey (P8-12): whether a model that kept learning beat a frozen copy on the same later trades, window by window, beside the
 * lessons and model versions of the same period and the plan-price grades by week. Every number is a server field; the live line
 * is not evaluable until real trades exist. Read only: the counterfactual is computed and stored by the owner's command.
 */
export function JourneyTab() {
  const [today] = useState(() => Date.now());
  const journey = useQuery(journeyQuery);
  const id = journey.data?.counterfactual?.id;
  const report = useQuery({
    ...counterfactualQuery(id ?? 0),
    queryFn: id === undefined ? skipToken : counterfactualQuery(id).queryFn,
  });
  // shortcut: lessons and models are the newest 50 of each (the list queries' cap); older ones are not drawn on the time axis.
  const lessons = useQuery(lessonsQuery('all'));
  const models = useQuery(modelsQuery('all'));
  const changelog = useQuery(changelogQuery());
  if (journey.isPending) return <LoadingState label="Loading the journey…" />;
  if (journey.isError) return <ErrorState error={journey.error} />;
  const data = journey.data;
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      {data.counterfactual === null ? (
        <Panel title="Is the system getting better?">
          <NonValue
            state={{
              kind: 'empty',
              reason: `No counterfactual is stored yet. Run \`${command}\` (newest run keys are on the Reports screen).`,
            }}
          />
        </Panel>
      ) : (
        <>
          {report.isPending && <LoadingState label="Loading the counterfactual…" />}
          {report.isError && <ErrorState error={report.error} />}
          {report.data && <CounterfactualPanels report={report.data} journey={data} />}
        </>
      )}
      <Panel title="Lessons and models over time">
        {(lessons.isPending || models.isPending || changelog.isPending) && (
          <LoadingState label="Loading lessons and models…" />
        )}
        {(lessons.isError || models.isError || changelog.isError) && (
          <ErrorState error={lessons.error ?? models.error ?? changelog.error} />
        )}
        {lessons.data && models.data && changelog.data && (
          <TimelineSection today={today} lessons={lessons.data} models={models.data} designations={changelog.data} />
        )}
      </Panel>
      <Panel title="Plan prices by week">
        <PlanPriceSection journey={data} />
      </Panel>
    </Box>
  );
}
