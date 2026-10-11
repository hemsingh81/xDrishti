/** Moves focus to the first field of `container` that is marked invalid (after React rendered the messages). */
export function focusFirstInvalid(container: HTMLElement | null): void {
  setTimeout(() => {
    container?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, 0);
}
