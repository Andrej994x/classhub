import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type ActivityItem = components['schemas']['ActivityListItem'];
export type ActivityFilter = { q: string; subjectId: string; toolType: string; page: number };

export const BOARDS = ['drawing-board', 'geometry-board'] as const;
const PAGE_SIZE = 20;

/** The workspace's saved activities (R3-P2), newest change first. */
export function useActivities(workspaceId: string, filter: ActivityFilter) {
  const query = {
    q: filter.q.trim() || undefined,
    subjectId: filter.subjectId || undefined,
    toolType: filter.toolType || undefined,
    page: filter.page,
    pageSize: PAGE_SIZE,
  };
  return useQuery({
    queryKey: ['w', workspaceId, 'activities', query] as const,
    placeholderData: keepPreviousData,
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/activities', { params: { path: { workspaceId }, query } })).data!,
  });
}

function useRefresh(workspaceId: string) {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['w', workspaceId, 'activities'] });
}

/**
 * Renames an activity or changes its subject. The document is sent back unchanged with the version the list showed: a
 * newer save elsewhere (the board, another tab) makes it a conflict instead of being overwritten.
 */
export function useRenameActivity(workspaceId: string) {
  const refresh = useRefresh(workspaceId);
  return useMutation({
    mutationFn: async ({ item, title, subjectId }: { item: ActivityItem; title: string; subjectId: string | null }) => {
      const path = { workspaceId, activityId: item.id };
      const current = (await api.client.GET('/api/w/{workspaceId}/activities/{activityId}', { params: { path } })).data!;
      return (
        await api.client.PUT('/api/w/{workspaceId}/activities/{activityId}', {
          params: { path },
          body: { title, subjectId, toolType: item.toolType, schemaVersion: current.schemaVersion, document: current.document, version: item.version },
        })
      ).data!;
    },
    onSuccess: refresh,
  });
}

export function useDuplicateActivity(workspaceId: string) {
  const refresh = useRefresh(workspaceId);
  return useMutation({
    mutationFn: async (item: ActivityItem) =>
      (await api.client.POST('/api/w/{workspaceId}/activities/{activityId}/duplicate', { params: { path: { workspaceId, activityId: item.id } } })).data!,
    onSuccess: refresh,
  });
}

export function useDeleteActivity(workspaceId: string) {
  const refresh = useRefresh(workspaceId);
  return useMutation({
    mutationFn: async (item: ActivityItem) => {
      await api.client.POST('/api/w/{workspaceId}/activities/{activityId}/delete', { params: { path: { workspaceId, activityId: item.id } }, body: { version: item.version } });
    },
    onSuccess: refresh,
  });
}
