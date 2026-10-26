import { QueryClient } from '@tanstack/react-query';
import { clearOfflineCaches } from '@/shared/lib/offlineCaches';
import { endSession } from './api/sessionQuery';

vi.mock('@/shared/lib/offlineCaches', () => ({ clearOfflineCaches: vi.fn(() => Promise.resolve()) }));

describe('endSession', () => {
  it.each(['signed_out', 'expired', 'none'] as const)(
    'clears the offline caches and worker when it ends as %s',
    (reason) => {
      vi.mocked(clearOfflineCaches).mockClear();
      endSession(new QueryClient(), reason);
      expect(clearOfflineCaches).toHaveBeenCalledTimes(1);
    },
  );
});
