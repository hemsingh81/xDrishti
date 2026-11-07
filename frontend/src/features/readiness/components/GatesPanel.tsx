import { Box, Typography } from '@mui/material';
import type { ReadinessGate } from '../api/readinessQueries';
import { rowStatus, rowStatusWords } from '../lib/words';
import { Panel } from './Panel';
import { GateChip } from './StatusChips';

function Gate({ gate, name, moves }: { gate: ReadinessGate; name: string; moves: string }) {
  return (
    <Box component="section" aria-label={name} sx={{ minWidth: 0 }}>
      <Typography variant="h3" component="h3" sx={{ mb: 0.5 }}>
        {name}
      </Typography>
      <GateChip name={name} passed={gate.passed} />
      <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
        Judges {moves}.{gate.gate === 'S' && !gate.labelOk ? ' The label is not "Ready for the next phase".' : ''}
      </Typography>
      {gate.failing.length > 0 && (
        <Box component="ul" aria-label={`${name}: rows that stop it`} sx={{ m: 0, mt: 0.5, pl: 3 }}>
          {gate.failing.map((row) => (
            <Typography key={row.number} component="li" variant="body2">
              Row {row.number}, {row.name}: {rowStatusWords[rowStatus(row.status)]}. {row.value}
            </Typography>
          ))}
        </Box>
      )}
    </Box>
  );
}

/** Gate R (Candidate to Paper, and the re-enable of a paused cell) and Gate S (Paper to Active), with the rows that stop them. */
export function GatesPanel({ gateR, gateS }: { gateR: ReadinessGate; gateS: ReadinessGate }) {
  return (
    <Panel title="Gates R and S">
      <Box
        sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}
      >
        <Gate
          gate={gateR}
          name="Gate R"
          moves="Candidate to Paper and Paused to Paper (rows 1 to 12 and 14 must all pass)"
        />
        <Gate
          gate={gateS}
          name="Gate S"
          moves="Paper to Active (Gate R, rows 13 and 15, and the label Ready for the next phase)"
        />
      </Box>
    </Panel>
  );
}
