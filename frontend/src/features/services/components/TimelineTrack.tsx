import { Box } from '@mui/material';
import { alpha } from '@mui/material/styles';
import type { ServicesTimeline } from '@/shared/api';
import { layoutTimeline, percentOf } from '../lib/timeline';
import { bandLaneHeight, TimelineBand } from './TimelineBand';
import { markLaneHeight, TimelineMark } from './TimelineMark';

const hours = [0, 3, 6, 9, 12, 15, 18, 21, 24] as const;

/**
 * The day from 00:00 to 24:00: the market session (green) and the review cutoff (red line) on the track, the bands of
 * services that are up over it, and the starts under it. The track and its hours are decoration (the card's title line
 * says the market hours and the cutoff in words); the bands and starts are reachable by keyboard and say what they are.
 */
export function TimelineTrack({ timeline }: { timeline: ServicesTimeline }) {
  const { marks, bands, markLanes } = layoutTimeline(timeline);
  const open = timeline.marketOpen;
  const close = timeline.marketClose;
  return (
    <Box sx={{ position: 'relative', pb: 0.5, overflow: 'hidden' }}>
      <Box
        aria-hidden
        sx={{ position: 'relative', height: 26, mb: 1, borderRadius: 1, bgcolor: 'action.hover', border: 0 }}
      >
        {open && close && (
          <Box
            sx={(theme) => ({
              position: 'absolute',
              top: 0,
              bottom: 0,
              left: `${percentOf(open).toFixed(2)}%`,
              width: `${(percentOf(close) - percentOf(open)).toFixed(2)}%`,
              border: 1,
              borderColor: 'success.main',
              borderRadius: 1,
              bgcolor: alpha(theme.palette.success.main, 0.14),
            })}
          />
        )}
        <Box
          sx={{
            position: 'absolute',
            top: -4,
            bottom: -4,
            width: 2,
            left: `${percentOf(timeline.reviewCutoff).toFixed(2)}%`,
            bgcolor: 'error.main',
          }}
        />
        {hours.map((hour) => (
          <Box
            key={hour}
            sx={(theme) => ({
              position: 'absolute',
              top: 5,
              left: `${((hour / 24) * 100).toFixed(2)}%`,
              transform: hour === 0 ? 'none' : hour === 24 ? 'translateX(-100%)' : 'translateX(-50%)',
              px: 0.5,
              fontSize: theme.typography.caption.fontSize,
              color: 'text.secondary',
            })}
          >
            {String(hour).padStart(2, '0')}
          </Box>
        ))}
      </Box>
      {bands.length > 0 && (
        <Box sx={{ position: 'relative', height: bands.length * bandLaneHeight, mb: 0.5 }}>
          {bands.map((band) => (
            <TimelineBand key={band.key} band={band} />
          ))}
        </Box>
      )}
      {marks.length > 0 && (
        <Box sx={{ position: 'relative', height: markLanes * markLaneHeight }}>
          {marks.map((mark) => (
            <TimelineMark key={mark.key} mark={mark} />
          ))}
        </Box>
      )}
    </Box>
  );
}
