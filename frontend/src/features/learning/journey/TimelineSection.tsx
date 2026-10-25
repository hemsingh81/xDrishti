import { Box, Link, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { NonValue } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import { axisOf, lessonBand, modelPins, type Designation } from './bands';
import type { Lesson, Model } from '../api/learningQueries';
import { learningLink } from '../lib/roleText';

interface Props {
  lessons: readonly Lesson[];
  models: readonly Model[];
  designations: readonly Designation[];
  /** The moment open lessons run to (epoch milliseconds), read once by the tab. */
  today: number;
}

const percent = (fraction: number): string => `${(fraction * 100).toFixed(2)}%`;

/** A track with one band or pin on it; the text beside it carries the same facts (the track is decoration). */
function Track({ from, to }: { from: number; to?: number }) {
  return (
    <Box aria-hidden sx={{ position: 'relative', height: 10, bgcolor: 'action.hover', borderRadius: 1 }}>
      <Box
        sx={{
          position: 'absolute',
          top: 0,
          bottom: 0,
          left: percent(from),
          width: to === undefined ? '3px' : percent(Math.max(0, to - from)),
          minWidth: '3px',
          bgcolor: 'text.secondary',
          borderRadius: 1,
        }}
      />
    </Box>
  );
}

/** Lessons as bands from opened to closed (or to today, "open") and model registrations and champion designations as pins, on one time axis. */
export function TimelineSection({ lessons, models, designations, today }: Props) {
  const axis = axisOf(lessons, models, designations, today);
  if (axis === null) {
    return (
      <Box sx={{ display: 'grid', gap: 1 }}>
        <NonValue state={{ kind: 'empty', reason: 'No lessons recorded' }} />
        <NonValue state={{ kind: 'empty', reason: 'No model stored' }} />
      </Box>
    );
  }
  const bands = lessons.map((lesson) => lessonBand(lesson, axis, today));
  const pins = modelPins(models, designations, axis);
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        {formatDate(new Date(axis.start).toISOString())} to {formatDate(new Date(axis.end).toISOString())}. A band shows
        when a finding opened and closed, not that the fix caused a change.
      </Typography>
      {lessons.length === 0 ? (
        <NonValue state={{ kind: 'empty', reason: 'No lessons recorded' }} />
      ) : (
        <Box
          component="ul"
          aria-label="Lesson bands"
          sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 1.25 }}
        >
          {bands.map((band) => (
            <li key={band.id}>
              <Typography component="p" sx={{ overflowWrap: 'anywhere' }}>
                <Link component={RouterLink} to={learningLink({ tab: 'lessons', lesson: band.id })}>
                  {band.label}
                </Link>
                {': '}
                {formatDate(band.startedAt)} to{' '}
                {band.open ? 'today (open)' : formatDate(band.endedAt ?? band.startedAt)}
                {band.result === null ? '' : `, ${band.result}`}
              </Typography>
              <Track from={band.from} to={band.to} />
            </li>
          ))}
        </Box>
      )}
      {models.length === 0 ? (
        <NonValue state={{ kind: 'empty', reason: 'No model stored' }} />
      ) : (
        <Box component="ul" aria-label="Model pins" sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 1.25 }}>
          {pins.map((pin) => (
            <li key={pin.id}>
              <Typography component="p">
                {pin.label}, {formatDate(pin.when)}
              </Typography>
              <Track from={pin.at} />
            </li>
          ))}
        </Box>
      )}
    </Box>
  );
}
