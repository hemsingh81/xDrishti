import { Card, CardContent, Typography } from '@mui/material';
import { ActionListSkeleton } from './ActionListSkeleton';

/** The card while its code is on the way: the title is there, the rows are placeholders. */
export function ActionCenterSkeleton() {
  return (
    <Card sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <Typography variant="h2" component="h2" sx={{ mb: 1.5 }}>
          Action center
        </Typography>
        <ActionListSkeleton />
      </CardContent>
    </Card>
  );
}
