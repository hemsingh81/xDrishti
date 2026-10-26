import { Alert } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink, useParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { entryQuery, hypothesisQuery, lessonQuery } from '../api/movesApi';
import { useCapability } from '../api/capabilitiesRead';
import { HypothesisMove } from './HypothesisMove';
import { LessonMove } from './LessonMove';
import { RevertMove } from './RevertMove';

const titles: Record<string, string> = {
  lesson: 'Move a lesson',
  hypothesis: 'Move a hypothesis',
  revert: 'Revert a cell move',
};

/** One learning move: /owner-acts/lesson/:id, /owner-acts/hypothesis/:id or /owner-acts/revert/:id. Off until the owner switches `learning_moves_ui` on. */
export function MovePage() {
  const { kind = '', id = '' } = useParams();
  const number = /^[1-9]\d{0,15}$/.test(id) ? Number(id) : null;
  const { enabled, loaded } = useCapability('learning_moves_ui');
  const known = kind in titles && number !== null;

  return (
    <>
      <PageHeader
        title={titles[kind] ?? 'Owner act'}
        description="A move is one new row with your reason and your password. Nothing is applied to a strategy, a plan or a setting."
      />
      {!known && <Alert severity="warning">There is nothing to move at this address.</Alert>}
      {known && !loaded && <LoadingState />}
      {known && loaded && !enabled && (
        <Alert severity="info">
          Moving from the screen is switched off. Switch it on in{' '}
          <RouterLink to="/owner-acts">the owner-act switches</RouterLink>.
        </Alert>
      )}
      {known && enabled && kind === 'lesson' && <Lesson id={number} />}
      {known && enabled && kind === 'hypothesis' && <Hypothesis id={number} />}
      {known && enabled && kind === 'revert' && <Revert id={number} />}
    </>
  );
}

function Lesson({ id }: { id: number }) {
  const query = useQuery(lessonQuery(id));
  if (query.isPending) return <LoadingState />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <LessonMove lesson={query.data} />;
}

function Hypothesis({ id }: { id: number }) {
  const query = useQuery(hypothesisQuery(id));
  if (query.isPending) return <LoadingState />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <HypothesisMove hypothesis={query.data} />;
}

function Revert({ id }: { id: number }) {
  const query = useQuery(entryQuery(id));
  if (query.isPending) return <LoadingState />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <RevertMove entry={query.data} />;
}
