import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatAsOf, formatCount, NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { experimentQuery, lessonQuery, type Lesson } from '../api/learningQueries';
import { outcomeWords, wordsOf } from '../lib/outcomeText';
import { lessonResultWord, moveNote } from '../lib/roleText';
import { DrawerSection, StepList } from './DrawerSection';
import { RecordDrawer } from './RecordDrawer';
import { RValue } from './RValue';
import { TermRow } from './TermRow';

/** One lesson in full: its history and, when it has a result, the before and after with their n from the experiment. */
export function LessonDrawer({ id, onClose }: { id: number; onClose: () => void }) {
  const query = useQuery(lessonQuery(id));
  return (
    <RecordDrawer title={`Lesson #${id}`} closeLabel="Close lesson" onClose={onClose}>
      {query.isPending && <LoadingState label="Loading the lesson…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && <Body lesson={query.data} />}
    </RecordDrawer>
  );
}

function Body({ lesson }: { lesson: Lesson }) {
  return (
    <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>{lesson.problem}</Typography>
      <DrawerSection title="Result">
        <Typography>{lessonResultWord(lesson.result)}</Typography>
        {lesson.result !== null && <Evidence lesson={lesson} />}
      </DrawerSection>
      <DrawerSection title="History">
        <StepList label="Steps of this lesson" steps={lesson.history} formatAt={formatAsOf} />
      </DrawerSection>
      <Typography variant="body2">
        May move to: {lesson.nextStates.length > 0 ? lesson.nextStates.join(', ') : 'nowhere'}. {moveNote('lesson')}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {lesson.note}
      </Typography>
    </>
  );
}

/** Numbers only here, each with its n (the trade counts live in the experiment). Without the experiment there is no n. */
function Evidence({ lesson }: { lesson: Lesson }) {
  const experiment = useQuery({ ...experimentQuery(lesson.experimentId ?? 0), enabled: lesson.experimentId !== null });
  if (lesson.experimentId === null) {
    return <NonValue state={{ kind: 'unavailable', reason: 'No experiment is linked, so there is no trade count.' }} />;
  }
  if (experiment.isPending) return <LoadingState label="Loading the experiment…" />;
  if (experiment.isError) {
    return (
      <NonValue state={{ kind: 'unavailable', reason: `Experiment #${lesson.experimentId} could not be read.` }} />
    );
  }
  const e = experiment.data;
  return (
    <Box component="dl" sx={{ m: 0, mt: 1, display: 'grid', gap: 0.5 }}>
      <TermRow term="Pooled mean R before">
        <RValue value={lesson.beforeMeanR} n={e.baselineTrades} missing="The server stored no mean R before." />
      </TermRow>
      <TermRow term="Pooled mean R after">
        <RValue value={lesson.afterMeanR} n={e.candidateTrades} missing="The server stored no mean R after." />
      </TermRow>
      <TermRow term="Total R before">
        <RValue value={lesson.beforeTotalR} missing="The server stored no total R before." />
      </TermRow>
      <TermRow term="Total R after">
        <RValue value={lesson.afterTotalR} missing="The server stored no total R after." />
      </TermRow>
      <TermRow term="Experiment outcome">{wordsOf(outcomeWords, lesson.experimentOutcome ?? e.outcome).word}</TermRow>
      <TermRow term="Folds used">{formatCount(e.usedFolds)}</TermRow>
    </Box>
  );
}
