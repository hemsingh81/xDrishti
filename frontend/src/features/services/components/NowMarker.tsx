import { Box } from '@mui/material';
import { istClockParts } from '@/shared/lib/istClock';
import { minutesPerDay } from '../lib/timeline';
import { nowRowHeight, trackHeight } from './timelineMetrics';

/**
 * "Now" in India time: a pill above the track and a line down through it, at the current minute. The pill keeps inside
 * the track near midnight. It is the one visible-to-everyone part of the track, so it says its time in words.
 */
export function NowMarker({ minutes, at }: { minutes: number; at: number }) {
  const { hours, minutes: mins } = istClockParts(at);
  const left = `${((minutes / minutesPerDay) * 100).toFixed(3)}%`;
  return (
    <>
      <Box
        sx={{
          position: 'absolute',
          top: 0,
          left: `clamp(48px, ${left}, calc(100% - 48px))`,
          transform: 'translateX(-50%)',
          px: 1,
          height: 20,
          display: 'flex',
          alignItems: 'center',
          borderRadius: 1,
          bgcolor: 'primary.main',
          color: 'primary.contrastText',
          fontSize: '0.75rem',
          fontWeight: 700,
          whiteSpace: 'nowrap',
          fontVariantNumeric: 'tabular-nums',
          zIndex: 2,
        }}
      >
        Now {hours}:{mins} IST
      </Box>
      <Box
        aria-hidden
        sx={{
          position: 'absolute',
          top: 20,
          left,
          width: 2,
          height: nowRowHeight - 20 + trackHeight,
          ml: '-1px',
          bgcolor: 'primary.main',
          zIndex: 1,
        }}
      />
    </>
  );
}
