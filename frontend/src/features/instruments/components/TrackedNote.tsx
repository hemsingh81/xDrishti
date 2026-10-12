import { Alert } from '@mui/material';
import type { InstrumentSummary } from '@/shared/api';

/** Honest note on the Tracked tab while no tracked instrument has any history yet (the EOD service is a later story). */
export function TrackedNote({ items }: { items: readonly InstrumentSummary[] | undefined }) {
  if (!items || items.length === 0 || items.some((item) => item.tracked.dataTo !== null)) return null;
  return (
    <Alert severity="info" sx={{ mb: 2, flexShrink: 0 }}>
      History collection starts with the EOD service (P1-12). Until then coverage shows a dash and the status says
      &quot;History not collected yet&quot;.
    </Alert>
  );
}
