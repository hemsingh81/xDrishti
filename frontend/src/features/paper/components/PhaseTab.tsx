import { Alert, Box, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { VerdictChip, formatCount, type Verdict } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperPhaseQuery, type PaperPhaseReport, type PhaseCriterion } from '../api/phaseQueries';
import { BasisNote } from './BasisNote';
import { PhasePrinted } from './PhasePrinted';
import { PhaseTables } from './PhaseTables';

const words: Record<string, Verdict> = { MET: 'met', 'NOT MET': 'not_met', 'NOT EVALUABLE': 'not_evaluable' };
const asVerdict = (word: string): Verdict => words[word] ?? 'not_evaluable';

/** The Phase check tab: the P10 exit judged from stored records, each criterion with its facts and blockers, the caveats in the headline. Read only; no write control. */
export function PhaseTab() {
  const phase = useQuery(paperPhaseQuery);
  if (phase.isPending) return <LoadingState label="Reading the paper phase check…" />;
  if (phase.isError) return <ErrorState error={phase.error} />;
  return <Body report={phase.data.report} />;
}

function Body({ report }: { report: PaperPhaseReport }) {
  const { epoch } = report;
  return (
    <Stack spacing={3}>
      <BasisNote
        basis="Paper only. Computed on read from stored records: nothing is written, no replay is run and no setting is changed. The owner confirms the exit; this page never does."
        limits={report.limits}
      />
      <Box component="section" aria-labelledby="phase-exit-heading">
        <Typography variant="h2" id="phase-exit-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          The P10 exit
        </Typography>
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'baseline', flexWrap: 'wrap' }}>
          <VerdictChip verdict={asVerdict(report.exitVerdict)} />
          <Typography component="span" sx={{ fontWeight: 600 }}>
            {report.exitCriterion}
          </Typography>
        </Box>
        <Typography color="text.secondary" sx={{ mt: 0.5 }}>
          {epoch
            ? `Epoch ${epoch.id} (${epoch.kind}) from ${epoch.startedOn}: ${formatCount(report.elapsedDays)} elapsed days to ${report.through}.`
            : `No paper epoch yet (through ${report.through}).`}
        </Typography>
      </Box>
      <Box component="section" aria-labelledby="phase-criteria-heading">
        <Typography variant="h2" id="phase-criteria-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          Criteria
        </Typography>
        <Stack spacing={2}>
          {report.criteria.map((criterion) => (
            <Criterion key={criterion.id} criterion={criterion} />
          ))}
        </Stack>
      </Box>
      <PhaseTables report={report} />
      <PhasePrinted printed={report.printed} />
    </Stack>
  );
}

function Criterion({ criterion }: { criterion: PhaseCriterion }) {
  return (
    <Box>
      <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'baseline', flexWrap: 'wrap' }}>
        <VerdictChip verdict={asVerdict(criterion.verdict)} />
        <Typography component="span" sx={{ fontWeight: 600 }}>
          {criterion.id}. {criterion.title}:
        </Typography>
        <Typography component="span">{criterion.label}</Typography>
      </Box>
      {criterion.facts.length > 0 && (
        <Box component="ul" aria-label={`Facts of ${criterion.id}`} sx={{ m: 0, pl: 2.5 }}>
          {criterion.facts.map((fact) => (
            <li key={fact}>{fact}</li>
          ))}
        </Box>
      )}
      {criterion.blockers.length > 0 && (
        <Alert severity="info" sx={{ mt: 0.5 }}>
          <Box component="ul" aria-label={`Blockers of ${criterion.id}`} sx={{ m: 0, pl: 2 }}>
            {criterion.blockers.map((blocker) => (
              <li key={blocker}>{blocker}</li>
            ))}
          </Box>
        </Alert>
      )}
    </Box>
  );
}
