import { Alert, Button } from '@mui/material';
import { Link } from 'react-router';

/**
 * "N suggested corporate actions to review" (P1-10a, ADR 0017 §5): splits and bonus issues that the detection found in
 * the stored day bars wait for the owner on the Data screen, where the link leads to the card. One line under the
 * summary ribbon; nothing when none wait.
 */
export function CorporateActionsAlert({ count }: { count: number }) {
  if (count <= 0) return null;
  return (
    <Alert
      severity="warning"
      role="region"
      aria-label="Corporate actions to review"
      sx={{ mb: 1, py: 0 }}
      action={
        <Button
          color="inherit"
          size="small"
          component={Link}
          to="/data?section=actions&actions=suggested"
          aria-label="Review suggested corporate actions"
        >
          Review
        </Button>
      }
    >
      {count === 1 ? '1 suggested corporate action' : `${String(count)} suggested corporate actions`} to review. A split
      or bonus issue that is not applied makes the price series look like a crash.
    </Alert>
  );
}
