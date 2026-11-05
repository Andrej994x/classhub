import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type DiagnosticTest = components['schemas']['DiagnosticTestResponse'];
export type DiagnosticTestListItem = components['schemas']['DiagnosticTestListItem'];
export type SaveDiagnosticTest = components['schemas']['SaveDiagnosticTestRequest'];

export const TEST_DIFFICULTIES = ['Beginner', 'Intermediate', 'Advanced'] as const;
export const MAX_QUESTIONS = 50;
export const MAX_OPTIONS = 8;
export const MAX_POINTS = 100;

const keys = {
  list: (workspaceId: string) => ['w', workspaceId, 'tests'] as const,
  one: (workspaceId: string, testId: string) => ['w', workspaceId, 'tests', testId] as const,
};

export function useTests(workspaceId: string) {
  return useQuery({
    queryKey: keys.list(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/tests', { params: { path: { workspaceId } } })).data!,
  });
}

/** Always refetched on mount: the editor never starts from a stale version. */
export function useTest(workspaceId: string, testId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, testId ?? 'new'),
    enabled: testId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/tests/{testId}', { params: { path: { workspaceId, testId: testId! } } })).data!,
  });
}

function useTestMutation<TInput>(workspaceId: string, run: (input: TInput) => Promise<DiagnosticTest>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (test) => {
      queryClient.setQueryData(keys.one(workspaceId, test.id), test);
      await queryClient.invalidateQueries({ queryKey: keys.list(workspaceId), exact: true });
    },
  });
}

export function useSaveTest(workspaceId: string, testId: string | undefined) {
  return useTestMutation(workspaceId, async (body: SaveDiagnosticTest) =>
    testId === undefined
      ? (await api.client.POST('/api/w/{workspaceId}/tests', { params: { path: { workspaceId } }, body })).data!
      : (await api.client.PUT('/api/w/{workspaceId}/tests/{testId}', { params: { path: { workspaceId, testId } }, body })).data!,
  );
}

export function useTestStatus(workspaceId: string, testId: string) {
  return useTestMutation(workspaceId, async ({ publish, version }: { publish: boolean; version: number }) => {
    const options = { params: { path: { workspaceId, testId } }, body: { version } };
    return publish
      ? (await api.client.POST('/api/w/{workspaceId}/tests/{testId}/publish', options)).data!
      : (await api.client.POST('/api/w/{workspaceId}/tests/{testId}/deactivate', options)).data!;
  });
}

export function useDuplicateTest(workspaceId: string, testId: string) {
  return useTestMutation(workspaceId, async () =>
    (await api.client.POST('/api/w/{workspaceId}/tests/{testId}/duplicate', { params: { path: { workspaceId, testId } } })).data!,
  );
}

/** Attempt count and the results visitors chose to share (D-18). */
export function useTestAttempts(workspaceId: string, testId: string) {
  return useQuery({
    queryKey: [...keys.one(workspaceId, testId), 'attempts'] as const,
    refetchOnMount: 'always',
    queryFn: async () =>
      (await api.client.GET('/api/w/{workspaceId}/tests/{testId}/attempts', { params: { path: { workspaceId, testId } } })).data!,
  });
}

export function useArchiveTest(workspaceId: string, testId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await api.client.DELETE('/api/w/{workspaceId}/tests/{testId}', { params: { path: { workspaceId, testId } } });
    },
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: keys.one(workspaceId, testId) });
      await queryClient.invalidateQueries({ queryKey: keys.list(workspaceId), exact: true });
    },
  });
}
