import { alpha, type SxProps, type Theme } from '@mui/material/styles';
import { numeric } from '@/theme';

export type PillTone = 'success' | 'warning' | 'error' | 'neutral';

/** The look of one item of the status ribbon: a compact bordered pill with tabular figures; a tone tints it. */
export const pillSx =
  (tone: PillTone = 'neutral'): Exclude<SxProps<Theme>, readonly unknown[]> =>
  (theme: Theme) => ({
    display: 'inline-flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: '4px 10px',
    minHeight: 38,
    px: 1.75,
    py: 0.5,
    border: 1,
    borderRadius: 1,
    borderColor: tone === 'neutral' ? 'divider' : `${tone}.main`,
    bgcolor: tone === 'neutral' ? 'background.paper' : alpha(theme.palette[tone].main, 0.08),
    color: 'text.primary',
    font: 'inherit',
    fontSize: theme.typography.body2.fontSize,
    textAlign: 'left',
    ...numeric,
  });
