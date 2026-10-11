import { renderRoutes } from '@/test/render';
import { InstrumentsPage } from './components/InstrumentsPage';

/** Renders the Instruments screen at `/instruments` + `search` (e.g. "?type=etf") with the real providers. */
export function renderInstruments(search = '') {
  return renderRoutes([{ path: '/instruments', element: <InstrumentsPage /> }], {
    initialPath: `/instruments${search}`,
  });
}
