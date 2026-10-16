import type { Theme } from '@mui/material/styles';
import { focusRing, motion } from './tokens';

export const transition = (theme: Theme, ...props: string[]) =>
  theme.transitions.create(props, { duration: motion.fast, easing: motion.easing });

/** Checkbox / radio / switch hide the real input, so the ring goes on the visible control. */
export function focusRingOn(theme: Theme) {
  return {
    '&.Mui-focusVisible': {
      outline: `${focusRing.width}px solid ${theme.palette.primary.main}`,
      outlineOffset: focusRing.offset,
    },
  };
}
