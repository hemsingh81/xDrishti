import { createControlComponents } from './componentsControls';
import { createSurfaceComponents } from './componentsSurfaces';
import { alpha, type Components, type Theme } from '@mui/material/styles';
import { palettes, shape, sidebar, spacingUnit, type ColorMode } from './tokens';

/** Component overrides: every value comes from the tokens or the finished palette, never a literal colour. */
export function createComponents(theme: Theme, mode: ColorMode): Components<Theme> {
  const p = palettes[mode];
  return {
    ...createControlComponents(theme, mode),
    ...createSurfaceComponents(mode),
    // MUI 9 no longer reads dotted palette paths in Typography's `color` prop (`color="text.secondary"` rendered as plain
    // text), so the paths the app uses are mapped here — secondary text is secondary again.
    MuiTypography: {
      variants: (
        [
          ['text.primary', p.text],
          ['text.secondary', p.textSecondary],
          ['text.tertiary', p.textTertiary],
          ['text.disabled', theme.palette.action.disabled],
          ['primary.main', p.accent],
          ['error.main', p.bad],
          ['success.main', p.good],
          ['warning.main', p.warn],
          ['info.main', p.info],
        ] as const
      ).map(([path, color]) => ({ props: { color: path }, style: { color } })),
    },
    MuiSkeleton: { styleOverrides: { root: { backgroundColor: alpha(p.textSecondary, 0.16) } } },
    // Sidebar: the active route (react-router adds `.active`) sits on accentSoft with an accent marker.
    MuiListItemButton: {
      styleOverrides: {
        root: {
          position: 'relative',
          borderRadius: shape.radiusNav,
          '&.active': {
            backgroundColor: p.accentSoft,
            color: p.accent,
            '&::before': {
              content: '""',
              position: 'absolute',
              left: 0,
              top: spacingUnit * 2,
              bottom: spacingUnit * 2,
              width: sidebar.markerWidth,
              borderRadius: `0 ${sidebar.markerWidth}px ${sidebar.markerWidth}px 0`,
              backgroundColor: p.accent,
            },
            '& .MuiListItemIcon-root': { color: p.accent },
          },
        },
      },
    },
    MuiListSubheader: { styleOverrides: { root: { color: p.textTertiary } } },
  };
}
