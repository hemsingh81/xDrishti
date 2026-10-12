import type { RequestHandler } from 'msw';
import { createQualityFake, type QualityFake } from '@/test/qualityFake';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { QualityPage } from './components/QualityPage';

/** Renders the Data quality screen at `/quality` + `search` (e.g. "?status=fail") over the stateful quality fake. */
export function renderQuality({
  search = '',
  fake = createQualityFake(),
  handlers = [],
}: {
  search?: string;
  fake?: QualityFake;
  /** Handlers that win over the fake's (a read that never answers). */
  handlers?: RequestHandler[];
} = {}) {
  server.use(...handlers, ...fake.handlers);
  const result = renderRoutes([{ path: '/quality', element: <QualityPage /> }], { initialPath: `/quality${search}` });
  return { ...result, fake };
}
