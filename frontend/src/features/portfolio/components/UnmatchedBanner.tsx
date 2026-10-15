import { Alert, Button } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';

interface UnmatchedBannerProps {
  /** Holdings with no match found so far. */
  count: number;
  /** More pages are still being read. */
  reading: boolean;
  onShowAll: () => void;
}

/** Above the holdings when the list shows only those with no match in the instrument master list: why, and the way back. */
export function UnmatchedBanner({ count, reading, onShowAll }: UnmatchedBannerProps) {
  return (
    <Alert
      severity="info"
      role="status"
      sx={{ mb: 2, flexShrink: 0 }}
      action={
        <Button color="inherit" size="small" onClick={onShowAll}>
          Show all holdings
        </Button>
      }
    >
      {reading
        ? 'Looking for holdings that are not in the instrument master list…'
        : `${formatNumber(count)} ${count === 1 ? 'holding is' : 'holdings are'} not in the instrument master list, so ${count === 1 ? 'it has' : 'they have'} no price or sector. The master refreshes by itself every day. Securities you traded but no longer hold are not listed here.`}
    </Alert>
  );
}
