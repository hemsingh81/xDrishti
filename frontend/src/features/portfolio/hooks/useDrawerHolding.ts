import { useQuery } from '@tanstack/react-query';
import type { HoldingRow } from '@/shared/api';
import { holdingLookupQuery, type HoldingRef } from '../api/lookupQueries';

const nobody: HoldingRef = { id: '', accountId: '', symbol: '' };

/**
 * The row the holding drawer shows. Normally the list has it; when the owner came from the Action center and the list does
 * not (a holding beyond the pages loaded, or another group), it is looked up in its account by the symbol the item named.
 */
export function useDrawerHolding(
  id: string | null,
  inList: HoldingRow | undefined,
  hint: HoldingRef | null,
): HoldingRow | undefined {
  const wanted = id !== null && inList === undefined && hint !== null && hint.id === id;
  const lookup = useQuery({ ...holdingLookupQuery(hint ?? nobody), enabled: wanted });
  return inList ?? (wanted ? (lookup.data ?? undefined) : undefined);
}
