import type { TFunction } from 'i18next';

/** A failed API call, from an RFC 9457 ProblemDetails response (or a network failure). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  /** Field errors from a ValidationProblem, keyed by camelCase path (e.g. "items[0].name"). */
  readonly fieldErrors: Record<string, string[]>;
  readonly traceId?: string;

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}, traceId?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
    this.traceId = traceId;
  }

  static network(): ApiError {
    return new ApiError(0, 'NETWORK_ERROR', 'Network error');
  }
}

interface ProblemBody {
  title?: string;
  status?: number;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

const codeForStatus = (status: number): string =>
  ({ 400: 'BAD_REQUEST', 401: 'UNAUTHORIZED', 403: 'FORBIDDEN', 404: 'NOT_FOUND', 409: 'CONFLICT', 422: 'BUSINESS_RULE_VIOLATION', 429: 'RATE_LIMITED' })[status] ??
  (status >= 500 ? 'INTERNAL_ERROR' : 'BAD_REQUEST');

/** Builds an ApiError from a response; tolerates non-JSON bodies. */
export async function toApiError(response: Response): Promise<ApiError> {
  let body: ProblemBody = {};
  try {
    body = (await response.clone().json()) as ProblemBody;
  } catch {
    // Not JSON (for example a proxy error page).
  }
  const code = typeof body.code === 'string' && body.code ? body.code : codeForStatus(response.status);
  return new ApiError(response.status, code, body.title ?? response.statusText, body.errors ?? {}, body.traceId);
}

/** The translated, user-facing message for an error (server titles are never shown directly). */
export function errorMessage(error: unknown, t: TFunction): string {
  const code = error instanceof ApiError ? error.code : 'INTERNAL_ERROR';
  const key = `errors.codes.${code}`;
  const translated = t(key);
  return translated === key ? t('errors.generic') : translated;
}
