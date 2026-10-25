import { Box, Button, Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount } from '@/shared/analytics';
import { LoadingState } from '@/shared/ui';
import { whatWorksQuery } from '../api/reportQueries';
import type { ReportQuery } from '../lib/reportFilter';
import { ReportState } from './ReportState';

interface WhatWorksCardProps {
  query: ReportQuery;
  /** Opens the full view; absent where the view is already open. */
  onOpen?: () => void;
}

/** The answer first: the server's own headline sentence and the counts behind it. The words are the server's, not page prose. */
export function WhatWorksCard({ query, onOpen }: WhatWorksCardProps) {
  const result = useQuery(whatWorksQuery(query));
  return (
    <Card component="section" aria-label="What works" sx={{ minWidth: 0 }}>
      <CardContent>
        <Typography variant="h2" component="h2" sx={{ mb: 1 }}>
          What works
        </Typography>
        {result.isPending ? (
          <LoadingState label="Loading what works…" />
        ) : result.isError ? (
          <ReportState error={result.error} />
        ) : (
          <Box sx={{ display: 'grid', gap: 1 }}>
            <Typography variant="body1" sx={{ fontWeight: 600 }}>
              {result.data.headline}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {`Cells looked at ${formatCount(result.data.cellsLookedAt)}, largest n ${formatCount(result.data.largestN)}, trades required ${formatCount(result.data.requiredTrades)}.`}
            </Typography>
            {onOpen && (
              <Box>
                <Button variant="outlined" onClick={onOpen} sx={{ minHeight: 44 }}>
                  See the full answer
                </Button>
              </Box>
            )}
          </Box>
        )}
      </CardContent>
    </Card>
  );
}
