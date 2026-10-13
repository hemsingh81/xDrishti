import PriceIcon from '@mui/icons-material/PriceChangeOutlined';
import { Button } from '@mui/material';

interface RefreshPricesButtonProps {
  /** The quote service is known (its run can be started and followed). */
  available: boolean;
  /** A run is on its way or going on: the button waits. */
  busy: boolean;
  onRefresh: () => void;
}

/** Runs the quote service now; busy while it runs. */
export function RefreshPricesButton({ available, busy, onRefresh }: RefreshPricesButtonProps) {
  return (
    <Button
      variant="outlined"
      startIcon={<PriceIcon />}
      loading={busy}
      disabled={!available}
      title={available ? 'Fetch the latest prices and previous closes now' : 'The quote service is not available'}
      onClick={onRefresh}
    >
      {busy ? 'Refreshing prices…' : 'Refresh prices'}
    </Button>
  );
}
