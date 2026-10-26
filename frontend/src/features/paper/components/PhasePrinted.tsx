import { Box, Typography } from '@mui/material';
import { formatCount } from '@/shared/analytics';
import type { PaperPhaseReport } from '../api/phaseQueries';

function List({ label, items }: { label: string; items: readonly string[] }) {
  return (
    <Box component="ul" aria-label={label} sx={{ m: 0, pl: 2.5 }}>
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </Box>
  );
}

/** What is printed beside the criteria and never judged by them: discipline, sensitivity, the split, P7, setup, blockers and what is proved elsewhere. */
export function PhasePrinted({ printed }: { printed: PaperPhaseReport['printed'] }) {
  return (
    <Box component="section" aria-labelledby="phase-printed-heading">
      <Typography variant="h2" id="phase-printed-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Printed, not judged
      </Typography>
      <List label="Discipline" items={printed.disciplineLines.map((l) => `Discipline: ${l}`)} />
      <Typography>Sensitivity over all primary proposals, plan prices: {printed.sensitivityMean}.</Typography>
      <Typography>
        Counted trades by approval: {formatCount(printed.ownerApproved)} by the owner,{' '}
        {formatCount(printed.autoApproved)} auto. Plans with no primary ticket:{' '}
        {formatCount(printed.plansWithoutPrimary)}. Manual nightly runs: {formatCount(printed.manualRuns)}.
      </Typography>
      <Typography>P7 phase check: {printed.p7}.</Typography>
      <Typography>Charge sheet confirmed by the owner: {printed.chargeSheetConfirmed ? 'yes' : 'no'}.</Typography>
      <List
        label="Services"
        items={printed.services.map((s) => `Service ${s.id}: ${s.state} (${s.role}${s.required ? ', needed' : ''})`)}
      />
      <List
        label="Settings"
        items={printed.switches.map((s) => `Setting ${s.name}: ${s.on ? 'ON' : 'OFF'} (${s.effect})`)}
      />
      {printed.blockers.length > 0 && <List label="Blockers" items={printed.blockers.map((b) => `Blocker: ${b}`)} />}
      <List label="Not judged here" items={printed.notJudged.map((n) => `${n.name}: ${n.verdict}. ${n.proof}`)} />
    </Box>
  );
}
