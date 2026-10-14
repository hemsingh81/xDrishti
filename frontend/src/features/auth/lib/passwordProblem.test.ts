import { ApiError } from '@/shared/api';
import { describePasswordProblem } from './passwordProblem';

const problem = (status: number, code: string, extra: object = {}) =>
  new ApiError(status, { title: code, status, code, ...extra });

describe('describePasswordProblem', () => {
  it('reads the new-password message of a rejected password', () => {
    expect(
      describePasswordProblem(problem(400, 'auth.password_invalid', { errors: { newPassword: ['Too short.'] } })),
    ).toEqual({ kind: 'new_password', message: 'Too short.' });
    expect(
      describePasswordProblem(
        problem(400, 'auth.password_unchanged', { errors: { newPassword: ['Same.', 'Pick another.'] } }),
      ),
    ).toEqual({ kind: 'new_password', message: 'Same. Pick another.' });
  });

  it('falls back to the title or a generic message without field errors', () => {
    expect(describePasswordProblem(problem(400, 'auth.password_invalid'))).toEqual({
      kind: 'new_password',
      message: 'auth.password_invalid',
    });
    expect(describePasswordProblem(new ApiError(400, { code: 'auth.password_invalid' }))).toEqual({
      kind: 'new_password',
      message: 'This password cannot be used.',
    });
  });

  it('tells a wrong current password, a lockout, a busy account and anything else apart', () => {
    expect(describePasswordProblem(problem(401, 'auth.invalid_credentials'))).toEqual({ kind: 'wrong_current' });
    expect(describePasswordProblem(problem(423, 'auth.locked', { retryAfterSeconds: 90 }))).toEqual({
      kind: 'locked',
      problem: { kind: 'locked', retryAfterSeconds: 90 },
    });
    expect(describePasswordProblem(problem(409, 'auth.busy'))).toEqual({ kind: 'busy' });
    expect(describePasswordProblem(problem(500, 'server'))).toEqual({ kind: 'failed' });
    expect(describePasswordProblem(new TypeError('network'))).toEqual({ kind: 'failed' });
  });
});
