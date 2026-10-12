import { alpha, type Theme } from '@mui/material/styles';
import { focusRing, motion, palettes } from '@/theme';

/**
 * The look shared by the basket cards of the row on top (prototype `.bcard`): a hairline card with a 4 px bar in the
 * basket's colour along its top edge. The selected card gets an accent outline and sits on `accentSoft`; a card that
 * would take a drop (`hot`) gets a dashed accent outline. The bar colour is the owner's data, so the card sets it
 * inline (`borderTopColor`).
 */
export const basketCardSx =
  ({ selected, hot = false }: { selected: boolean; hot?: boolean }) =>
  (theme: Theme) => {
    const p = palettes[theme.palette.mode];
    return {
      listStyle: 'none',
      position: 'relative',
      display: 'flex',
      minWidth: 0,
      borderRadius: `${theme.shape.borderRadius}px`,
      border: `1px solid ${theme.palette.divider}`,
      borderTop: `4px solid ${theme.palette.divider}`,
      bgcolor: hot ? alpha(theme.palette.primary.main, 0.12) : selected ? p.accentSoft : 'background.paper',
      transitionProperty: 'background-color, border-color, box-shadow, outline-color',
      transitionDuration: motion.fast,
      outline: hot
        ? `${focusRing.width}px dashed ${theme.palette.primary.main}`
        : selected
          ? `${focusRing.width}px solid ${theme.palette.primary.main}`
          : 'none',
      outlineOffset: 1,
      '&:hover': { bgcolor: hot ? undefined : selected ? p.accentSoft : p.surfaceAlt },
    };
  };
