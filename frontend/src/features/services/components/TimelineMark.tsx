import { Box, ButtonBase, Tooltip } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { percentOf, type TimelineMarkItem } from '../lib/timeline';

export const markLaneHeight = 22;

/**
 * One start on the timeline: a dot with its time and name. It is a button only so that the keyboard reaches it: its
 * name (and the tooltip) is the sentence; the label next to the dot is for the eye. A start that follows another
 * service has an info-coloured dot and a "↳" in its label.
 */
export function TimelineMark({ mark }: { mark: TimelineMarkItem }) {
  const position = percentOf(mark.time).toFixed(2);
  return (
    <Tooltip title={mark.description} placement="top" arrow>
      <ButtonBase
        aria-label={mark.description}
        sx={(theme) => ({
          position: 'absolute',
          top: mark.lane * markLaneHeight,
          ...(mark.flip
            ? { right: `calc(${(100 - Number(position)).toFixed(2)}% - 6px)` }
            : { left: `calc(${position}% - 6px)` }),
          flexDirection: mark.flip ? 'row-reverse' : 'row',
          gap: 0.5,
          height: markLaneHeight - 4,
          px: 0.5,
          borderRadius: 0.5,
          whiteSpace: 'nowrap',
          fontSize: theme.typography.caption.fontSize,
          color: 'text.primary',
          '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.08) },
        })}
      >
        <Box
          component="span"
          aria-hidden
          sx={{
            width: 8,
            height: 8,
            flex: 'none',
            borderRadius: '50%',
            bgcolor: mark.after ? 'info.main' : 'primary.main',
          }}
        />
        <span aria-hidden>{mark.label}</span>
      </ButtonBase>
    </Tooltip>
  );
}
