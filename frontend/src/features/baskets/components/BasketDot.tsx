import { Box } from '@mui/material';

/** The basket's colour as a dot. The colour is the owner's data, so it is the one inline value; the name is always shown too. */
export function BasketDot({ colour, size = 12, className }: { colour: string; size?: number; className?: string }) {
  return (
    <Box
      component="span"
      aria-hidden
      className={className}
      sx={{ display: 'inline-block', width: size, height: size, borderRadius: '50%', flex: '0 0 auto' }}
      style={{ backgroundColor: colour }}
    />
  );
}
