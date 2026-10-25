import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { lessonsQuery } from '../api/learningQueries';
import { lessonStates, parseId } from '../lib/roleText';
import { FilterBar } from './FilterBar';
import { LessonCard } from './LessonCard';
import { LessonDrawer } from './LessonDrawer';
import { Panel } from './Panel';

/** Lessons: what the monitors found wrong, newest 50, with the state filter and the open lesson in the address. */
export function LessonsTab() {
  const url = useUrlState();
  const state = url.choice('state', lessonStates, 'all');
  const open = parseId(url.text('lesson'));
  const query = useQuery(lessonsQuery(state));
  return (
    <Panel
      title="Lessons"
      actions={
        <FilterBar
          label="Lesson state"
          values={lessonStates}
          value={state}
          onChange={(next) => {
            url.update({ state: next === 'all' ? null : next });
          }}
        />
      }
    >
      {query.isPending && <LoadingState label="Loading the lessons…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data?.length === 0 && (
        <NonValue
          state={{
            kind: 'empty',
            reason:
              state === 'all'
                ? 'No lesson stored yet; run `xd learn lesson`.'
                : `No ${state} lesson. Try another state.`,
          }}
        />
      )}
      {query.data && query.data.length > 0 && (
        <>
          <Box
            component="ul"
            aria-label="Lessons, newest first"
            sx={{ m: 0, p: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}
          >
            {query.data.map((lesson) => (
              <LessonCard
                key={lesson.id}
                lesson={lesson}
                onOpen={() => {
                  url.update({ lesson: String(lesson.id) });
                }}
              />
            ))}
          </Box>
          <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
            {query.data[0]?.note}
          </Typography>
        </>
      )}
      {open !== null && (
        <LessonDrawer
          id={open}
          onClose={() => {
            url.update({ lesson: null });
          }}
        />
      )}
    </Panel>
  );
}
