import { Box, Skeleton } from '@mui/material';

/** The chart area while the data or the chart library loads: the same height as the chart, so nothing shifts. */
export function ChartSkeleton({ height }: { height: number }) {
  return (
    <Box role="status" aria-label="Loading chart">
      <Skeleton variant="rounded" width="100%" height={height} animation="wave" />
    </Box>
  );
}
