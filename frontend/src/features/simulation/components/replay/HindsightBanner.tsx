import { Alert, AlertTitle, Box } from '@mui/material';
import type { ReplayRun } from '../../api/replayApi';

/** Always visible, above every figure (AC 10): each hindsight label of the run and the costs label. Never behind a click. */
export function HindsightBanner({ run }: { run: Pick<ReplayRun, 'hindsight' | 'costsLabel'> }) {
  const lines = run.hindsight.map((label) => ({ key: label.code, text: label.text }));
  if (!lines.some((line) => line.text === run.costsLabel)) lines.push({ key: 'costs', text: run.costsLabel });
  return (
    <Alert severity="warning" role="note" aria-label="Hindsight labels" sx={{ '& .MuiAlert-message': { minWidth: 0 } }}>
      <AlertTitle>Research replay, not evidence. It carries hindsight:</AlertTitle>
      <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
        {lines.map((line) => (
          <li key={line.key}>{line.text}</li>
        ))}
      </Box>
    </Alert>
  );
}
