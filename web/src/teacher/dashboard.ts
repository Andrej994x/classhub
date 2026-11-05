import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Dashboard = components['schemas']['DashboardResponse'];

export const DASHBOARD_PERIODS = [7, 30, 90] as const;
export type DashboardPeriod = (typeof DASHBOARD_PERIODS)[number];

/** The workspace's own numbers for the last 7, 30 or 90 UTC days (always fresh when the dashboard opens). */
export function useDashboard(workspaceId: string, days: DashboardPeriod) {
  return useQuery({
    queryKey: ['w', workspaceId, 'dashboard', days] as const,
    refetchOnMount: 'always',
    placeholderData: keepPreviousData,
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/dashboard', { params: { path: { workspaceId }, query: { days } } })).data!,
  });
}
