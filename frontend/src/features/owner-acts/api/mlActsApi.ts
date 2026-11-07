import { api, unwrap } from '@/shared/api';
import { useAct } from './movesApi';

// CF-10 10d-2b: the two owner acts of the Models screen (writes). The reads are in `features/learning`; both acts re-read
// everything under the `learning` root when they settle.

export type MlSwitchField = 'influenceEnabled' | 'autoPromote' | 'autoRollback';

export interface MlSwitchAct {
  field: MlSwitchField;
  on: boolean;
  reason: string;
  /** Only for turning influence or automatic promotion ON, typed for this act; never kept. Otherwise omitted (sent as null). */
  password?: string;
}

/** Changes ONE ML switch. The other two are sent as null, which the API reads as "leave alone". */
export function useMlSwitchAct() {
  return useAct(async ({ field, on, reason, password }: MlSwitchAct) =>
    unwrap(
      await api.PUT('/api/learning/ml-settings', {
        body: {
          influenceEnabled: field === 'influenceEnabled' ? on : null,
          autoPromote: field === 'autoPromote' ? on : null,
          autoRollback: field === 'autoRollback' ? on : null,
          reason,
          password: password ?? null,
        },
      }),
    ),
  );
}

/** Applies one rollback proposal. Session and CSRF only: lowering reliance on ML needs no password and has no body. */
export function useApplyRollbackAct() {
  return useAct(async (id: number) =>
    unwrap(await api.POST('/api/learning/rollback/{id}/apply', { params: { path: { id } } })),
  );
}
