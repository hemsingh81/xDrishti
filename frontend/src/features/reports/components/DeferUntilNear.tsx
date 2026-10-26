import { Box } from '@mui/material';
import { useRef, type ReactNode } from 'react';
import { useInView } from '@/shared/lib/useInView';

/**
 * Mounts its children, and so their requests, when the place comes near the viewport (P8-17f). The panels below the first
 * view otherwise load twice on the way to a basket: once unfiltered, once with the basket. The place keeps a height so the
 * page does not jump; once seen the children stay mounted. Without `IntersectionObserver` they mount at once.
 */
export function DeferUntilNear({ children }: { children: ReactNode }) {
  const root = useRef<HTMLDivElement>(null);
  const near = useInView(root);
  return (
    <Box ref={root} sx={near ? undefined : { minHeight: 480 }}>
      {near && children}
    </Box>
  );
}
