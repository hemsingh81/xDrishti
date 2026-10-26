import { useEffect } from 'react';
import { useCapability } from '@/features/owner-acts';

/** Draws nothing. Once the switch `pwa` is known (signed in), registers the worker (ON) or removes it and its caches (OFF). */
export function PwaGate() {
  const { enabled, loaded, failed } = useCapability('pwa');
  useEffect(() => {
    if (!loaded && !failed) return;
    // Unknown (the read failed) counts as OFF: the kill switch fails safe and never registers.
    void import('@/shared/lib/pwaControl').then((module) => module.applyPwa(loaded && enabled));
  }, [enabled, loaded, failed]);
  return null;
}
