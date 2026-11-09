import type { Screen } from './matrix';

/** AU-09: the bare `/settings` view, collapsed on the five essentials (the default handlers serve the catalog). */
export const settingsScreens: Screen[] = [
  { id: 'AU-09-settings-essentials', route: '/settings', mounts: { full: () => undefined } },
];
