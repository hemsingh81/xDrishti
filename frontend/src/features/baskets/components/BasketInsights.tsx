import { Box, Card, Divider, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

function InsightCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card component="section" aria-label={title} sx={{ minWidth: 0 }}>
      <Typography variant="h3" component="h2" sx={{ px: 2, py: 1.5 }}>
        {title}
      </Typography>
      <Divider />
      <Box sx={{ px: 2, py: 1.5 }}>
        <Typography color="text.secondary" variant="body2">
          {children}
        </Typography>
      </Box>
    </Card>
  );
}

/**
 * The two cards beside the members in the prototype: how the basket's taken trades did, and the 1-year buy-and-hold
 * benchmark. Their data does not exist yet (trades: P3, price history: P1-12), so they say so instead of showing numbers.
 */
export function BasketInsights() {
  return (
    <Stack spacing={2} sx={{ minWidth: 0 }}>
      <InsightCard title="Basket performance (taken trades)">
        Appears once trades have been taken in this basket&apos;s instruments.
      </InsightCard>
      <InsightCard title="1-year buy-and-hold benchmark">
        Needs price history — available after the EOD service (P1-12).
      </InsightCard>
    </Stack>
  );
}
