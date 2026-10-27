import { http } from 'msw';
import { problemResponse, server } from '../src/test/server';

/** The learning studies have none stored (a 404 each): the screens that do not show studies mount this. */
export const noStudies = () => {
  server.use(
    http.get('/api/learning/entry-windows', () =>
      problemResponse({ title: 'None stored.', code: 'learning.entry_windows_not_found' }, 404),
    ),
    http.get('/api/learning/meta-policy', () =>
      problemResponse({ title: 'None stored.', code: 'learning.meta_policy_not_found' }, 404),
    ),
  );
};
