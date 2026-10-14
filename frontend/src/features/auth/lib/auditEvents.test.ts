import { auditEventLabel } from './auditEvents';

describe('auditEventLabel', () => {
  it.each([
    ['LoginSucceeded', 'Signed in'],
    ['LoginFailed', 'Sign-in failed'],
    ['AccountLocked', 'Account locked after too many failed attempts'],
    ['LoginRefusedLocked', 'Sign-in refused while locked'],
    ['Logout', 'Signed out'],
    ['SessionExpiredIdle', 'Session ended after being idle'],
    ['SessionExpiredAbsolute', 'Session ended at its time limit'],
    ['OwnerProvisioned', 'Owner account created'],
    ['OwnerPasswordReset', 'Password reset from the command line'],
    ['StepUpSucceeded', 'Password confirmed for a sensitive change'],
    ['StepUpFailed', 'Password confirmation failed'],
    ['PasswordChanged', 'Password changed'],
    ['ProfileUpdated', 'Profile updated'],
  ])('names %s as "%s"', (event, label) => {
    expect(auditEventLabel(event)).toBe(label);
  });

  it('turns an unknown event into readable words', () => {
    expect(auditEventLabel('PasskeyRegistered')).toBe('Passkey registered');
    expect(auditEventLabel('some_other-event')).toBe('Some other event');
  });

  it('never returns an empty label', () => {
    expect(auditEventLabel('')).toBe('Unknown event');
  });
});
