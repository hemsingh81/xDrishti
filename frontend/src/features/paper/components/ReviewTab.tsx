import { Alert, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatAsOf } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperReviewListQuery, paperReviewNowQuery, paperReviewStoredQuery } from '../api/reviewQueries';
import { DisciplineCard } from './DisciplineCard';
import { ReviewSections } from './ReviewSections';

const NOW = '';

/** One stored review, or the current week built now. */
function ReviewBody({ week }: { week: string }) {
  const stored = useQuery({ ...paperReviewStoredQuery(week), enabled: week !== NOW });
  const now = useQuery({ ...paperReviewNowQuery, enabled: week === NOW });
  const review = week === NOW ? now : stored;
  if (review.isPending) return <LoadingState label="Loading the review…" />;
  if (review.isError) return <ErrorState error={review.error} />;
  const r = review.data;
  return (
    <>
      <Alert severity="info" sx={{ mb: 2 }}>
        {r.stored
          ? `Stored review of ${r.week}, built ${formatAsOf(r.builtAt)}.`
          : `Built now for ${r.week} from stored records; nothing is stored.`}{' '}
        Paper only: no order was placed and none exists. Nothing was changed.
      </Alert>
      <ReviewSections sections={r.sections} />
    </>
  );
}

/** The Review tab: the weekly paper review (stored or built now) and the discipline report behind it. Read only; no write control. */
export function ReviewTab() {
  const list = useQuery(paperReviewListQuery);
  const [week, setWeek] = useState(NOW);
  const items = list.data?.items ?? [];
  return (
    <>
      <TextField
        select
        size="small"
        label="Review week"
        value={week}
        onChange={(event) => {
          setWeek(event.target.value);
        }}
        sx={{ minWidth: 260, mb: 2 }}
      >
        <MenuItem value={NOW}>This week, built now</MenuItem>
        {items.map((item) => (
          <MenuItem key={item.id} value={item.week}>
            {item.week} (stored)
          </MenuItem>
        ))}
      </TextField>
      {list.isError && <Typography color="text.secondary">The list of stored reviews could not be read.</Typography>}
      <ReviewBody key={week} week={week} />
      <DisciplineCard />
    </>
  );
}
