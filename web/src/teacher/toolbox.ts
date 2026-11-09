import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Tool = components['schemas']['ToolResponse'];
export type AdminTool = components['schemas']['AdminToolResponse'];
export type ToolInput = components['schemas']['SaveToolRequest'];

export type ToolboxFilter = { q: string; subjectId: string; category: string };

/** The active tools matching the filter (R3-P1); the previous result stays while typing. */
export function useToolbox(workspaceId: string, filter: ToolboxFilter) {
  const query = {
    q: filter.q.trim() || undefined,
    subjectId: filter.subjectId || undefined,
    category: filter.category || undefined,
  };
  return useQuery({
    queryKey: ['w', workspaceId, 'toolbox', query] as const,
    placeholderData: keepPreviousData,
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/toolbox', { params: { path: { workspaceId }, query } })).data!,
  });
}

/** Every tool, active or not, for platform staff. */
export function useAdminTools() {
  return useQuery({
    queryKey: ['admin', 'toolbox'],
    queryFn: async () => (await api.client.GET('/api/admin/toolbox')).data!,
  });
}

/** Adds a tool (no id) or changes one (with the version that was loaded). */
export function useSaveTool() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, input }: { id?: string; input: ToolInput }) =>
      id
        ? (await api.client.PUT('/api/admin/toolbox/{toolId}', { params: { path: { toolId: id } }, body: input })).data!
        : (await api.client.POST('/api/admin/toolbox', { body: input })).data!,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'toolbox'] });
    },
  });
}
