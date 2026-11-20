import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem details as returned by the API (see ApiExceptionHandler). */
export interface Problem {
  status: number;
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export const GENERIC_ERROR = 'Something went wrong. Please try again.';
export const OFFLINE_ERROR = "Can't reach the server. Check your connection and try again.";

export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return { status: 0, detail: OFFLINE_ERROR };
    }
    const body = typeof error.error === 'object' && error.error !== null ? (error.error as Partial<Problem>) : {};
    return { ...body, status: error.status };
  }
  return { status: -1, detail: GENERIC_ERROR };
}

/** One human-readable sentence for banners and snackbars. */
export function problemMessage(problem: Problem): string {
  if (problem.status === 429) {
    return 'Too many attempts. Please wait a minute and try again.';
  }
  if (problem.errors) {
    const first = Object.values(problem.errors).flat()[0];
    if (first) {
      return first;
    }
  }
  return problem.detail ?? problem.title ?? GENERIC_ERROR;
}

/** Field errors keyed by camelCase field name, for showing under form inputs. */
export function fieldErrors(problem: Problem): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    if (messages.length > 0) {
      result[field] = messages.join(' ');
    }
  }
  return result;
}
