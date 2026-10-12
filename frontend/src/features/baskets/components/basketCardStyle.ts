import { alpha, type Theme } from '@mui/material/styles';
import { focusRing, motion, palettes } from '@/theme';

/**
 * The look shared by the basket cards of the left column: a hairline card with the basket's colour as a stripe on its
 * left edge. The selected card sits on `accentSoft` with an accent border; a card that would take a drop (`hot`) gets
 * a dashed accent outline. The stripe colour is the owner's data, so it is set inline by the card.
 */
export const basketCardSx =
  ({ selected, hot = false }: { selected: boolean; hot?: boolean }) =>
  (theme: Theme) => {
    const p = palettes[theme.palette.mode];
    return {
      listStyle: 'none',
      display: 'flex',
      alignItems: 'stretch',
      gap: 0.5,
      py: 1,
      pr: 1,
      pl: 0.5,
      borderRadius: `${theme.shape.borderRadius}px`,
      border: `1px solid ${selected ? theme.palette.primary.main : theme.palette.divider}`,
      borderLeftWidth: 4,
      bgcolor: hot ? alpha(theme.palette.primary.main, 0.12) : selected ? p.accentSoft : 'background.paper',
      boxShadow: selected ? `0 0 0 1px ${theme.palette.primary.main}` : 'none',
      transitionProperty: 'background-color, border-color, box-shadow, outline-color',
      transitionDuration: motion.fast,
      outline: hot ? `${focusRing.width}px dashed ${theme.palette.primary.main}` : 'none',
      outlineOffset: 2,
      '&:hover': { bgcolor: hot ? undefined : selected ? p.accentSoft : p.surfaceAlt },
    };
  };
