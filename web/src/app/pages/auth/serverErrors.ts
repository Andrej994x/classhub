import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError } from '../../../api/errors.ts';

/**
 * Marks fields the API rejected. Server messages are English developer text, so each field gets a
 * translated message key instead (the form shows `t(key)`). `fields` maps API field names to form fields.
 * Returns true when any field was marked.
 */
export function applyServerFieldErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: Record<string, { field: Path<T>; message: string }>,
): boolean {
  if (!(error instanceof ApiError) || error.code !== 'VALIDATION_FAILED') return false;
  let marked = false;
  for (const [apiField, target] of Object.entries(fields)) {
    if (error.fieldErrors[apiField]?.length) {
      setError(target.field, { type: 'server', message: target.message });
      marked = true;
    }
  }
  return marked;
}
