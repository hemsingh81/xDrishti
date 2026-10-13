import { Box } from '@mui/material';
import { useEffect, useEffectEvent, useRef } from 'react';
import type { ServicesTimeline } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { istMinutesOfDay } from '@/shared/lib/istClock';
import { useWallClock } from '@/shared/lib/useWallClock';
import { layoutTimeline, minutesPerDay, trackWidth } from '../lib/timeline';
import { NowMarker } from './NowMarker';
import { bandLaneHeight, TimelineBand } from './TimelineBand';
import { markLaneHeight, TimelineMark } from './TimelineMark';
import { nowRowHeight } from './timelineMetrics';
import { TrackBar } from './TrackBar';

/**
 * The trading-day timeline: the track (market session, review cutoff, hours), a live "now" marker while the timeline is
 * of today, then the services that are up all or part of the day, then the starts as pills, stacked in rows so no text
 * overlaps. The track is laid out for 900 px: on a narrower screen it scrolls sideways, starting at "now". The track and
 * its hours are decoration; the bands and starts are reachable by keyboard and say what they are.
 */
export function TimelineTrack({ timeline }: { timeline: ServicesTimeline }) {
  const { marks, bands, markLanes } = layoutTimeline(timeline);
  // The marker moves once a minute.
  const now = useWallClock(60_000);
  const today = istDayIso(now) === timeline.date;
  const minutes = istMinutesOfDay(now);
  const scroller = useRef<HTMLDivElement>(null);

  // On a screen narrower than the track, bring "now" (or the middle of the day) into view when the timeline is shown.
  const reveal = useEffectEvent(() => {
    const el = scroller.current;
    if (el && el.scrollWidth > el.clientWidth) {
      const shown = today ? minutes : minutesPerDay / 2;
      el.scrollLeft = Math.max(0, (shown / minutesPerDay) * el.scrollWidth - el.clientWidth / 2);
    }
  });
  // Only for a new timeline, not every minute (which would fight the reader's scrolling).
  useEffect(() => {
    reveal();
  }, [timeline.date]);

  return (
    <Box ref={scroller} sx={{ overflowX: 'auto', pb: 0.5 }}>
      <Box sx={{ minWidth: trackWidth, px: 2 }}>
        <Box sx={{ position: 'relative', pt: today ? `${String(nowRowHeight)}px` : 0 }}>
          {today && <NowMarker minutes={minutes} at={now} />}
          <TrackBar timeline={timeline} nowPercent={today ? (minutes / minutesPerDay) * 100 : null} />
          {bands.length > 0 && (
            <Box sx={{ position: 'relative', height: bands.length * bandLaneHeight, mt: 0.5 }}>
              {bands.map((band) => (
                <TimelineBand key={band.key} band={band} />
              ))}
            </Box>
          )}
          {marks.length > 0 && (
            <Box sx={{ position: 'relative', height: markLanes * markLaneHeight, mt: 0.5 }}>
              {marks.map((mark) => (
                <TimelineMark key={mark.key} mark={mark} />
              ))}
            </Box>
          )}
        </Box>
      </Box>
    </Box>
  );
}
