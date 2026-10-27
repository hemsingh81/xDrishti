import { anchorSpans, markSpans } from './markSpans';

describe('markSpans', () => {
  it('counts offsets in UTF-16 code units (an emoji is two)', () => {
    const text = '\u{1F600} won 46 trades';
    const start = text.indexOf('46');
    expect(markSpans(text, [{ start, end: start + 2 }])).toEqual([
      { text: '\u{1F600} won ', marked: false },
      { text: '46', marked: true },
      { text: ' trades', marked: false },
    ]);
  });

  it('handles adjacent spans, spans at both ends, and drops bad ones without changing the text', () => {
    const parts = markSpans('abcdef', [
      { start: 3, end: 6 },
      { start: 0, end: 3 },
      { start: 4, end: 5 },
      { start: 9, end: 12 },
      { start: 2, end: 2 },
    ]);
    expect(parts.map((p) => p.text).join('')).toBe('abcdef');
    expect(parts.filter((p) => p.marked).map((p) => p.text)).toEqual(['abc', 'def']);
  });
});

describe('anchorSpans', () => {
  const answer = 'about 46 of them won, 46 again';
  it('keeps a span whose characters match, moves a drifted one to the nearest match, drops one that is nowhere', () => {
    expect(anchorSpans(answer, [{ start: 6, end: 8, text: '46' }])).toEqual([{ start: 6, end: 8 }]);
    expect(anchorSpans(answer, [{ start: 20, end: 22, text: '46' }])).toEqual([{ start: 22, end: 24 }]);
    expect(anchorSpans(answer, [{ start: 0, end: 3, text: '999' }])).toEqual([]);
  });
});
