import { z } from 'zod';

/** Mirrors the API rules (AuthRules); the API stays authoritative. Messages are i18n keys. */
export const PASSWORD_MIN = 10;
export const PASSWORD_MAX = 128;
const EMAIL_MAX = 256;

const email = z.string().trim().min(1, 'auth.validation.emailRequired').max(EMAIL_MAX, 'auth.validation.emailInvalid').pipe(z.email('auth.validation.emailInvalid'));
const newPassword = z.string().min(PASSWORD_MIN, 'auth.validation.passwordTooShort').max(PASSWORD_MAX, 'auth.validation.passwordTooLong');

export const loginSchema = z.object({
  email,
  password: z.string().min(1, 'auth.validation.passwordRequired').max(PASSWORD_MAX, 'auth.validation.passwordTooLong'),
  rememberMe: z.boolean(),
});

export const registerSchema = z
  .object({ email, password: newPassword, confirmPassword: z.string() })
  .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'auth.validation.passwordsDiffer' });

export const emailSchema = z.object({ email });

export const resetSchema = z
  .object({ password: newPassword, confirmPassword: z.string() })
  .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'auth.validation.passwordsDiffer' });

export type LoginForm = z.infer<typeof loginSchema>;
export type RegisterForm = z.infer<typeof registerSchema>;
export type EmailForm = z.infer<typeof emailSchema>;
export type ResetForm = z.infer<typeof resetSchema>;

/** Reads `key=value` pairs from a URL fragment (tokens travel in the fragment, never to servers or logs). */
export function readFragment(hash: string): URLSearchParams {
  return new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
}
