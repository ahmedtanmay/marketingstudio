import { HttpErrorResponse } from '@angular/common/http';
import { OFFLINE_ERROR, fieldErrors, problemMessage, toProblem } from './problem';

describe('problem helpers', () => {
  it('maps validation problems to field messages', () => {
    const problem = toProblem(
      new HttpErrorResponse({ status: 400, error: { title: 'Validation', errors: { password: ['This password is too common.'] } } }),
    );

    expect(fieldErrors(problem)).toEqual({ password: 'This password is too common.' });
    expect(problemMessage(problem)).toBe('This password is too common.');
  });

  it('[AUTH-5] keeps the plan-limit code so the UI can offer an upgrade', () => {
    const problem = toProblem(
      new HttpErrorResponse({ status: 402, error: { detail: 'Your plan includes 2 seats', code: 'PLAN_LIMIT_REACHED' } }),
    );

    expect(problem.code).toBe('PLAN_LIMIT_REACHED');
    expect(problemMessage(problem)).toContain('2 seats');
  });

  it('explains network failures and rate limits in plain language', () => {
    expect(problemMessage(toProblem(new HttpErrorResponse({ status: 0 })))).toBe(OFFLINE_ERROR);
    expect(problemMessage(toProblem(new HttpErrorResponse({ status: 429 })))).toContain('Too many attempts');
  });
});
