import { renderRoutes } from '@/test/render';
import { LearningPage } from './components/LearningPage';

/** Renders the Learning screen at `/learning` + `search` (e.g. "?tab=setups") with the real providers. */
export function renderLearning(search = '') {
  return renderRoutes([{ path: '/learning', element: <LearningPage /> }], { initialPath: `/learning${search}` });
}
