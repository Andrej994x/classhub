import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type MfaStatus = components['schemas']['MfaStatusResponse'];
export type AuthenticatorSetup = components['schemas']['AuthenticatorSetupResponse'];
export type AdminOverview = components['schemas']['AdminOverviewResponse'];
export type AdminUser = components['schemas']['AdminUserSummary'];
export type AdminUserDetail = components['schemas']['AdminUserDetail'];
export type AdminAuditEntry = components['schemas']['AdminAuditEntry'];

export const USER_STATUSES = ['Active', 'Suspended'] as const;
export const PLATFORM_OWNER = 'PlatformOwner';
export const PLATFORM_ADMIN = 'PlatformAdmin';

export const isPlatformStaff = (roles: readonly string[] | undefined) => !!roles?.some((r) => r === PLATFORM_OWNER || r === PLATFORM_ADMIN);

const keys = {
  mfa: ['auth', 'mfa'] as const,
  admin: ['admin'] as const,
  overview: ['admin', 'overview'] as const,
  users: (filters: AdminUserFilters) => ['admin', 'users', filters] as const,
  user: (userId: string) => ['admin', 'user', userId] as const,
  audit: (filters: AuditFilters) => ['admin', 'audit', filters] as const,
};

/** The account's two-factor state; always fresh when the admin area opens. */
export function useMfaStatus(enabled = true) {
  return useQuery({
    queryKey: keys.mfa,
    enabled,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/auth/mfa')).data!,
  });
}

/** A new key. Replacing an enabled authenticator needs proof: a current code, or a recovery code (used up). */
export function useSetupAuthenticator() {
  return useMutation({
    mutationFn: async (proof?: { code?: string; recoveryCode?: string }) =>
      (await api.client.POST('/api/auth/mfa/authenticator', { body: { code: proof?.code ?? null, recoveryCode: proof?.recoveryCode ?? null } })).data!,
  });
}

/** Confirms the key; the response holds the recovery codes, shown once. The status is refreshed by the caller. */
export function useEnableAuthenticator() {
  return useMutation({
    mutationFn: async (code: string) => (await api.client.POST('/api/auth/mfa/authenticator/enable', { body: { code } })).data!.recoveryCodes,
  });
}

export function useRegenerateRecoveryCodes() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (code: string) => (await api.client.POST('/api/auth/mfa/recovery-codes', { body: { code } })).data!.recoveryCodes,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.mfa }),
  });
}

export function useRefreshMfaStatus() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: keys.mfa });
}

export function useAdminOverview() {
  return useQuery({
    queryKey: keys.overview,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/overview')).data!,
  });
}

export interface AdminUserFilters {
  q?: string;
  status?: string;
  staff?: boolean;
  page?: number;
}

export function useAdminUsers(filters: AdminUserFilters) {
  return useQuery({
    queryKey: keys.users(filters),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await api.client.GET('/api/admin/users', {
          params: { query: { q: filters.q?.trim() || undefined, status: filters.status || undefined, staff: filters.staff || undefined, page: filters.page ?? 1 } },
        })
      ).data!,
  });
}

export function useAdminUser(userId: string) {
  return useQuery({
    queryKey: keys.user(userId),
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/users/{userId}', { params: { path: { userId } } })).data!,
  });
}

function useUserMutation<TInput>(userId: string, run: (input: TInput) => Promise<AdminUserDetail>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (detail) => {
      queryClient.setQueryData(keys.user(userId), detail);
      await queryClient.invalidateQueries({ queryKey: keys.admin, predicate: (q) => q.queryKey[1] !== 'user' });
    },
  });
}

export function useChangeUserStatus(userId: string) {
  return useUserMutation(userId, async ({ suspend, reason }: { suspend: boolean; reason: string }) =>
    suspend
      ? (await api.client.POST('/api/admin/users/{userId}/suspend', { params: { path: { userId } }, body: { reason } })).data!
      : (await api.client.POST('/api/admin/users/{userId}/reactivate', { params: { path: { userId } }, body: { reason } })).data!,
  );
}

export function useChangeAdminRole(userId: string) {
  return useUserMutation(userId, async ({ grant, code }: { grant: boolean; code: string }) =>
    grant
      ? (await api.client.POST('/api/admin/users/{userId}/admin-role/grant', { params: { path: { userId } }, body: { code } })).data!
      : (await api.client.POST('/api/admin/users/{userId}/admin-role/revoke', { params: { path: { userId } }, body: { code } })).data!,
  );
}

export interface AuditFilters {
  action?: string;
  userId?: string;
  from?: string;
  to?: string;
  page?: number;
}

export function useAuditLog(filters: AuditFilters) {
  return useQuery({
    queryKey: keys.audit(filters),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await api.client.GET('/api/admin/audit', {
          params: {
            query: {
              action: filters.action?.trim() || undefined,
              userId: filters.userId || undefined,
              from: filters.from || undefined,
              to: filters.to || undefined,
              page: filters.page ?? 1,
            },
          },
        })
      ).data!,
  });
}

export type AdminPlan = components['schemas']['AdminPlanResponse'];
export type AdminSubscription = components['schemas']['AdminSubscriptionResponse'];
export type SubscriptionEvent = components['schemas']['SubscriptionEventResponse'];

export const PAYMENT_METHODS = ['bank-transfer', 'cash', 'card', 'complimentary', 'other'] as const;
export const PAYMENT_CURRENCIES = ['MKD', 'EUR', 'USD', 'CHF', 'GBP', 'RSD', 'ALL', 'BGN', 'TRY'];

export function usePlans() {
  return useQuery({
    queryKey: ['admin', 'plans'] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/plans')).data!,
  });
}

/** Platform Owner only; sends every limit with the version that was loaded. */
export function useUpdatePlan(code: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { limits: { key: string; value: number }[]; version: number }) =>
      (await api.client.PUT('/api/admin/plans/{code}', { params: { path: { code } }, body: input })).data!,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'plans'] }),
  });
}

export function useWorkspaceSubscription(workspaceId: string) {
  return useQuery({
    queryKey: ['admin', 'subscription', workspaceId] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/workspaces/{workspaceId}/subscription', { params: { path: { workspaceId } } })).data!,
  });
}

export interface ActivateProInput {
  months: number;
  amountMinor: number;
  currency: string;
  paymentMethod: string;
  paymentReference: string | null;
  paidOn: string;
  note: string | null;
}

export function useActivatePro(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: ActivateProInput) =>
      (await api.client.POST('/api/admin/workspaces/{workspaceId}/subscription/pro', { params: { path: { workspaceId } }, body: input })).data!,
    onSuccess: async (subscription) => {
      queryClient.setQueryData(['admin', 'subscription', workspaceId], subscription);
      await queryClient.invalidateQueries({ queryKey: ['admin', 'overview'] });
    },
  });
}
