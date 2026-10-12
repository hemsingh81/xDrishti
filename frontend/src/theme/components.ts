import { dialogTitleClasses } from '@mui/material/DialogTitle';
import { alpha, type Components, type Theme } from '@mui/material/styles';
import {
  controlHeight,
  dialogSpacing,
  elevation,
  focusRing,
  motion,
  numeric,
  palettes,
  shape,
  sidebar,
  spacingUnit,
  stateAlpha,
  table,
  type ColorMode,
} from './tokens';

const transition = (theme: Theme, ...props: string[]) =>
  theme.transitions.create(props, { duration: motion.fast, easing: motion.easing });

/** Checkbox / radio / switch hide the real input, so the ring goes on the visible control. */
function focusRingOn(theme: Theme) {
  return {
    '&.Mui-focusVisible': {
      outline: `${focusRing.width}px solid ${theme.palette.primary.main}`,
      outlineOffset: focusRing.offset,
    },
  };
}

/** Component overrides: every value comes from the tokens or the finished palette, never a literal colour. */
export function createComponents(theme: Theme, mode: ColorMode): Components<Theme> {
  const p = palettes[mode];
  const e = elevation[mode];
  return {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: ({ ownerState }) => ({
          borderRadius: shape.radiusSmall,
          transition: transition(theme, 'background-color', 'border-color', 'color'),
          ...(ownerState.variant === 'contained' && ownerState.color === 'primary'
            ? { '&:hover': { backgroundColor: alpha(p.accent, 0.88) } }
            : {}),
          ...(ownerState.variant === 'outlined' && ownerState.color === 'primary' ? { borderColor: p.accent } : {}),
        }),
        sizeMedium: { minHeight: controlHeight.medium },
        sizeSmall: { minHeight: controlHeight.small },
      },
    },
    MuiIconButton: { styleOverrides: { root: { transition: transition(theme, 'background-color', 'color') } } },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: shape.radiusSmall,
          backgroundColor: p.surface,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: p.inputBorder },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: p.accent },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: p.accent, borderWidth: 2 },
          '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: p.bad },
          '&.Mui-disabled': { backgroundColor: theme.palette.action.disabledBackground },
        },
        sizeSmall: { minHeight: controlHeight.small },
        input: { '&::placeholder': { color: p.textSecondary, opacity: stateAlpha.placeholder } },
      },
    },
    MuiFormHelperText: { styleOverrides: { root: { fontSize: `${12 / 14}rem`, marginLeft: 2 } } },
    MuiChip: {
      styleOverrides: {
        root: ({ ownerState }) => ({
          fontWeight: 600,
          borderRadius: shape.radiusSmall,
          ...(ownerState.variant === 'outlined' && ownerState.color === 'default' ? { borderColor: p.border } : {}),
        }),
        // Room to the left of the icon, so the icon does not sit on the chip's edge.
        icon: ({ ownerState }) => ({ marginLeft: ownerState.size === 'small' ? 6 : 8, marginRight: -2 }),
      },
    },
    MuiTabs: {
      styleOverrides: {
        root: { borderBottom: `1px solid ${p.border}` },
        indicator: { height: 3, borderRadius: '3px 3px 0 0', backgroundColor: p.accent },
      },
    },
    MuiTab: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          color: p.textSecondary,
          transition: theme.transitions.create(['color', 'background-color'], {
            duration: motion.base,
            easing: motion.easing,
          }),
          '&:hover': { color: p.text },
          '&.Mui-selected': { color: p.accent },
        },
      },
    },
    MuiTable: { styleOverrides: { root: { ...numeric } } },
    MuiTableCell: {
      styleOverrides: {
        root: {
          borderBottomColor: p.border,
          height: table.rowHeight,
          paddingTop: spacingUnit * 1.5,
          paddingBottom: spacingUnit * 1.5,
        },
        head: {
          backgroundColor: p.surfaceAlt,
          color: p.textSecondary,
          fontWeight: 600,
          fontSize: `${12 / 14}rem`,
          letterSpacing: '0.02em',
        },
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: {
          '&.MuiTableRow-hover:hover': { backgroundColor: alpha(p.accent, table.hoverAlpha) },
          '&.Mui-selected, &.Mui-selected:hover': { backgroundColor: alpha(p.accent, table.selectedAlpha) },
        },
      },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: { root: { borderColor: p.border, borderRadius: shape.radius, backgroundImage: 'none' } },
    },
    MuiDrawer: { styleOverrides: { paper: { borderRight: `1px solid ${p.border}`, boxShadow: 'none' } } },
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'inherit' },
      styleOverrides: {
        root: { backgroundColor: p.surface, boxShadow: 'none', borderBottom: `1px solid ${p.border}` },
      },
    },
    // Dialogs: a tinted header band, a plain body and a neutral footer band (prototype .modal / .dlg-h / .dlg-b / .dlg-f).
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
        root: { padding: dialogSpacing.header, background: p.accentSoft, borderBottom: `1px solid ${p.border}` },
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
        root: { padding: dialogSpacing.footer, background: p.surfaceAlt, borderTop: `1px solid ${p.border}` },
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
        paper: { border: `1px solid ${p.border}`, borderRadius: shape.radius, boxShadow: e.md },
      },
    },
    MuiMenu: { styleOverrides: { paper: { border: `1px solid ${p.border}`, boxShadow: e.md } } },
    MuiMenuItem: {
      styleOverrides: {
        root: {
          '&.Mui-selected': { backgroundColor: p.accentSoft },
          '&.Mui-selected:hover': { backgroundColor: p.accentSoft },
        },
      },
    },
    MuiAlert: { styleOverrides: { root: { borderRadius: shape.radiusSmall, alignItems: 'center' } } },
    MuiLink: {
      defaultProps: { underline: 'hover' },
      styleOverrides: { root: { color: p.accent, fontWeight: 500, transition: transition(theme, 'color') } },
    },
    // Unchecked boxes use inputBorder (3:1 against the surface); checked ones the accent.
    MuiCheckbox: {
      styleOverrides: { root: { color: p.inputBorder, '&.Mui-checked': { color: p.accent }, ...focusRingOn(theme) } },
    },
    MuiRadio: {
      styleOverrides: { root: { color: p.inputBorder, '&.Mui-checked': { color: p.accent }, ...focusRingOn(theme) } },
    },
    MuiSwitch: {
      styleOverrides: {
        // Off: inputBorder track with a surface thumb (3:1 each way); on: the usual half-strength accent track.
        switchBase: {
          color: p.surface,
          '&.Mui-checked': { color: p.accent },
          '&.Mui-checked + .MuiSwitch-track': { backgroundColor: p.accent, opacity: 0.5 },
          ...focusRingOn(theme),
        },
        track: { backgroundColor: p.inputBorder, opacity: 1 },
      },
    },
    MuiSkeleton: { styleOverrides: { root: { backgroundColor: alpha(p.textSecondary, 0.16) } } },
    // Sidebar: the active route (react-router adds `.active`) sits on accentSoft with an accent marker.
    MuiListItemButton: {
      styleOverrides: {
        root: {
          position: 'relative',
          borderRadius: shape.radius,
          '&.active': {
            backgroundColor: p.accentSoft,
            color: p.accent,
            '&::before': {
              content: '""',
              position: 'absolute',
              left: 0,
              top: spacingUnit * 1.5,
              bottom: spacingUnit * 1.5,
              width: sidebar.markerWidth,
              borderRadius: sidebar.markerWidth,
              backgroundColor: p.accent,
            },
            '& .MuiListItemIcon-root': { color: p.accent },
          },
        },
      },
    },
    MuiListSubheader: { styleOverrides: { root: { color: p.textSecondary } } },
  };
}
