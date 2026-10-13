import { ButtonBase, Tooltip } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { percentOf, type TimelineBandItem } from '../lib/timeline';

export const bandLaneHeight = 28;
const pillHeight = 22;

/**
 * A part of the day a service is up, as a pill under the track (a window is solid, an interval dashed). A button only
 * so that the keyboard reaches it: its name (and the tooltip) says how long it is up.
 */
export function TimelineBand({ band }: { band: TimelineBandItem }) {
  const left = percentOf(band.from);
  const width = percentOf(band.to) - left;
  return (
    <Tooltip title={band.description} placement="top" arrow>
      <ButtonBase
        aria-label={band.description}
        sx={(theme) => ({
          position: 'absolute',
          top: band.lane * bandLaneHeight,
          left: `${left.toFixed(2)}%`,
          width: `${width.toFixed(2)}%`,
          height: pillHeight,
          justifyContent: 'flex-start',
          px: 1,
          overflow: 'hidden',
          whiteSpace: 'nowrap',
          textOverflow: 'ellipsis',
          border: 1,
          borderStyle: band.dashed ? 'dashed' : 'solid',
          borderColor: 'primary.main',
          borderRadius: '999px',
          bgcolor: alpha(theme.palette.primary.main, 0.12),
          color: 'primary.main',
          fontSize: theme.typography.caption.fontSize,
          fontWeight: 650,
          '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.2) },
        })}
      >
        <span aria-hidden>{band.label}</span>
      </ButtonBase>
    </Tooltip>
  );
}
