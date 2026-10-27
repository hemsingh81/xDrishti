/**
 * The structure the renderer draws from model text: paragraphs, `- ` and `1.` lists, and `**bold**` runs. Nothing else is
 * interpreted (no link, image, table or HTML). Every piece keeps its offset into the raw text, so the grounding marks (UTF-16
 * offsets of the raw answer) still land on the right characters after the list markers and `**` are dropped.
 */
export interface Piece {
  text: string;
  /** Offset of `text` in the raw answer (UTF-16 code units). */
  offset: number;
  bold: boolean;
}
export interface Line {
  text: string;
  offset: number;
}
export type Block = { kind: 'p'; lines: Line[] } | { kind: 'ul' | 'ol'; items: Line[] };

const bullet = /^(\s{0,3}[-*]\s+)(.*)$/;
const numbered = /^(\s{0,3}\d{1,3}[.)]\s+)(.*)$/;

function rawLines(text: string): Line[] {
  const lines: Line[] = [];
  let start = 0;
  for (let i = 0; i <= text.length; i++) {
    const c = text[i];
    if (i === text.length || c === '\n' || c === '\r') {
      lines.push({ text: text.slice(start, i), offset: start });
      if (c === '\r' && text[i + 1] === '\n') i++;
      start = i + 1;
    }
  }
  return lines;
}

export function parseBlocks(text: string): Block[] {
  const blocks: Block[] = [];
  let last: Block | undefined;
  for (const line of rawLines(text)) {
    if (line.text.trim() === '') {
      last = undefined;
      continue;
    }
    const b = bullet.exec(line.text);
    const n = b ? null : numbered.exec(line.text);
    const m = b ?? n;
    const kind = b ? 'ul' : n ? 'ol' : 'p';
    const content: Line = m ? { text: m[2] ?? '', offset: line.offset + (m[1]?.length ?? 0) } : line;
    if (last?.kind === kind) {
      if (last.kind === 'p') last.lines.push(content);
      else last.items.push(content);
      continue;
    }
    last = kind === 'p' ? { kind, lines: [content] } : { kind, items: [content] };
    blocks.push(last);
  }
  return blocks;
}

/** Splits `**bold**` runs; an unmatched `**` stays as text. */
export function parseInline(line: Line): Piece[] {
  const out: Piece[] = [];
  const parts = line.text.split('**');
  const pairs = parts.length % 2 === 1 ? parts.length : parts.length - 1;
  let at = line.offset;
  parts.forEach((part, i) => {
    const marker = i > 0 ? 2 : 0;
    const start = at + marker;
    at = start + part.length;
    if (i < pairs) {
      if (part) out.push({ text: part, offset: start, bold: i % 2 === 1 });
    } else if (i === pairs) {
      // The unmatched marker is text: keep the characters and the offsets of the raw line.
      out.push({ text: `**${part}`, offset: start - 2, bold: false });
    } else if (part) {
      out.push({ text: part, offset: start, bold: false });
    }
  });
  return out;
}
