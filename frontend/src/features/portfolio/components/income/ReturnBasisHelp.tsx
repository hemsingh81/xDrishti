import InfoIcon from '@mui/icons-material/InfoOutlined';
import { IconButton, Tooltip } from '@mui/material';

export const returnBasisHelp =
  'Realised profit is added to the numerator; the base is the cost of what you still hold. For a partly sold position the percentage can therefore look high.';

/** A small help button (keyboard focusable) that says what the total return percentage is measured against. */
export function ReturnBasisHelp() {
  return (
    <Tooltip describeChild title={returnBasisHelp}>
      <IconButton size="small" aria-label="How the total return is worked out" sx={{ p: 0.25, ml: 0.5 }}>
        <InfoIcon sx={{ fontSize: 14 }} />
      </IconButton>
    </Tooltip>
  );
}
