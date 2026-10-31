import { plannedNavItems } from './navigation';
import { plannedPaths } from './plannedPaths';

describe('plannedPaths', () => {
  it('lists the paths of the navigation entries that still wait for a phase', () => {
    expect([...plannedPaths]).toEqual(plannedNavItems.map((i) => i.path));
  });
});
