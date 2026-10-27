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
 * Checks each span against the answer: the characters at its offsets must be the span's own text. A span that does not match is
 * moved to the nearest place where its text occurs (a drifted offset must not underline the wrong characters); one whose text
 * is nowhere in the answer is dropped (the banner still counts it).
 */
export function anchorSpans(answer: string, spans: readonly (Span & { text: string })[]): Span[] {
  const out: Span[] = [];
  for (const span of spans) {
    if (span.text === '' || answer.slice(span.start, span.end) === span.text) {
      out.push({ start: span.start, end: span.end });
      continue;
    }
    let best = -1;
    for (let i = answer.indexOf(span.text); i >= 0; i = answer.indexOf(span.text, i + 1)) {
      if (best < 0 || Math.abs(i - span.start) < Math.abs(best - span.start)) best = i;
    }
    if (best >= 0) out.push({ start: best, end: best + span.text.length });
  }
  return out;
}
