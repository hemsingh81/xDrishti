import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import fixture from '@/test/fixtures/assistant-turn.sse?raw';
import { stubMatchMedia } from '@/test/matchMedia';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { AssistantPage } from './components/AssistantPage';
import {
  conversationBuilder,
  conversationId,
  llm,
  mountChat,
  mountConversation,
  mountEmptyHistory,
  mountRefusal,
  mountStatus,
  sseResponse,
  statusBuilder,
} from './testing';

pinClock('2026-10-27T09:00:00Z');

const open = (path = '/assistant') =>
  renderRoutes([{ path: '/assistant', element: <AssistantPage /> }], { initialPath: path });

const ask = async (text: string) => {
  const user = userEvent.setup();
  const box = await screen.findByLabelText('Ask about your data');
  await waitFor(() => {
    expect(box).toBeEnabled();
  });
  await user.type(box, text);
  await user.click(screen.getByRole('button', { name: 'Send' }));
  return user;
};

describe('gate states', () => {
  const cases: [string, Parameters<typeof statusBuilder>[0], RegExp][] = [
    [
      'switched off',
      { enabled: false, tools: { state: 'disabled', count: 0, schemaTokensEstimate: 0 } },
      /Assistant is switched off/,
    ],
    ['model not running', { llm: llm('down') }, /Model server not running/],
    ['model loading', { llm: llm('loading') }, /Loading the model, usually 20 to 105 seconds/],
    ['key rejected', { llm: llm('misconfigured') }, /The model server rejected the API key/],
    ['tools down', { tools: { state: 'down', count: 0, schemaTokensEstimate: 0 } }, /Tool server not running/],
  ];
  it.each(cases)('%s: says so, disables asking and draws no number', async (_n, over, words) => {
    mountStatus(statusBuilder(over));
    mountEmptyHistory();
    open();
    expect((await screen.findAllByText(words)).length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Ask about your data')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Stop' })).not.toBeInTheDocument();
  });

  it('shows the start command for a stopped model and says the app cannot start it', async () => {
    mountStatus(statusBuilder({ llm: llm('down') }));
    mountEmptyHistory();
    open();
    expect(await screen.findByText('scripts/xd-ai.sh start')).toBeInTheDocument();
    expect(screen.getByText(/cannot start it/)).toBeInTheDocument();
  });

  it('offers Turn on when switched off', async () => {
    mountStatus(statusBuilder({ enabled: false }));
    mountEmptyHistory();
    const user = userEvent.setup();
    open();
    await user.click(await screen.findByRole('button', { name: 'Turn on' }));
    expect(await screen.findByRole('dialog', { name: 'Assistant settings' })).toBeInTheDocument();
  });

  it.each([
    ['busy', { busy: true }, /Another question is being answered/],
    ['nightly', { nightlyRunning: true }, /nightly run is in progress/],
  ] as const)('%s: composer disabled with the reason as text', async (_n, over, words) => {
    mountStatus(statusBuilder(over));
    mountEmptyHistory();
    open();
    expect(await screen.findByText(words)).toBeInTheDocument();
    expect(screen.getByLabelText('Ask about your data')).toBeDisabled();
  });

  it('ready: composer enabled; the evaluation line is plain', async () => {
    mountStatus(statusBuilder());
    mountEmptyHistory();
    open();
    await waitFor(() => {
      expect(screen.getByLabelText('Ask about your data')).toBeEnabled();
    });
    expect(screen.getByText(/Not yet evaluated/)).toBeInTheDocument();
  });

  it.each([
    [{ state: 'failed', at: '2026-10-20T09:00:00Z', detail: null }, /Warning: the last evaluation failed/],
    [{ state: 'passed', at: '2026-10-20T09:00:00Z', detail: null }, /^Evaluated 20 Oct 2026/],
  ])('evaluation %o', async (evaluation, words) => {
    mountStatus(statusBuilder({ evaluation }));
    mountEmptyHistory();
    open();
    expect(await screen.findByText(words)).toBeInTheDocument();
  });

  it('shows an error when the status cannot be read', async () => {
    server.use(http.get('/api/assistant/status', () => problemResponse({ title: 'Down', code: 'x' }, 500)));
    mountEmptyHistory();
    open();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});

describe('asking', () => {
  beforeEach(() => {
    mountStatus(statusBuilder());
    mountConversation();
  });

  it('streams the recorded turn: answer, tool call, mark on the figure, banner, and the stored copy replaces it', async () => {
    mountChat(() => sseResponse(fixture));
    const { router } = open();
    await ask('What is the expectancy?');
    await waitFor(() => {
      expect(router.state.location.search).toBe(`?c=${conversationId}`);
    });
    // The stored copy (its call has 2 rows) has replaced the live one (1 row).
    await screen.findByRole('button', { name: 'Tool call 1, get_report, 2 rows, ok' });
    await waitFor(() => {
      expect(screen.getAllByRole('article')).toHaveLength(1);
    });
    const answer = screen.getByRole('article', { name: 'Assistant answer' });
    expect(answer).toHaveTextContent('Expectancy is 0.12R over 143 trades [1], and about 46');
    expect(answer).toHaveTextContent('of them won.');
    expect(answer).not.toHaveTextContent('Let me check the report.');
    const mark = within(answer).getByText('46', { selector: 'mark' });
    expect(mark).toHaveTextContent('(not from a tool)');
    expect(within(answer).getByText('1 figure in this answer was not found in any tool result')).toBeInTheDocument();
    expect(within(answer).getByRole('button', { name: /Tool call 1, get_report/ })).toBeInTheDocument();
    expect(screen.getAllByRole('article')).toHaveLength(1);
    expect(screen.getByLabelText('Ask about your data')).toHaveFocus();
  });

  it('sends the question and the conversation id with the CSRF-protected client', async () => {
    let body: unknown;
    mountChat(() => sseResponse(fixture));
    server.use(
      http.post('/api/assistant/chat', async ({ request }) => {
        body = await request.json();
        return sseResponse(fixture);
      }),
    );
    open();
    await ask('hello');
    await waitFor(() => {
      expect(body).toEqual({ conversationId: null, message: 'hello' });
    });
  });

  it('sends nothing for an empty message and blocks one over 2,000 characters', async () => {
    let calls = 0;
    server.use(
      http.post('/api/assistant/chat', () => {
        calls += 1;
        return sseResponse(fixture);
      }),
    );
    open();
    const user = userEvent.setup();
    const box = await screen.findByLabelText('Ask about your data');
    await waitFor(() => {
      expect(box).toBeEnabled();
    });
    await user.type(box, '   ');
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
    fireEvent.change(box, { target: { value: 'x'.repeat(2001) } });
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
    expect(screen.getByText('2001 of 2000 characters')).toBeInTheDocument();
    expect(calls).toBe(0);
  });

  it('example prompts fill the box and do not send', async () => {
    let calls = 0;
    server.use(
      http.post('/api/assistant/chat', () => {
        calls += 1;
        return sseResponse(fixture);
      }),
    );
    open();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Which cells have enough trades to rank?' }));
    expect(screen.getByLabelText('Ask about your data')).toHaveValue('Which cells have enough trades to rank?');
    expect(calls).toBe(0);
  });

  it('Enter sends, Shift+Enter is a newline', async () => {
    mountChat(() => sseResponse(fixture.slice(0, fixture.indexOf('event: status')), { hang: true }));
    open();
    const user = userEvent.setup();
    const box = await screen.findByLabelText('Ask about your data');
    await waitFor(() => {
      expect(box).toBeEnabled();
    });
    await user.type(box, 'a{Shift>}{Enter}{/Shift}b');
    expect(box).toHaveValue('a\nb');
    await user.type(box, '{Enter}');
    expect(await screen.findByText(/^a\s+b$/, { selector: 'p' })).toBeInTheDocument();
  });

  it('a stream that closes before done keeps the partial text and says so', async () => {
    const cut = fixture.slice(0, fixture.indexOf('event: done'));
    mountChat(() => sseResponse(cut));
    open();
    await ask('q');
    expect(await screen.findByText('The connection closed before the answer finished')).toBeInTheDocument();
    expect(screen.getByText('Incomplete')).toBeInTheDocument();
    expect(screen.getByText(/Expectancy is 0.12R/)).toBeInTheDocument();
    expect(screen.getByText('Not checked (stopped or failed)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument();
  });

  it('Stop aborts the request, keeps the partial text as Stopped, and does not retry', async () => {
    let calls = 0;
    const part = fixture.slice(0, fixture.indexOf('event: ping'));
    server.use(
      http.post('/api/assistant/chat', () => {
        calls += 1;
        return sseResponse(part, { hang: true });
      }),
    );
    open();
    const user = await ask('q');
    expect(await screen.findByText(/Expectancy is 0.12R over 143 trades/)).toBeInTheDocument();
    expect(screen.getByText('q', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText('Working: writing the answer')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop' }));
    expect(await screen.findByText('Stopped')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument();
    expect(calls).toBe(1);
  });

  it('leaving the page mid-turn aborts quietly (nothing is applied after unmount)', async () => {
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    mountChat(() => sseResponse(fixture.slice(0, fixture.indexOf('event: ping')), { hang: true }));
    const view = open();
    await ask('q');
    await screen.findByText(/Expectancy is 0.12R/);
    view.unmount();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('Esc also stops', async () => {
    mountChat(() => sseResponse(fixture.slice(0, fixture.indexOf('event: ping')), { hang: true }));
    open();
    const user = await ask('q');
    await screen.findByRole('button', { name: 'Stop' });
    await user.keyboard('{Escape}');
    expect(await screen.findByText('Stopped')).toBeInTheDocument();
  });

  it.each([
    [409, 'assistant.busy', 'Another question is being answered. Try again in a moment.'],
    [409, 'assistant.disabled', 'The assistant is switched off.'],
    [503, 'assistant.model_unavailable', 'The model server is not available.'],
    [503, 'assistant.tools_unavailable', 'The tool server is not available.'],
    [422, 'assistant.too_long', 'That message is too long.'],
    [429, 'auth.rate_limited', 'Too many requests. Wait a moment and try again.'],
  ])(
    'refusal %i %s shows its sentence, keeps the typed text and re-enables the composer',
    async (status, code, text) => {
      mountRefusal(status, code);
      open();
      await ask('keep me');
      expect((await screen.findAllByText(text)).length).toBeGreaterThan(0);
      expect(screen.getByLabelText('Ask about your data')).toHaveValue('keep me');
      expect(screen.getByLabelText('Ask about your data')).toBeEnabled();
    },
  );
});

describe('accessibility', () => {
  it('has a log that does not announce tokens and a polite status that says working then ready', async () => {
    mountStatus(statusBuilder());
    mountConversation();
    mountChat(() => sseResponse(fixture.slice(0, fixture.indexOf('event: ping')), { hang: true }));
    open();
    const log = await screen.findByRole('log', { name: 'Conversation' });
    expect(log).toHaveAttribute('aria-live', 'off');
    const user = await ask('q');
    const announcer = screen.getAllByRole('status').find((el) => el.getAttribute('aria-live') === 'polite');
    await waitFor(() => {
      expect(announcer).toHaveTextContent('Assistant is working');
    });
    await user.click(screen.getByRole('button', { name: 'Stop' }));
    await waitFor(() => {
      expect(screen.getByLabelText('Ask about your data')).toHaveFocus();
    });
  });

  it('names the live tool call chip while the turn runs', async () => {
    mountStatus(statusBuilder());
    mountConversation();
    mountChat(() =>
      sseResponse(fixture.slice(0, fixture.indexOf('event: status\ndata: {"phase":"answering"')), { hang: true }),
    );
    open();
    await ask('q');
    expect(await screen.findByRole('button', { name: 'Tool call 1, get_report, 1 row, ok' })).toBeEnabled();
  });
});

describe('model text is text', () => {
  const nasty =
    '<script>alert(1)</script>\n<img src=http://x/a.png>\n[a](http://x) ![i](http://x)\n| a | b |\n|---|---|\n&lt;b&gt; **bold**\n\n- one\n- two\n\n1. first';
  it('renders HTML, links, images and tables as inert text, with only paragraphs, lists and bold', async () => {
    const detail = conversationBuilder();
    const assistant = detail.messages[1];
    if (!assistant) throw new Error('fixture');
    assistant.content = nasty;
    assistant.ungrounded = [];
    assistant.groundingStatus = 'no_numbers';
    assistant.toolCalls = [];
    mountStatus(statusBuilder());
    mountConversation(detail);
    open(`/assistant?c=${conversationId}`);
    const answer = await screen.findByRole('article', { name: 'Assistant answer' });
    expect(answer).toHaveTextContent('<script>alert(1)</script>');
    expect(answer).toHaveTextContent('[a](http://x)');
    expect(answer).toHaveTextContent('&lt;b&gt;');
    for (const tag of ['a', 'img', 'script', 'iframe', 'table']) expect(answer.querySelector(tag)).toBeNull();
    expect(answer.querySelector('strong')).toHaveTextContent('bold');
    expect(answer.querySelectorAll('ul li')).toHaveLength(2);
    expect(answer.querySelectorAll('ol li')).toHaveLength(1);
    expect(screen.queryByText(/not found in any tool result/)).not.toBeInTheDocument();
  });
});

describe('history', () => {
  beforeEach(() => {
    mountStatus(statusBuilder());
    stubMatchMedia((q) => q.includes('min-width')); // a wide screen: the list sits beside the thread
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('on a phone the list is a drawer opened by History', async () => {
    stubMatchMedia(() => false);
    mountConversation();
    open();
    const user = userEvent.setup();
    expect(screen.queryByRole('navigation', { name: 'Conversations' })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'History' }));
    expect(await screen.findByRole('button', { name: /Expectancy question/ })).toBeInTheDocument();
  });

  it('says No conversations yet for an empty list', async () => {
    mountEmptyHistory();
    open();
    expect(await screen.findByText('No conversations yet')).toBeInTheDocument();
  });

  it('opens a conversation by ?c with its stored answer, call and mark; marks count UTF-16 offsets after an emoji', async () => {
    const detail = conversationBuilder();
    const assistant = detail.messages[1];
    if (!assistant) throw new Error('fixture');
    assistant.content = '\u{1F600} Expectancy 0.12R, about 46 won.';
    const at = assistant.content.indexOf('46');
    assistant.ungrounded = [{ start: at, end: at + 2, text: '46' }];
    mountConversation(detail);
    open(`/assistant?c=${conversationId}`);
    const answer = await screen.findByRole('article', { name: 'Assistant answer' });
    expect(within(answer).getByText('46', { selector: 'mark' })).toBeInTheDocument();
    expect(within(answer).queryByText('0.12R', { selector: 'mark' })).not.toBeInTheDocument();
  });

  it('drops a span whose text is not at its offsets and says how many figures were not located', async () => {
    const detail = conversationBuilder();
    const assistant = detail.messages[1];
    if (!assistant) throw new Error('fixture');
    assistant.ungrounded = [{ start: 3, end: 5, text: '46' }];
    mountConversation(detail);
    open(`/assistant?c=${conversationId}`);
    const answer = await screen.findByRole('article', { name: 'Assistant answer' });
    expect(answer.querySelector('mark')).toBeNull();
    expect(within(answer).getByText('1 figure not located in the text')).toBeInTheDocument();
  });

  it('selects from the list, then New conversation clears ?c', async () => {
    mountConversation();
    const { router } = open();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /Expectancy question/ }));
    await screen.findByRole('article', { name: 'Assistant answer' });
    expect(router.state.location.search).toBe(`?c=${conversationId}`);
    await user.click(screen.getByRole('button', { name: 'New conversation' }));
    expect(router.state.location.search).toBe('');
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('opens a tool call on demand: arguments, rows through the kit (a non-value says its words), evidence', async () => {
    mountConversation();
    let loads = 0;
    server.use(
      http.get('/api/assistant/tool-calls/:id', () => {
        loads += 1;
        return HttpResponse.json(conversationToolCall());
      }),
    );
    open(`/assistant?c=${conversationId}`);
    const user = userEvent.setup();
    const chip = await screen.findByRole('button', { name: 'Tool call 1, get_report, 2 rows, ok' });
    expect(chip).toHaveAttribute('aria-expanded', 'false');
    expect(loads).toBe(0);
    await user.click(chip);
    expect(chip).toHaveAttribute('aria-expanded', 'true');
    const table = await screen.findByRole('table', { name: 'Tool call result: get_report' });
    expect(within(table).getByText('Only 4 trades.')).toBeInTheDocument();
    expect(within(table).getAllByText('Not available')).toHaveLength(1);
    expect(within(table).queryByText('4')).toBeNull();
    expect(within(table).getByText('Nifty 50')).toBeInTheDocument();
    expect(screen.getByLabelText('Arguments')).toHaveTextContent('"report": "summary"');
    expect(screen.getByRole('region', { name: 'Evidence' })).toBeInTheDocument();
    await user.click(chip);
    await user.click(chip);
    await screen.findByRole('table', { name: 'Tool call result: get_report' });
    expect(loads).toBe(1);
  });
});

function conversationToolCall() {
  return {
    id: 501,
    conversationId,
    turnId: 't1',
    callIndex: 1,
    tool: 'get_report',
    arguments: { report: 'summary' },
    status: 'ok',
    errorCode: null,
    result: {
      ok: true,
      asOf: '2026-10-27T03:00:00Z',
      rows: [
        { basket: 'Nifty 50', expectancy: { state: 'value', number: 0.12, reason: null } },
        { basket: 'Bank', expectancy: { state: 'insufficient', number: null, reason: 'Only 4 trades.' } },
      ],
    },
    resultBytes: 1,
    rows: 2,
    truncated: false,
    evidence: { basis: 'gross', n: 143, dateRange: '2025-01-01..2025-12-31' },
    redactions: 0,
    injectionFlags: 0,
    startedAt: '2026-10-27T08:00:01Z',
    durationMs: 42,
  };
}
