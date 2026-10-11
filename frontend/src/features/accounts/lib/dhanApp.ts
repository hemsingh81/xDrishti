/** Dhan is the only broker: what the owner needs to know to create its API app (from Dhan's documentation). */
export const dhanApp = {
  name: 'Dhan',
  clientIdLabel: 'Client id',
  clientIdHint: 'Your Dhan client id, e.g. 1000000001 (My Profile on web.dhan.co).',
  apiKeyLabel: 'API key (app_id)',
  apiSecretLabel: 'API secret (app_secret)',
  /** API keys expire this many months after they were generated … */
  apiKeyValidityMonths: 12,
  /** … and the owner is warned this many days before. */
  apiKeyExpiryWarningDays: 30,
  setupSteps: [
    'Sign in to web.dhan.co and open My Profile → Access DhanHQ APIs.',
    'Toggle "API key", enter an app name and the Redirect URL below (Postback is optional).',
    'Generate the key: copy the API key (app_id) and API secret (app_secret) into xDrishti.',
  ],
  notes: [
    'A static IP is needed only for placing orders, not for data or portfolio.',
    'Dhan allows 25 sign-ins (consents) a day; each sign-in gives a token valid for 24 hours.',
  ],
} as const;
