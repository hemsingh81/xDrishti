import { ApiError } from '@/shared/api';
import { fieldErrorsOf, problemMessage, scoped, unplacedMessages } from './fieldErrors';
import { draftOf, isPlanEmpty, planRoleChange, primaryBlockedMessage, type RoleDraft } from './roleChanges';
import { istDayIso } from '@/shared/lib/format';
import {
  keyExpiry,
  secretStatusText,
  toUpdateSecretsInput,
  validateReplaceSecrets,
  type ReplaceSecretsDraft,
} from './secrets';
import { initialWizardState, stepOfField, suggestName, toCreateInput, validateStep } from './wizardState';

const standby: RoleDraft = { data: true, dataKind: 'standby', trading: false, portfolio: false };
const primary: RoleDraft = { ...standby, dataKind: 'primary' };
const off: RoleDraft = { data: false, dataKind: 'standby', trading: false, portfolio: false };

describe('planRoleChange', () => {
  it('does nothing when nothing changed', () => {
    expect(isPlanEmpty(planRoleChange(primary, primary))).toBe(true);
    expect(isPlanEmpty(planRoleChange(standby, standby))).toBe(true);
  });

  it('uses make-primary for becoming primary, never a roles request for the Data part', () => {
    expect(planRoleChange(standby, primary)).toEqual({ body: null, makePrimary: true });
    expect(planRoleChange(off, primary)).toEqual({ body: null, makePrimary: true });
  });

  it('turns Data on as standby or off through the roles request', () => {
    expect(planRoleChange(off, standby).body?.data).toEqual({ enabled: true, kind: 'standby' });
    expect(planRoleChange(standby, off).body?.data).toEqual({ enabled: false, kind: null });
  });

  it('blocks switching off or demoting the primary', () => {
    expect(planRoleChange(primary, off).blocked).toBe(primaryBlockedMessage);
    expect(planRoleChange(primary, standby).blocked).toBe(primaryBlockedMessage);
  });

  it('lets the matrix ignore the primary loss when another account takes over', () => {
    const plan = planRoleChange(primary, standby, { ignorePrimaryLoss: true });
    expect(plan.blocked).toBeUndefined();
    expect(isPlanEmpty(plan)).toBe(true);
  });

  it('names only the roles that changed', () => {
    const plan = planRoleChange(standby, { ...standby, trading: true });
    expect(plan.body).toEqual({ data: null, trading: { enabled: true }, portfolio: null });
  });

  it('reads the current roles from the API shape', () => {
    expect(
      draftOf({
        data: { enabled: true, primary: true, standby: false },
        trading: { enabled: false, mode: 'paper', profile: null },
        portfolio: { enabled: true },
      }),
    ).toEqual({ data: true, dataKind: 'primary', trading: false, portfolio: true });
  });
});

describe('field errors', () => {
  const problem = new ApiError(400, {
    code: 'accounts.invalid',
    errors: { 'trading.riskProfileId': ['Unknown risk profile.', 'Again.'], name: ['Required.'] },
  });

  it('joins messages per field', () => {
    expect(fieldErrorsOf(problem)).toEqual({
      'trading.riskProfileId': 'Unknown risk profile. Again.',
      name: 'Required.',
    });
  });

  it('strips the section prefix used by create requests', () => {
    expect(scoped(fieldErrorsOf(problem), 'trading')).toEqual({
      riskProfileId: 'Unknown risk profile. Again.',
      name: 'Required.',
    });
  });

  it('has no general message for per-field problems, but one for conflicts and outages', () => {
    expect(problemMessage(problem)).toBeUndefined();
    expect(problemMessage(new ApiError(409, { title: 'Already used.', code: 'accounts.conflict' }))).toBe(
      'Already used.',
    );
    expect(problemMessage(new TypeError('network'))).toMatch(/Could not reach the server/);
    expect(problemMessage(undefined)).toBeUndefined();
  });
});

describe('unplaced field messages', () => {
  it('lists the messages of fields the form does not show, and nothing when all are shown', () => {
    const errors = { refreshMode: 'Bad.', includeInTotals: 'No.', other: 'Hmm.' };

    expect(unplacedMessages(errors, ['refreshMode'])).toBe('includeInTotals: No. other: Hmm.');
    expect(unplacedMessages(errors, ['refreshMode', 'includeInTotals', 'other'])).toBeUndefined();
    expect(unplacedMessages({}, [])).toBeUndefined();
  });
});

describe('wizard helpers', () => {
  it.each([
    ['clientId', 0],
    ['secrets.apiKey', 0],
    ['secrets.apiSecret', 0],
    ['apiKeyIssuedOn', 0],
    ['name', 1],
    ['color', 1],
    ['roles.data.kind', 2],
    ['roles.trading', 2],
    ['trading.riskProfileId', 3],
    ['portfolio.firstImportDate', 3],
    ['data.liveFeedBudget', 3],
  ])('maps %s to step %d', (field, step) => {
    expect(stepOfField(field)).toBe(step);
  });

  it('suggests "Dhan · Main" when unused, otherwise the next free number', () => {
    expect(suggestName([])).toBe('Dhan · Main');
    expect(suggestName([{ name: 'dhan · main' }, { name: 'Dhan · 2' }])).toBe('Dhan · 3');
  });

  it('asks for the Dhan app details on the first step and for a role on the third', () => {
    const state = initialWizardState([]);
    expect(Object.keys(validateStep(0, state))).toEqual(
      expect.arrayContaining(['clientId', 'secrets.apiKey', 'secrets.apiSecret']),
    );
    expect(validateStep(2, state)).toEqual({});
    expect(validateStep(2, { ...state, roles: { ...state.roles, portfolio: false } })).toHaveProperty('roles');
  });

  it('builds the create request without a broker', () => {
    const state = initialWizardState([]);
    const input = toCreateInput({
      ...state,
      app: { clientId: ' 1000000001 ', apiKey: ' k ', apiSecret: ' s ', apiKeyIssuedOn: '2026-10-01' },
      identity: { ...state.identity, name: ' Main ' },
    });
    expect(input).toMatchObject({
      name: 'Main',
      clientId: '1000000001',
      secrets: { apiKey: 'k', apiSecret: 's' },
      apiKeyIssuedOn: '2026-10-01',
    });
    expect(input).not.toHaveProperty('broker');
  });
});

describe('secrets', () => {
  it('describes a secret by set / not set and when, never by value', () => {
    expect(secretStatusText({ name: 'apiKey', set: true, updatedAt: '2026-10-09' })).toBe(
      'API key: set · updated 9 Oct 2026',
    );
    expect(secretStatusText({ name: 'apiSecret', set: false, updatedAt: null })).toBe('API secret: not set');
  });

  it('warns within the window and after expiry', () => {
    expect(keyExpiry('2027-10-09', undefined, '2026-10-11')).toMatchObject({ state: 'ok' });
    expect(keyExpiry('2026-11-10', undefined, '2026-10-11')).toEqual({
      state: 'soon',
      expiresOn: '2026-11-10',
      daysLeft: 30,
    });
    expect(keyExpiry('2026-10-11', undefined, '2026-10-11')).toEqual({ state: 'expired', expiresOn: '2026-10-11' });
  });

  const draft = (patch: Partial<ReplaceSecretsDraft> = {}): ReplaceSecretsDraft => ({
    apiKey: '',
    apiSecret: '',
    apiKeyIssuedOn: '',
    issuedOnTouched: false,
    ...patch,
  });

  it('sends only what was typed; a new key goes with today unless another day was picked', () => {
    expect(toUpdateSecretsInput(draft({ apiSecret: ' s ' }), '2026-01-01')).toEqual({
      apiKey: null,
      apiSecret: 's',
      apiKeyIssuedOn: null,
    });
    expect(toUpdateSecretsInput(draft({ apiKey: 'k' }), '2026-01-01').apiKeyIssuedOn).toBe(istDayIso());
    expect(
      toUpdateSecretsInput(draft({ apiKey: 'k', apiKeyIssuedOn: '2026-10-01', issuedOnTouched: true }), '2026-01-01')
        .apiKeyIssuedOn,
    ).toBe('2026-10-01');
  });

  it('needs something to change and a generated-on day that is not in the future', () => {
    expect(validateReplaceSecrets(draft(), '2026-01-01')).toHaveProperty('apiKey');
    expect(
      validateReplaceSecrets(draft({ apiKeyIssuedOn: '2026-02-01', issuedOnTouched: true }), '2026-01-01'),
    ).toEqual({});
    expect(
      validateReplaceSecrets(draft({ apiKey: 'k', apiKeyIssuedOn: '2999-01-01', issuedOnTouched: true }), '2026-01-01'),
    ).toHaveProperty('apiKeyIssuedOn');
  });
});
