import { parseBlocks, parseInline } from './plainText';

describe('plainText', () => {
  it('gives structure to paragraphs, both list kinds and bold only', () => {
    const blocks = parseBlocks('Intro\nmore\n\n- one\n- two\n\n1. a\n2) b\n\n| t | t |\n|---|---|');
    expect(blocks.map((b) => b.kind)).toEqual(['p', 'ul', 'ol', 'p']);
    expect(blocks[3]).toMatchObject({ kind: 'p', lines: [{ text: '| t | t |' }, { text: '|---|---|' }] });
  });

  it('keeps raw offsets after list markers and bold markers are dropped', () => {
    const raw = '- **bold** 46';
    const [block] = parseBlocks(raw);
    const line = block?.kind === 'ul' ? block.items[0] : undefined;
    expect(line).toBeDefined();
    const pieces = parseInline(line ?? { text: '', offset: 0 });
    for (const piece of pieces) expect(raw.slice(piece.offset, piece.offset + piece.text.length)).toBe(piece.text);
    expect(pieces.map((p) => [p.text, p.bold])).toEqual([
      ['bold', true],
      [' 46', false],
    ]);
  });

  it('leaves an unmatched ** as text with the right offset', () => {
    const pieces = parseInline({ text: 'a **b', offset: 10 });
    expect(pieces.map((p) => p.text).join('')).toBe('a **b');
    expect(pieces.every((p) => !p.bold)).toBe(true);
  });
});
