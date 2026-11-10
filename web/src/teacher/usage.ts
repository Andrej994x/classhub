import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type WorkspaceUsage = components['schemas']['WorkspaceUsageResponse'];
export type PlatformUsage = components['schemas']['PlatformUsageResponse'];
export const USAGE_PERIODS = [7, 30, 90] as const;
export type UsagePeriod = (typeof USAGE_PERIODS)[number];

/** The workspace's own usage (R3-P6): tools opened, activities created and reused. */
export function useWorkspaceUsage(workspaceId: string, days: UsagePeriod = 30) {
  return useQuery({
    queryKey: ['w', workspaceId, 'usage', days] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/usage', { params: { path: { workspaceId }, query: { days } } })).data!,
  });
}

/** Platform-wide totals for staff. */
export function usePlatformUsage(days: UsagePeriod) {
  return useQuery({
    queryKey: ['admin', 'usage', days] as const,
    queryFn: async () => (await api.client.GET('/api/admin/usage', { params: { query: { days } } })).data!,
  });
}

/** Counts an opened tool; best effort (a failed count never stops opening the tool). */
export function countToolOpened(workspaceId: string, code: string) {
  void api.client.POST('/api/w/{workspaceId}/toolbox/{code}/open', { params: { path: { workspaceId, code } } }).catch(() => undefined);
}

/** Counts a saved activity opened again on its board; best effort. */
export function countActivityOpened(workspaceId: string, activityId: string) {
  void api.client.POST('/api/w/{workspaceId}/activities/{activityId}/open', { params: { path: { workspaceId, activityId } } }).catch(() => undefined);
}
