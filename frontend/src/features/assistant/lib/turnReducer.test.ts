import fixture from '@/test/fixtures/assistant-turn.sse?raw';
import { createSseParser } from './sseParser';
import { answerOf, idleTurn, turnReducer, type TurnAction, type TurnState } from './turnReducer';

const frames = (text: string): TurnAction[] => {
  const out: TurnAction[] = [];
  const p = createSseParser((f) => {
    let data: unknown;
    try {
      data = JSON.parse(f.data);
    } catch {
      return;
    }
    out.push({ type: 'frame', event: f.event, data });
  });
  p.push(text);
  return out;
};
const run = (actions: TurnAction[]): TurnState => actions.reduce(turnReducer, turnReducer(idleTurn, { type: 'start' }));

describe('turnReducer', () => {
  it('builds the answer, the calls and the grounding of the recorded turn', () => {
    const state = run(frames(fixture));
    expect(state.running).toBe(false);
    expect(answerOf(state)).toBe('Expectancy is 0.12R over 143 trades [1], and about 46 of them won.');
    expect(state.calls).toHaveLength(1);
    expect(state.calls[0]).toMatchObject({
      tool: 'get_report',
      complete: true,
      toolCallId: 501,
      rows: 1,
      status: 'ok',
    });
    expect(state.done?.grounding.ungrounded).toEqual([{ start: 62, end: 64, text: '46' }]);
    expect(state.conversationId).toBe('0198a2c4-7b1e-7c3a-9a41-5f0c6d2e8b10');
  });

  it('removes the text of a discarded iteration', () => {
    const state = run(frames(fixture).slice(0, 4));
    expect(answerOf(state)).toBe('');
  });

  it('keeps the order of tool calls and marks a call complete only by its result', () => {
    const s = run([
      { type: 'frame', event: 'tool_call', data: { callIndex: 1, tool: 'a', arguments: {} } },
      { type: 'frame', event: 'tool_call', data: { callIndex: 2, tool: 'a', arguments: {} } },
      {
        type: 'frame',
        event: 'tool_result',
        data: { callIndex: 2, toolCallId: 9, status: 'error', rowCount: 0, error: { code: 'x', message: 'm' } },
      },
    ]);
    expect(s.calls.map((c) => [c.index, c.complete])).toEqual([
      [1, false],
      [2, true],
    ]);
    expect(s.calls[1]?.error).toEqual({ code: 'x', message: 'm' });
  });

  it('marks a stream that closes before done as interrupted, with the text kept', () => {
    const s = run([...frames(fixture).slice(0, 8), { type: 'closed' }]);
    expect(s.interrupted).toBe(true);
    expect(s.done).toBeNull();
    expect(answerOf(s)).toContain('Expectancy is 0.12R');
  });

  it('ignores a closed after done, a stopped turn is not interrupted, an error ends the turn', () => {
    expect(run([...frames(fixture), { type: 'closed' }]).interrupted).toBe(false);
    expect(run([{ type: 'stopped' }])).toMatchObject({ stopped: true, interrupted: false });
    expect(run([{ type: 'frame', event: 'error', data: { code: 'c', message: 'boom' } }])).toMatchObject({
      running: false,
      error: { message: 'boom' },
    });
  });

  it('survives hostile frames (wrong types, nulls, unknown events)', () => {
    const s = run([
      { type: 'frame', event: 'token', data: { iteration: 'x', text: 5 } },
      { type: 'frame', event: 'token', data: null },
      { type: 'frame', event: 'tool_call', data: [] },
      { type: 'frame', event: 'status', data: { phase: 'weird' } },
      { type: 'frame', event: 'nonsense', data: { a: 1 } },
      { type: 'frame', event: 'done', data: { grounding: { ungrounded: [{ start: 'a' }, 5] } } },
    ]);
    expect(s.calls).toEqual([]);
    expect(answerOf(s)).toBe('');
    expect(s.done?.grounding).toEqual({ status: 'not_checked', checked: 0, ungrounded: [] });
  });
});
