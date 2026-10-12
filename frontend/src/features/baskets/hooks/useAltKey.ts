import { useCallback, useEffect, useRef, useState } from 'react';

interface ModifierEvent {
  altKey: boolean;
}

/**
 * Whether Alt / Option is held while `active` (a drag is running). `held` is the state for showing "move" or "copy";
 * `isHeld()` is the live value for the moment of the drop. The key is read from key and pointer events, so it works for
 * the mouse, touch with a keyboard and the keyboard drag; losing focus resets it.
 */
export function useAltKey(active: boolean) {
  const [held, setHeld] = useState(false);
  const ref = useRef(false);

  useEffect(() => {
    if (!active) return undefined;
    const update = (event: Event) => {
      const next = (event as unknown as ModifierEvent).altKey;
      if (typeof next !== 'boolean') return;
      ref.current = next;
      setHeld(next);
    };
    const reset = () => {
      ref.current = false;
      setHeld(false);
    };
    const events = ['keydown', 'keyup', 'pointermove', 'pointerup', 'mousemove', 'mouseup'] as const;
    for (const name of events) window.addEventListener(name, update, true);
    window.addEventListener('blur', reset);
    return () => {
      for (const name of events) window.removeEventListener(name, update, true);
      window.removeEventListener('blur', reset);
      reset();
    };
  }, [active]);

  const isHeld = useCallback(() => ref.current, []);
  return { held, isHeld };
}
