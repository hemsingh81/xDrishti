import { Box, Tooltip, useMediaQuery, useTheme } from '@mui/material';
import { istClockParts, istDayLabel } from '@/shared/lib/istClock';
import { useWallClock } from '@/shared/lib/useWallClock';
import { numeric } from '@/theme';
import { RollingDigit } from './RollingDigit';

const reducedMotion = '(prefers-reduced-motion: reduce)';

function Digits({ text, animate }: { text: string; animate: boolean }) {
  return (
    <>
      {Array.from(text).map((digit, index) => (
        // The position is the identity: the tens digit stays the tens digit, so only a changed digit rolls.
        <RollingDigit key={index} value={digit} animate={animate} />
      ))}
    </>
  );
}

/**
 * The time in India (IST, Asia/Kolkata) as HH:mm:ss, whatever the browser's time zone, with each digit rolling up when
 * it changes. From `sm` it shows seconds; on a phone only HH:mm (and ticks once a minute). Assistive technology gets
 * one label, "HH:mm India Standard Time, <day>", that changes at most once a minute and is never announced; the
 * rolling digits are hidden from it. The tooltip shows the date for the mouse; it adds nothing for assistive technology,
 * whose label already has it (the label wins over the tooltip's).
 */
export function IstClock() {
  const theme = useTheme();
  const seconds = !useMediaQuery(theme.breakpoints.down('sm'));
  const animate = !useMediaQuery(reducedMotion);
  const now = useWallClock(seconds ? 1000 : 60_000);
  const { hours, minutes, seconds: secs } = istClockParts(now);
  const day = istDayLabel(now);
  const label = `${hours}:${minutes} India Standard Time, ${day}`;

  return (
    <Tooltip title={`${day} · India Standard Time`}>
      <Box
        role="timer"
        aria-live="off"
        aria-label={label}
        sx={{
          display: 'inline-flex',
          // On a phone "IST" sits under the time, so the clock stays narrow.
          flexDirection: { xs: 'column', sm: 'row' },
          alignItems: { xs: 'center', sm: 'baseline' },
          flexShrink: 0,
          gap: { xs: 0, sm: 0.75 },
          px: { xs: 0, sm: 1 },
          py: 0.25,
          borderRadius: 1,
          fontWeight: 600,
          fontSize: '0.95rem',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          cursor: 'default',
          ...numeric,
        }}
      >
        <Box component="span" aria-hidden sx={{ display: 'inline-flex', alignItems: 'baseline' }}>
          <Digits text={hours} animate={animate} />
          <Colon />
          <Digits text={minutes} animate={animate} />
          {seconds && (
            <>
              <Colon />
              <Digits text={secs} animate={animate} />
            </>
          )}
        </Box>
        <Box
          component="span"
          aria-hidden
          sx={{ fontSize: '0.65rem', lineHeight: 1, fontWeight: 700, letterSpacing: '0.06em', color: 'text.secondary' }}
        >
          IST
        </Box>
      </Box>
    </Tooltip>
  );
}

function Colon() {
  return (
    <Box component="span" aria-hidden sx={{ px: '1px', opacity: 0.7 }}>
      :
    </Box>
  );
}
