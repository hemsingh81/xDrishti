export interface Span {
  start: number;
  end: number;
}

export interface TextPart {
  text: string;
  marked: boolean;
}

/**
 * Splits an answer into plain and marked parts. Offsets are UTF-16 code units (as the server counts them). Spans that are
 * out of range, empty, unsorted or overlapping are clamped or dropped; the text itself is never changed.
 */
export function markSpans(text: string, spans: readonly Span[]): TextPart[] {
  const sorted = [...spans].sort((a, b) => a.start - b.start);
  const parts: TextPart[] = [];
  let at = 0;
  for (const span of sorted) {
    const start = Math.max(span.start, at);
    const end = Math.min(span.end, text.length);
    if (!Number.isInteger(start) || !Number.isInteger(end) || end <= start) continue;
    if (start > at) parts.push({ text: text.slice(at, start), marked: false });
    parts.push({ text: text.slice(start, end), marked: true });
    at = end;
  }
  if (at < text.length) parts.push({ text: text.slice(at), marked: false });
  return parts;
}

/**
 * Keeps the spans whose characters at the offsets are the span's own text (offsets are exact UTF-16 indices). A span that does
 * not match is dropped, never moved: `dropped` counts them so the screen can say how many figures it could not locate.
 */
export function checkSpans(
  answer: string,
  spans: readonly (Span & { text: string })[],
): { valid: Span[]; dropped: number } {
  const valid = spans
    .filter((s) => s.text === '' || answer.slice(s.start, s.end) === s.text)
    .map((s) => ({ start: s.start, end: s.end }));
  return { valid, dropped: spans.length - valid.length };
}
