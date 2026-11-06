import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Group = components['schemas']['GroupResponse'];
export type GroupListItem = components['schemas']['GroupListItem'];

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'groups'] as const,
  list: (workspaceId: string, archived: boolean, studentId?: string) => ['w', workspaceId, 'groups', 'list', archived, studentId ?? null] as const,
  one: (workspaceId: string, groupId: string) => ['w', workspaceId, 'groups', groupId] as const,
};

export function useGroups(workspaceId: string, archived: boolean, studentId?: string) {
  return useQuery({
    queryKey: keys.list(workspaceId, archived, studentId),
    queryFn: async () =>
      (await api.client.GET('/api/w/{workspaceId}/groups', { params: { path: { workspaceId }, query: { archived: archived || undefined, studentId } } })).data!,
  });
}

export function useGroup(workspaceId: string, groupId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, groupId ?? 'new'),
    enabled: groupId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/groups/{groupId}', { params: { path: { workspaceId, groupId: groupId! } } })).data!,
  });
}

function useGroupMutation<TInput>(workspaceId: string, run: (input: TInput) => Promise<Group>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (group) => {
      queryClient.setQueryData(keys.one(workspaceId, group.id), group);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useSaveGroup(workspaceId: string, groupId?: string) {
  return useGroupMutation(workspaceId, async ({ name, subjectId, description, version }: { name: string; subjectId: string | null; description: string | null; version: number | null }) =>
    groupId
      ? (await api.client.PUT('/api/w/{workspaceId}/groups/{groupId}', { params: { path: { workspaceId, groupId } }, body: { name, subjectId, description, version } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/groups', { params: { path: { workspaceId } }, body: { name, subjectId, description, version: null } })).data!,
  );
}

export function useArchiveGroup(workspaceId: string, groupId: string) {
  return useGroupMutation(workspaceId, async ({ archived, version }: { archived: boolean; version: number }) =>
    archived
      ? (await api.client.POST('/api/w/{workspaceId}/groups/{groupId}/archive', { params: { path: { workspaceId, groupId } }, body: { version } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/groups/{groupId}/restore', { params: { path: { workspaceId, groupId } }, body: { version } })).data!,
  );
}

export function useAddMember(workspaceId: string, groupId: string) {
  return useGroupMutation(workspaceId, async (studentId: string) =>
    (await api.client.POST('/api/w/{workspaceId}/groups/{groupId}/members', { params: { path: { workspaceId, groupId } }, body: { studentId } })).data!,
  );
}

export function useRemoveMember(workspaceId: string, groupId: string) {
  return useGroupMutation(workspaceId, async (studentId: string) =>
    (await api.client.DELETE('/api/w/{workspaceId}/groups/{groupId}/members/{studentId}', { params: { path: { workspaceId, groupId, studentId } } })).data!,
  );
}
