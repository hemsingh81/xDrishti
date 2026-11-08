import { Alert, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { paperSequentialQuery } from '../api/shadowQueries';
import { SequentialBlock } from './SequentialState';

/** The CF-09 line beside the phase check: the state of the edge and behavioural series. A flag only; the fixed P10 gates decide. */
export function SequentialLine() {
  const sequential = useQuery(paperSequentialQuery);
  if (sequential.isPending) return <LoadingState label="Reading the sequential test…" />;
  if (sequential.isError) {
    return (
      <Alert severity="info">
        Sequential failure test: unavailable (no shadow edge series yet, or it could not be read).
      </Alert>
    );
  }
  const { edge, behavioural, line } = sequential.data;
  return (
    <Box component="section" aria-labelledby="phase-sequential-heading">
      <Typography variant="h2" id="phase-sequential-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Sequential failure test
      </Typography>
      <SequentialBlock edgeState={edge.state} behaviouralState={behavioural.state} line={line} />
    </Box>
  );
}
