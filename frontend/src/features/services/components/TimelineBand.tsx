import { ButtonBase, Tooltip } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { percentOf, type TimelineBandItem } from '../lib/timeline';

export const bandLaneHeight = 20;

/** A part of the day a service is up (a window; an interval is dashed). A button only so that the keyboard reaches it. */
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
          height: bandLaneHeight - 4,
          justifyContent: 'flex-start',
          px: 0.75,
          overflow: 'hidden',
          whiteSpace: 'nowrap',
          border: 1,
          borderStyle: band.dashed ? 'dashed' : 'solid',
          borderColor: 'primary.main',
          borderRadius: 0.75,
          bgcolor: alpha(theme.palette.primary.main, 0.1),
          color: 'primary.main',
          fontSize: theme.typography.caption.fontSize,
          fontWeight: 600,
        })}
      >
        <span aria-hidden>{band.label}</span>
      </ButtonBase>
    </Tooltip>
  );
}
