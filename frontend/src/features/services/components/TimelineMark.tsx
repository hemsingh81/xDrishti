import { Box, ButtonBase, Tooltip } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { percentOf, type TimelineMarkItem } from '../lib/timeline';

export const markLaneHeight = 28;
const pillHeight = 22;
/** The dot sits 12 px into the pill, so it is centred on the start time. */
const dotOffset = 12;

/**
 * One start on the timeline: a pill with a dot at its time, the time in bold and the name. It is a button only so that
 * the keyboard reaches it: its name (and the tooltip) is the sentence; the text in the pill is for the eye. A start that
 * follows another service has an info-coloured dot and border and a "↳" in front. A start near the right edge is
 * written to the left of its dot, so the pill stays inside the track.
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
            ? { right: `calc(${(100 - Number(position)).toFixed(2)}% - ${String(dotOffset)}px)` }
            : { left: `calc(${position}% - ${String(dotOffset)}px)` }),
          flexDirection: mark.flip ? 'row-reverse' : 'row',
          gap: 0.75,
          height: pillHeight,
          px: 1,
          border: 1,
          borderColor: mark.after ? 'info.main' : 'divider',
          borderRadius: '999px',
          bgcolor: 'background.paper',
          whiteSpace: 'nowrap',
          fontSize: theme.typography.caption.fontSize,
          color: 'text.primary',
          '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.1) },
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
        <span aria-hidden>
          {mark.after && <Box component="span" sx={{ color: 'info.main', fontWeight: 700 }}>{`↳ `}</Box>}
          <Box component="b" sx={{ fontWeight: 700 }}>
            {mark.time}
          </Box>{' '}
          {mark.name}
        </span>
      </ButtonBase>
    </Tooltip>
  );
}
