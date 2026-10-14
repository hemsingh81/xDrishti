/** Plain words for the audit event names the API sends (see GET /api/auth/audit). */
const labels: Record<string, string> = {
  LoginSucceeded: 'Signed in',
  LoginFailed: 'Sign-in failed',
  AccountLocked: 'Account locked after too many failed attempts',
  LoginRefusedLocked: 'Sign-in refused while locked',
  Logout: 'Signed out',
  SessionExpiredIdle: 'Session ended after being idle',
  SessionExpiredAbsolute: 'Session ended at its time limit',
  OwnerProvisioned: 'Owner account created',
  OwnerPasswordReset: 'Password reset from the command line',
  StepUpSucceeded: 'Password confirmed for a sensitive change',
  StepUpFailed: 'Password confirmation failed',
  PasswordChanged: 'Password changed',
  ProfileUpdated: 'Profile updated',
};

/** "SomeNewEvent" -> "Some new event": an event this version does not know is still readable. */
function words(name: string): string {
  const spaced = name
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase() : 'Unknown event';
}

export function auditEventLabel(event: string): string {
  return labels[event] ?? words(event);
}
