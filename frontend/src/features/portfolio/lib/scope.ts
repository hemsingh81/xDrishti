/** "Main · Long term": the account (when one is chosen) and the bucket the page is narrowed to; null when it is not. */
export function scopeLabelOf(
  accountName: string | null,
  bucketId: string | null,
  buckets: readonly { id: string; name: string }[] | undefined,
): string | null {
  const bucket = bucketId === null ? null : (buckets?.find((b) => b.id === bucketId)?.name ?? null);
  return [accountName, bucket].filter((part): part is string => part !== null).join(' · ') || null;
}

/** Scrolls an element to the top of its scroll container on the next frame (once the page has laid out what changed). */
export function scrollToElement(element: HTMLElement | null): void {
  requestAnimationFrame(() => {
    if (typeof element?.scrollIntoView === 'function') element.scrollIntoView({ block: 'start' });
  });
}
