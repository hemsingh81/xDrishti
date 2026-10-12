import { useEffect, useRef, type RefObject } from 'react';

export type InputModality = 'keyboard' | 'pointer';

/**
 * Tracks whether the last thing the person did was press a key or use a pointer (mouse, pen, touch). Read `.current`
 * when something happens later (a server answer) to know how the action that led to it was started. Starts as pointer,
 * the safe default: nothing moves focus unless a key was used.
 */
export function useInputModality(): RefObject<InputModality> {
  const modality = useRef<InputModality>('pointer');
  useEffect(() => {
    const keyboard = () => {
      modality.current = 'keyboard';
    };
    const pointer = () => {
      modality.current = 'pointer';
    };
    document.addEventListener('keydown', keyboard, true);
    document.addEventListener('pointerdown', pointer, true);
    document.addEventListener('mousedown', pointer, true);
    document.addEventListener('touchstart', pointer, true);
    return () => {
      document.removeEventListener('keydown', keyboard, true);
      document.removeEventListener('pointerdown', pointer, true);
      document.removeEventListener('mousedown', pointer, true);
      document.removeEventListener('touchstart', pointer, true);
    };
  }, []);
  return modality;
}
