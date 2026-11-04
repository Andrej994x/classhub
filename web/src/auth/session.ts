import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import { ApiError } from '../api/errors.ts';
import type { components } from '../api/schema';
import { i18n, isLanguage, rememberLanguage } from '../i18n/index.ts';

export type Me = components['schemas']['MeResponse'];
export type MyWorkspace = components['schemas']['MyWorkspaceResponse'];

export const meQueryKey = ['auth', 'me'] as const;

/** The signed-in account, or null when there is no session (401). Other errors propagate. */
export async function fetchMe(): Promise<Me | null> {
  try {
    const { data } = await api.client.GET('/api/auth/me');
    return data ?? null;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null;
    throw error;
  }
}

/** Adopts the account's language (the account preference wins over the browser's remembered choice). */
export function adoptAccountLanguage(me: Me | null) {
  if (me && isLanguage(me.locale) && i18n.resolvedLanguage !== me.locale) {
    rememberLanguage(me.locale);
    void i18n.changeLanguage(me.locale);
  }
}

/**
 * Drops everything cached for an identity (workspaces, workspace details, …) except the account query, and
 * the antiforgery token, which is bound to the identity.
 */
export function forgetIdentityData(queryClient: QueryClient) {
  api.resetToken();
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== meQueryKey[0] || query.queryKey[1] !== meQueryKey[1] });
}

export function useMe() {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: meQueryKey,
    queryFn: async () => {
      const previous = queryClient.getQueryData<Me | null>(meQueryKey);
      const me = await fetchMe();
      // The session changed outside this tab's sign-in/sign-out (another tab, expiry): nothing cached for the
      // previous identity may be shown to the new one (workspace isolation in the UI as well).
      if (previous !== undefined && (previous?.id ?? null) !== (me?.id ?? null)) forgetIdentityData(queryClient);
      adoptAccountLanguage(me);
      return me;
    },
    staleTime: 60_000,
  });
}

export function useMyWorkspaces(enabled = true) {
  return useQuery({
    queryKey: ['me', 'workspaces'],
    enabled,
    queryFn: async () => (await api.client.GET('/api/me/workspaces')).data ?? [],
  });
}

export function useSignIn() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { email: string; password: string; rememberMe: boolean }) =>
      (await api.client.POST('/api/auth/login', { body: input })).data!,
    onSuccess: (me) => {
      queryClient.clear(); // nothing cached for another identity survives a sign-in
      queryClient.setQueryData(meQueryKey, me);
      adoptAccountLanguage(me);
    },
  });
}

export function useSignOut() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      try {
        await api.client.POST('/api/auth/logout');
      } catch (error) {
        // 401: the session had already ended, which is what signing out wants.
        if (!(error instanceof ApiError && error.status === 401)) throw error;
      }
    },
    // Only after the server ended the session: on failure the cookie is still valid, so the UI must not
    // pretend to be signed out (the caller shows the error and the user can retry).
    onSuccess: () => {
      api.resetToken();
      queryClient.clear();
      queryClient.setQueryData(meQueryKey, null);
    },
  });
}

/** Saves the UI language on the account (when signed in), so emails and other devices use it too. */
export async function saveAccountLanguage(locale: string) {
  const { data } = await api.client.PATCH('/api/auth/me', { body: { locale, timeZone: null } });
  return data;
}

/**
 * Only same-site paths are accepted as a post-login destination (no open redirect): must start with a
 * single "/" and must not be a protocol-relative or backslash URL.
 */
export function safeReturnTo(value: string | null | undefined, fallback = '/app'): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return fallback;
  return value;
}
