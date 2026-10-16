import { alpha, type Components, type Theme } from '@mui/material/styles';
import { focusRingOn, transition } from './componentHelpers';
import { controlHeight, elevation, motion, palettes, rem, shape, stateAlpha, type ColorMode } from './tokens';

/** Component overrides for controls: buttons, text fields, chips, tabs, segmented control, checkboxes, switches and links. */
export function createControlComponents(theme: Theme, mode: ColorMode): Components<Theme> {
  const p = palettes[mode];
  const e = elevation[mode];
  return {
    // Buttons: 36 px (small 30, large 44), radius 10 (small 8), weight 550, a hairline shadow (prototype `.btn`).
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: ({ ownerState }) => ({
          borderRadius: shape.radiusControl,
          paddingTop: 0,
          paddingBottom: 0,
          transition: transition(theme, 'background-color', 'border-color', 'color'),
          ...(ownerState.variant === 'text' ? {} : { boxShadow: e.sm }),
          ...(ownerState.variant === 'contained' && ownerState.color === 'primary'
            ? { '&:hover': { backgroundColor: alpha(p.accent, 0.88) } }
            : {}),
          ...(ownerState.variant === 'outlined' && ownerState.color === 'primary' ? { borderColor: p.accent } : {}),
          ...(ownerState.variant === 'outlined' && ownerState.color === 'inherit'
            ? { borderColor: p.border, '&:hover': { backgroundColor: p.surfaceStrong, borderColor: p.borderStrong } }
            : {}),
          '&.Mui-disabled': { boxShadow: 'none' },
        }),
        sizeSmall: {
          minHeight: controlHeight.small,
          paddingInline: 10,
          fontSize: rem(12.5),
          borderRadius: shape.radiusSmall,
        },
        sizeMedium: { minHeight: controlHeight.medium, paddingInline: 14 },
        sizeLarge: { minHeight: controlHeight.large, paddingInline: 18, fontSize: rem(15) },
      },
    },
    // Icon buttons: a 36 px rounded square (small 30), like the prototype's `.btn.icon`.
    MuiIconButton: {
      styleOverrides: {
        root: ({ ownerState }) => ({
          transition: transition(theme, 'background-color', 'color'),
          borderRadius: ownerState.size === 'small' ? shape.radiusSmall : shape.radiusControl,
          padding: ownerState.size === 'small' ? 5 : 6,
          ...(ownerState.edge === 'start' ? { marginLeft: ownerState.size === 'small' ? -3 : -6 } : {}),
          ...(ownerState.edge === 'end' ? { marginRight: ownerState.size === 'small' ? -3 : -6 } : {}),
        }),
      },
    },
    // Text fields: every size is 36 px tall with radius 10 (prototype `.input`); the floating label is centred to match.
    MuiFormControl: { defaultProps: { size: 'small' } },
    MuiTextField: { defaultProps: { size: 'small' } },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: shape.radiusControl,
          backgroundColor: p.surface,
          minHeight: controlHeight.medium,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: p.inputBorder },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: p.accent },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: p.accent, borderWidth: 2 },
          '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: p.bad },
          '&.Mui-disabled': { backgroundColor: theme.palette.action.disabledBackground },
          '&.MuiInputBase-multiline': { paddingTop: 8, paddingBottom: 8 },
        },
        input: {
          paddingTop: 8,
          paddingBottom: 8,
          '&::placeholder': { color: p.textSecondary, opacity: stateAlpha.placeholder },
        },
      },
    },
    MuiInputLabel: {
      styleOverrides: {
        root: {
          '&.MuiInputLabel-outlined:not(.MuiInputLabel-shrink)': { transform: 'translate(14px, 8px) scale(1)' },
        },
      },
    },
    MuiFormHelperText: { styleOverrides: { root: { fontSize: rem(12), marginLeft: 2 } } },
    // Chips: a tonal pill (soft tint behind coloured text), like the prototype's `.chip` — outlined and filled look alike.
    // A pressed toggle chip (`aria-pressed="true"`: the selected filter) also gets a border in its text colour, so the
    // selection does not rest on the colour of the tint alone.
    MuiChip: {
      styleOverrides: {
        root: ({ ownerState }) => {
          const tones: Record<string, { bg: string; fg: string }> = {
            default: { bg: p.surfaceStrong, fg: p.textSecondary },
            primary: { bg: p.accentSoft, fg: p.accent },
            success: { bg: p.goodSoft, fg: p.good },
            error: { bg: p.badSoft, fg: p.bad },
            warning: { bg: p.warnSoft, fg: p.warn },
            info: { bg: p.infoSoft, fg: p.info },
          };
          const tone = tones[ownerState.color ?? 'default'] ?? { bg: p.surfaceStrong, fg: p.textSecondary };
          return {
            fontWeight: 600,
            fontSize: rem(11.5),
            borderRadius: shape.radiusPill,
            backgroundColor: tone.bg,
            color: tone.fg,
            border: '1px solid transparent',
            '&[aria-pressed="true"]': { borderColor: tone.fg },
            '& .MuiChip-icon, & .MuiChip-deleteIcon': { color: 'inherit' },
            '&.MuiChip-clickable:hover, &.MuiChip-clickable:focus': {
              backgroundColor: tone.bg,
              filter: 'brightness(0.96)',
            },
          };
        },
        sizeSmall: { height: 22 },
        sizeMedium: { height: 26 },
        label: ({ ownerState }) => ({
          paddingLeft: ownerState.size === 'small' ? 9 : 11,
          paddingRight: ownerState.size === 'small' ? 9 : 11,
        }),
        // Room to the left of the icon, so the icon does not sit on the chip's edge.
        icon: ({ ownerState }) => ({ marginLeft: ownerState.size === 'small' ? 6 : 8, marginRight: -2 }),
      },
    },
    MuiTabs: {
      styleOverrides: {
        root: { borderBottom: `1px solid ${p.border}` },
        indicator: { height: 2, borderRadius: 2, backgroundColor: p.accent },
      },
    },
    MuiTab: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          minHeight: 44,
          minWidth: 0,
          padding: '12px',
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
    // Segmented control (prototype `.seg`): a tray with a raised, accent-coloured selected segment.
    MuiToggleButtonGroup: {
      styleOverrides: {
        root: {
          gap: 2,
          padding: 3,
          backgroundColor: p.surfaceAlt,
          border: `1px solid ${p.border}`,
          borderRadius: shape.radiusControl,
          '& .MuiToggleButtonGroup-grouped.MuiToggleButtonGroup-grouped': {
            border: 0,
            margin: 0,
            borderRadius: 7,
          },
        },
      },
    },
    MuiToggleButton: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          color: p.textSecondary,
          border: `1px solid ${p.border}`,
          borderRadius: 7,
          padding: '5px 12px',
          '&:hover': { color: p.text, backgroundColor: alpha(p.accent, 0.05) },
          '&.Mui-selected, &.Mui-selected:hover': {
            color: p.accent,
            backgroundColor: p.surface,
            boxShadow: `${e.sm}, 0 0 0 1px ${p.border}`,
          },
        },
        sizeSmall: { padding: '3px 10px', fontSize: rem(12.5) },
      },
    },
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
  };
}
