import { dialogTitleClasses } from '@mui/material/DialogTitle';
import { alpha, type Components, type Theme } from '@mui/material/styles';
import { dialogSpacing, elevation, numeric, palettes, rem, shape, table, type ColorMode } from './tokens';

/** Component overrides for surfaces: tables, cards, app bar, dialogs, menus, banners, tooltips and progress bars. */
export function createSurfaceComponents(mode: ColorMode): Components<Theme> {
  const p = palettes[mode];
  const e = elevation[mode];
  return {
    MuiTable: { styleOverrides: { root: { ...numeric } } },
    MuiTableCell: {
      styleOverrides: {
        root: {
          borderBottomColor: p.border,
          height: table.rowHeight,
          paddingTop: table.cellPaddingY,
          paddingBottom: table.cellPaddingY,
          paddingLeft: table.cellPaddingX,
          paddingRight: table.cellPaddingX,
          '&.MuiTableCell-paddingCheckbox': { padding: '0 0 0 4px' },
          '&.MuiTableCell-paddingNone': { padding: 0 },
        },
        // Header: small uppercase caps on the alternate surface (prototype `.tbl th`).
        head: {
          backgroundColor: p.surfaceAlt,
          color: p.textSecondary,
          fontWeight: 650,
          fontSize: rem(11.5),
          lineHeight: 1.5,
          letterSpacing: '0.02em',
          textTransform: 'uppercase',
        },
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: {
          '&:nth-of-type(even)': { backgroundColor: alpha(p.surfaceAlt, table.zebraAlpha) },
          '&.MuiTableRow-hover:hover': { backgroundColor: alpha(p.accent, table.hoverAlpha) },
          '&.Mui-selected, &.Mui-selected:hover': { backgroundColor: alpha(p.accent, table.selectedAlpha) },
        },
      },
    },
    // Cards: 12 px radius, hairline border and the soft prototype shadow; 14 / 16 px body, 13 / 16 px header.
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: {
        root: { borderColor: p.border, borderRadius: shape.radius, backgroundImage: 'none', boxShadow: e.md },
      },
    },
    MuiCardContent: {
      styleOverrides: { root: { padding: '14px 16px', '&:last-child': { paddingBottom: 14 } } },
    },
    MuiCardHeader: {
      styleOverrides: {
        root: { padding: '13px 16px', borderBottom: `1px solid ${p.border}` },
        title: { fontSize: rem(14.5), fontWeight: 650, letterSpacing: '-0.005em' },
      },
    },
    MuiDrawer: { styleOverrides: { paper: { borderRight: `1px solid ${p.border}`, boxShadow: 'none' } } },
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'inherit' },
      styleOverrides: {
        // Frosted: the surface at 88 % over a blur, so content scrolls softly beneath it (prototype `.top`).
        root: {
          backgroundColor: alpha(p.surface, 0.88),
          backdropFilter: 'blur(10px)',
          boxShadow: 'none',
          borderBottom: `1px solid ${p.border}`,
        },
      },
    },
    // Dialogs: header, body and footer bands separated by hairlines, all on the surface (prototype .modal / .dlg-h / .dlg-b / .dlg-f).
    MuiDialog: {
      styleOverrides: {
        paper: ({ ownerState }) => ({
          backgroundImage: 'none', // no dark-mode elevation tint: the body stays `surface`
          border: ownerState.fullScreen ? 0 : `1px solid ${p.border}`,
          borderRadius: ownerState.fullScreen ? 0 : shape.radiusLarge,
          boxShadow: e.lg,
        }),
      },
    },
    MuiDialogTitle: {
      styleOverrides: {
        root: { padding: dialogSpacing.header, background: p.surface, borderBottom: `1px solid ${p.border}` },
      },
    },
    MuiDialogContent: {
      styleOverrides: {
        root: {
          padding: dialogSpacing.body,
          // MUI drops the top padding after a title; the header is a separate band here, so keep it.
          [`.${dialogTitleClasses.root} + &`]: { padding: dialogSpacing.body },
        },
      },
    },
    MuiDialogActions: {
      styleOverrides: {
        root: { padding: dialogSpacing.footer, background: p.surface, borderTop: `1px solid ${p.border}` },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: {
          backgroundColor: p.text,
          color: p.background,
          fontSize: `${12 / 14}rem`,
          fontWeight: 500,
          borderRadius: shape.radiusSmall,
          boxShadow: e.md,
        },
        arrow: { color: p.text },
      },
    },
    MuiPopover: {
      styleOverrides: {
        paper: { border: `1px solid ${p.border}`, borderRadius: shape.radius, boxShadow: e.lg },
      },
    },
    MuiMenu: {
      styleOverrides: { paper: { border: `1px solid ${p.border}`, boxShadow: e.lg }, list: { padding: 6 } },
    },
    MuiMenuItem: {
      styleOverrides: {
        root: {
          borderRadius: shape.radiusSmall,
          '&.Mui-selected': { backgroundColor: p.accentSoft },
          '&.Mui-selected:hover': { backgroundColor: p.accentSoft },
        },
      },
    },
    // Banners: a soft tint with a matching hairline, 13 px text on the normal text colour (prototype `.banner`).
    MuiAlert: {
      styleOverrides: {
        root: ({ ownerState }) => {
          const tones = {
            info: { bg: p.accentSoft, line: p.accent, icon: p.accent, alpha: 0.22 },
            success: { bg: p.goodSoft, line: p.good, icon: p.good, alpha: 0.25 },
            warning: { bg: p.warnSoft, line: p.warn, icon: p.warn, alpha: 0.25 },
            error: { bg: p.badSoft, line: p.bad, icon: p.bad, alpha: 0.25 },
          } as const;
          const tone = tones[ownerState.severity ?? 'info'];
          return {
            borderRadius: shape.radius,
            padding: '11px 15px',
            fontSize: rem(13),
            alignItems: 'flex-start',
            ...(ownerState.variant === 'standard'
              ? { backgroundColor: tone.bg, color: p.text, border: `1px solid ${alpha(tone.line, tone.alpha)}` }
              : {}),
            '& .MuiAlert-icon': { color: tone.icon },
          };
        },
        icon: { padding: 0, marginRight: 10, marginTop: 1, fontSize: 18, opacity: 1 },
        message: { padding: 0 },
        action: { padding: 0, marginLeft: 12, marginRight: 0 },
      },
    },
    MuiLinearProgress: {
      styleOverrides: {
        root: { height: 8, borderRadius: 4, backgroundColor: p.surfaceStrong },
        bar: { borderRadius: 4 },
      },
    },
  };
}
