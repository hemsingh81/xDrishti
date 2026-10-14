import { useRef } from 'react';

const enabledButton = 'section[aria-label="Trades and ledger from Dhan"] button:not(:disabled)';

/**
 * "Read the ledger" on the invitation under the tiles: scrolls to the sync bar and puts the keyboard on the first usable
 * button of its Trades and ledger section (Sync trades & ledger). The control is never a disabled one:
 * - with several accounts and "All accounts" the section is not there (a read belongs to one account), so the keyboard goes
 *   to the Account picker, where the owner chooses one (the bar says "Pick an account above to sync it");
 * - when the account is not connected, or a read runs, its buttons are disabled: the bar itself takes the focus, so the
 *   visible reason beside it is what is read out.
 * Returns `[barRef, accountRef, focus]`: `barRef` goes on the element that wraps the sync bar (focusable, `tabIndex={-1}`),
 * `accountRef` on the input of the Account picker.
 */
export function useSyncBarFocus() {
  const barRef = useRef<HTMLDivElement>(null);
  const accountRef = useRef<HTMLInputElement>(null);
  const focus = () => {
    const bar = barRef.current;
    if (!bar) return;
    const button = bar.querySelector<HTMLElement>(enabledButton);
    const hasSection = bar.querySelector('section[aria-label="Trades and ledger from Dhan"]') !== null;
    const target = button ?? (hasSection ? bar : accountRef.current);
    if (!target) return;
    if (typeof target.scrollIntoView === 'function') target.scrollIntoView({ block: 'center' });
    target.focus();
  };
  return [barRef, accountRef, focus] as const;
}
