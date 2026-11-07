import { renderRoutes } from '@/test/render';
import { ReadinessPage } from './components/ReadinessPage';

/** Renders the Readiness screen at `/readiness` + `search` (e.g. "?strategy=vwap&version=1") with the real providers. */
export function renderReadiness(search = '') {
  return renderRoutes([{ path: '/readiness', element: <ReadinessPage /> }], { initialPath: `/readiness${search}` });
}
