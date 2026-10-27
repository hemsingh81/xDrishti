/** Every sentence the assistant screen says, in one place so tests pin them. */
export const words = {
  title: 'Assistant',
  off: 'Assistant is switched off',
  offHelp: 'Nothing is asked or answered while it is off.',
  turnOn: 'Turn on',
  down: 'Model server not running',
  downHelp: 'This app cannot start it. Start it in a terminal with:',
  loading: 'Loading the model, usually 20 to 105 seconds',
  misconfigured: 'The model server rejected the API key',
  misconfiguredHelp: 'Check the key the model server was started with.',
  toolsDown: 'Tool server not running',
  toolsDownHelp: 'The assistant cannot read your data without it. Start it with:',
  busy: 'Another question is being answered. Wait for it to finish.',
  nightly: 'The nightly run is in progress. Try again when it has finished.',
  startCommand: 'scripts/xd-ai.sh start',
  notEvaluated: 'Not yet evaluated: answers have not been checked against the test set',
  noConversations: 'No conversations yet',
  composerLabel: 'Ask about your data',
  examples: [
    'What is the expectancy of 15m SELL setups by basket?',
    'Which cells have enough trades to rank?',
    "What is in tomorrow's plan?",
  ],
  working: 'Assistant is working',
  ready: 'Answer ready',
  closedEarly: 'The connection closed before the answer finished',
  stopped: 'Stopped',
  incomplete: 'Incomplete',
  notFromTool: '(not from a tool)',
  checked: 'Checked: every figure was found in the tool results above',
  notChecked: 'Not checked (stopped or failed)',
  adHoc: 'Ad-hoc query, not checked by the report engine',
  chartAsTable: 'Chart shown as a table',
  maxChars: 2000,
  noPasswordOff: 'No password needed to turn off',
  portfolioOff: "Off: portfolio questions answer 'not available'",
  settingsSaved: 'Settings saved',
} as const;

export const refusals: Readonly<Record<string, string>> = {
  'assistant.disabled': 'The assistant is switched off.',
  'assistant.busy': 'Another question is being answered. Try again in a moment.',
  'assistant.nightly_running': 'The nightly run is in progress. Try again when it has finished.',
  'assistant.model_unavailable': 'The model server is not available.',
  'assistant.tools_unavailable': 'The tool server is not available.',
  'assistant.invalid': 'That message cannot be sent.',
  'assistant.too_long': 'That message is too long.',
  'assistant.not_found': 'That conversation was not found.',
  'auth.rate_limited': 'Too many requests. Wait a moment and try again.',
};

export const ungroundedWords = (n: number) =>
  `${n} ${n === 1 ? 'figure' : 'figures'} in this answer ${n === 1 ? 'was' : 'were'} not found in any tool result`;
