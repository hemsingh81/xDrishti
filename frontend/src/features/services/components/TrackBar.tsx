import { Box, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import type { ServicesTimeline } from '@/shared/api';
import { hourTicks, percentOf, trackWidth } from '../lib/timeline';
import { hoursRowHeight, trackHeight } from './timelineMetrics';

/** A label shorter than this (px on the track) does not fit inside the market band. */
const minLabelPx = 150;
/** After this much of the day the cutoff label is written to the left of its line. */
const flipLabelFrom = 78;

/** Width (px) a label of this many characters takes on the track, with a little air. */
const labelPx = (chars: number) => chars * 7 + 20;

/**
 * The day from 00:00 to 24:00 as a track: hour ticks (every third one full height), the market session as a green band
 * with its hours, the review cutoff as a red line with its time, and the hour numbers under it. All of it is decoration
 * for the eye: the card's title line says the market hours and the cutoff in words.
 */
export function TrackBar({ timeline, nowPercent }: { timeline: ServicesTimeline; nowPercent: number | null }) {
  const open = timeline.marketOpen;
  const close = timeline.marketClose;
  const cutoff = percentOf(timeline.reviewCutoff);
  const bandLeft = open ? percentOf(open) : 0;
  const bandWidth = open && close ? percentOf(close) - bandLeft : 0;
  const roomForLabel = (bandWidth / 100) * trackWidth >= minLabelPx;
  // A label the "now" line would run through moves to the other side.
  const crosses = (from: number, chars: number) =>
    nowPercent !== null && nowPercent >= from && nowPercent <= from + (labelPx(chars) / trackWidth) * 100;
  const marketLabel = open && close ? `Market ${open}–${close}` : '';
  const marketRight = crosses(bandLeft, marketLabel.length);
  const cutoffLabel = `Review cutoff ${timeline.reviewCutoff}`;
  const cutoffLeft = cutoff > flipLabelFrom || crosses(cutoff, cutoffLabel.length);

  return (
    <Box aria-hidden>
      <Box
        sx={{
          position: 'relative',
          height: trackHeight,
          overflow: 'hidden',
          border: 1,
          borderColor: 'divider',
          borderRadius: 1.5,
          bgcolor: 'action.hover',
        }}
      >
        {open && close && (
          <Box
            sx={(theme) => ({
              position: 'absolute',
              top: 0,
              bottom: 0,
              left: `${bandLeft.toFixed(2)}%`,
              width: `${bandWidth.toFixed(2)}%`,
              borderLeft: 2,
              borderRight: 2,
              borderStyle: 'solid',
              borderColor: 'success.main',
              bgcolor: alpha(theme.palette.success.main, 0.16),
            })}
          >
            {roomForLabel && (
              <Typography
                variant="caption"
                sx={{
                  position: 'absolute',
                  top: 6,
                  ...(marketRight ? { right: 8 } : { left: 8 }),
                  fontWeight: 700,
                  color: 'success.main',
                  whiteSpace: 'nowrap',
                }}
              >
                {marketLabel}
              </Typography>
            )}
          </Box>
        )}
        {hourTicks.map(({ hour, major }) => (
          <Box
            key={hour}
            sx={{
              position: 'absolute',
              bottom: 0,
              left: `${((hour / 24) * 100).toFixed(2)}%`,
              width: '1px',
              height: major ? '100%' : 6,
              bgcolor: major ? 'divider' : 'text.disabled',
              opacity: major ? 0.7 : 0.5,
            }}
          />
        ))}
        <Box
          sx={{
            position: 'absolute',
            top: 0,
            bottom: 0,
            left: `${cutoff.toFixed(2)}%`,
            width: 2,
            bgcolor: 'error.main',
          }}
        />
        <Typography
          variant="caption"
          sx={{
            position: 'absolute',
            bottom: 5,
            fontWeight: 700,
            color: 'error.main',
            whiteSpace: 'nowrap',
            ...(cutoffLeft
              ? { right: `calc(${(100 - cutoff).toFixed(2)}% + 8px)` }
              : { left: `calc(${cutoff.toFixed(2)}% + 8px)` }),
          }}
        >
          {cutoffLabel}
        </Typography>
      </Box>
      <Box sx={{ position: 'relative', height: hoursRowHeight }}>
        {hourTicks
          .filter((tick) => tick.major)
          .map(({ hour }) => (
            <Typography
              key={hour}
              variant="caption"
              color="text.secondary"
              sx={{
                position: 'absolute',
                top: 2,
                left: `${((hour / 24) * 100).toFixed(2)}%`,
                transform: 'translateX(-50%)',
                fontVariantNumeric: 'tabular-nums',
              }}
            >
              {String(hour).padStart(2, '0')}
            </Typography>
          ))}
      </Box>
    </Box>
  );
}
