import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Lead = components['schemas']['LeadResponse'];
export type LeadList = components['schemas']['LeadListResponse'];
export type LeadListItem = components['schemas']['LeadListItem'];

export const LEAD_STATUSES = ['New', 'Contacted', 'Converted', 'Closed'] as const;
export const LEAD_TYPES = ['Message', 'LessonRequest', 'Diagnostic'] as const;
export type LeadStatus = (typeof LEAD_STATUSES)[number];

/** Allowed changes, mirroring LeadStatusRules in the API (which stays authoritative). */
export const NEXT_STATUSES: Record<LeadStatus, LeadStatus[]> = {
  New: ['Contacted', 'Converted', 'Closed'],
  Contacted: ['New', 'Converted', 'Closed'],
  Converted: ['Contacted', 'Closed'],
  Closed: ['New', 'Contacted'],
};

export interface LeadFilters {
  status?: string;
  type?: string;
  q?: string;
  archived?: boolean;
  page?: number;
}

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'leads'] as const,
  list: (workspaceId: string, filters: LeadFilters) => ['w', workspaceId, 'leads', 'list', filters] as const,
  one: (workspaceId: string, leadId: string) => ['w', workspaceId, 'leads', leadId] as const,
};

export function useLeads(workspaceId: string, filters: LeadFilters) {
  return useQuery({
    queryKey: keys.list(workspaceId, filters),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await api.client.GET('/api/w/{workspaceId}/leads', {
          params: {
            path: { workspaceId },
            query: {
              status: filters.status || undefined,
              type: filters.type || undefined,
              q: filters.q?.trim() || undefined,
              archived: filters.archived || undefined,
              page: filters.page ?? 1,
              pageSize: 20,
            },
          },
        })
      ).data!,
  });
}

/** Always refetched on mount: status changes send the version that was really loaded. */
export function useLead(workspaceId: string, leadId: string) {
  return useQuery({
    queryKey: keys.one(workspaceId, leadId),
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/leads/{leadId}', { params: { path: { workspaceId, leadId } } })).data!,
  });
}

function useLeadMutation<TInput>(workspaceId: string, leadId: string, run: (input: TInput) => Promise<Lead>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (lead) => {
      queryClient.setQueryData(keys.one(workspaceId, leadId), lead);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useChangeLeadStatus(workspaceId: string, leadId: string) {
  return useLeadMutation(workspaceId, leadId, async ({ status, version }: { status: LeadStatus; version: number }) =>
    (await api.client.PUT('/api/w/{workspaceId}/leads/{leadId}/status', { params: { path: { workspaceId, leadId } }, body: { status, version } })).data!,
  );
}

export function useArchiveLead(workspaceId: string, leadId: string) {
  return useLeadMutation(workspaceId, leadId, async ({ archived, version }: { archived: boolean; version: number }) => {
    const options = { params: { path: { workspaceId, leadId } }, body: { version } };
    return archived
      ? (await api.client.POST('/api/w/{workspaceId}/leads/{leadId}/archive', options)).data!
      : (await api.client.POST('/api/w/{workspaceId}/leads/{leadId}/restore', options)).data!;
  });
}

export function useAddLeadNote(workspaceId: string, leadId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (text: string) =>
      (await api.client.POST('/api/w/{workspaceId}/leads/{leadId}/notes', { params: { path: { workspaceId, leadId } }, body: { text } })).data!,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: keys.one(workspaceId, leadId) });
    },
  });
}
