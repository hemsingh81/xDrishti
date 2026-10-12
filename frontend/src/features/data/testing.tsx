import type { RequestHandler } from 'msw';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { DataPage } from './components/DataPage';

/** Renders the Data & import screen at `/data` + `search` (e.g. "?status=behind") over the stateful data fake. */
export function renderData({
  search = '',
  fake = createDataFake(),
  handlers = [],
}: {
  search?: string;
  fake?: DataFake;
  /** Handlers that win over the fake's (a read that never answers). */
  handlers?: RequestHandler[];
} = {}) {
  server.use(...handlers, ...fake.handlers);
  const result = renderRoutes(
    [
      { path: '/data', element: <DataPage /> },
      { path: '/accounts', element: <p>Accounts screen</p> },
    ],
    { initialPath: `/data${search}` },
  );
  return { ...result, fake };
}
