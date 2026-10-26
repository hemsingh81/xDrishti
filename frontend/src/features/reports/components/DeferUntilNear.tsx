import { Box } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useRef, type ReactNode } from 'react';
import { useInView } from '@/shared/lib/useInView';

/** The height of the five panels below the first view (measured on the fixture): one column below xl, rows of two from xl. */
const placeholderHeight = { xs: 1260, sm: 1050, xl: 760 };

/**
 * Mounts its children, and so their requests, when the place comes near the viewport (P8-17f). The panels below the first
 * view otherwise load twice on the way to a basket: once unfiltered, once with the basket. The place keeps a height so the
 * page does not jump; once seen the children stay mounted. Without `IntersectionObserver` they mount at once.
 */
export function DeferUntilNear({ children }: { children: ReactNode }) {
  const root = useRef<HTMLDivElement>(null);
  const near = useInView(root);
  return (
    <Box ref={root} aria-busy={!near} sx={near ? undefined : { minHeight: placeholderHeight }}>
      {near ? children : <Box sx={visuallyHidden}>More panels load as you scroll.</Box>}
    </Box>
  );
}
