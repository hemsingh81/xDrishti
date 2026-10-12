import { useMutation } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Looks up pasted tickers, symbols and ISINs (read-only: nothing is tracked until the owner confirms). */
export function useResolveSymbols() {
  return useMutation({
    mutationFn: async (symbols: readonly string[]) =>
      unwrap(await api.POST('/api/instruments/tracked/resolve', { body: { symbols: [...symbols] } })),
  });
}
