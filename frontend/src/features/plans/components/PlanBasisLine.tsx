import { Typography } from '@mui/material';

/**
 * The plan's basis as plain text, clamped to two lines (the whole text stays in the page and in the tooltip). The chips
 * are only a summary: this line is what makes a warning impossible to miss if the wording ever drifts.
 */
export function PlanBasisLine({ basis }: { basis: string }) {
  return (
    <Typography
      variant="caption"
      color="text.secondary"
      title={basis}
      sx={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}
    >
      Basis: {basis}
    </Typography>
  );
}
