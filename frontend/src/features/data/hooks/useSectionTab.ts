import { useEffect, type RefObject } from 'react';
import { useSearchParams } from 'react-router';

/**
 * A link such as the bell's (`/data?section=actions&actions=suggested`) names the section it wants (the tab shows it:
 * see `resolveTab`). Once the page is ready the focus moves to the open tab's panel (it scrolls into view and a screen
 * reader is told where it went), and the old parameter is dropped so that Back and a reload do not do it again.
 * `ready` is true once the panel exists.
 */
export function useSectionTab(panel: RefObject<HTMLElement | null>, ready: boolean) {
  const [params, setParams] = useSearchParams();
  const section = params.get('section');
  useEffect(() => {
    if (section === null || !ready) return;
    panel.current?.focus();
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('section');
        return next;
      },
      { replace: true },
    );
  }, [section, ready, panel, setParams]);
}
