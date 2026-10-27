import { createSseParser, type SseFrame } from './sseParser';
import fixture from '@/test/fixtures/assistant-turn.sse?raw';

function parse(chunks: string[], end = true): SseFrame[] {
  const frames: SseFrame[] = [];
  const p = createSseParser((f) => frames.push(f));
  chunks.forEach((c) => {
    p.push(c);
  });
  if (end) p.end();
  return frames;
}

describe('sseParser', () => {
  it('reads the recorded turn the same however it is cut', () => {
    const whole = parse([fixture]);
    expect(whole.map((f) => f.event)).toEqual([
      'turn',
      'status',
      'token',
      'iteration_discard',
      'tool_call',
      'tool_result',
      'status',
      'token',
      'ping',
      'token',
      'done',
    ]);
    for (const size of [1, 3, 7, 50]) {
      const pieces = fixture.match(new RegExp(`[\\s\\S]{1,${size}}`, 'g')) ?? [];
      expect(parse(pieces)).toEqual(whole);
    }
  });

  it('handles CRLF, CR, comments, a missing space, multi-line data and an unknown event', () => {
    const frames = parse([
      ': hello\r\nevent: a\r\ndata:1\r\ndata: 2\r\n\r\nevent: weird\rdata: x\r\r',
      'data: plain\n\n',
    ]);
    expect(frames).toEqual([
      { event: 'a', data: '1\n2' },
      { event: 'weird', data: 'x' },
      { event: 'message', data: 'plain' },
    ]);
  });

  it('drops a frame cut off by the end of the stream', () => {
    expect(parse(['event: token\ndata: {"a":1}\n\nevent: done\ndata: {"b"'])).toHaveLength(1);
  });

  it('survives a split inside a UTF-8 character when decoded as a stream', () => {
    const bytes = new TextEncoder().encode('event: token\ndata: {"text":"café \u{1F600}"}\n\n');
    const decoder = new TextDecoder();
    const frames: SseFrame[] = [];
    const p = createSseParser((f) => frames.push(f));
    for (let i = 0; i < bytes.length; i += 5) p.push(decoder.decode(bytes.slice(i, i + 5), { stream: true }));
    expect(JSON.parse(frames[0]?.data ?? '{}')).toEqual({ text: 'café \u{1F600}' });
  });

  it('does not grow without bound on a hostile endless line, and its tail never forms a frame', () => {
    const frames: SseFrame[] = [];
    const p = createSseParser((f) => frames.push(f));
    for (let i = 0; i < 30; i++) p.push('x'.repeat(100_000));
    p.push('\n\nevent: ok\ndata: 2\n\n');
    expect(frames).toEqual([{ event: 'ok', data: '2' }]);
  });

  it('does not form a frame from the tail of an overflowed line on the same line', () => {
    const frames: SseFrame[] = [];
    const p = createSseParser((f) => frames.push(f));
    p.push('data: ' + 'y'.repeat(1_100_000) + 'EVIL\n\n');
    expect(frames).toEqual([]);
  });
});
