import { screen, waitFor, within } from '@testing-library/react';

/**
 * The newest toast on the page (the last in document order, whatever its severity). A toast is a `status` (success, info) or an `alert` (warning, error) in the
 * "Notifications" region of the ToastProvider, so tests that do not care which severity it has look for either there
 * (other live regions on the page are not toasts).
 */
export async function findToast(): Promise<HTMLElement> {
  return waitFor(() => {
    const stack = within(screen.getByRole('region', { name: 'Notifications' }));
    const newest = [...stack.queryAllByRole('alert'), ...stack.queryAllByRole('status')]
      .sort((a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1))
      .at(-1);
    if (!newest) throw new Error('No toast on the page');
    return newest;
  });
}
