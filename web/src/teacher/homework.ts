import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Homework = components['schemas']['HomeworkResponse'];
export type HomeworkAssignment = components['schemas']['HomeworkAssignmentResponse'];
export type HomeworkListItem = components['schemas']['HomeworkListItem'];
export type StudentHomework = components['schemas']['StudentHomeworkResponse'];
export type HomeworkStatus = 'Assigned' | 'Submitted' | 'Reviewed';

/** Same-origin URLs of an answer's file (cookie authenticated; the API sends attachments, images inline). */
export const answerFileUrl = (workspaceId: string, homeworkId: string, assignmentId: string) =>
  `/api/w/${workspaceId}/homework/${homeworkId}/assignments/${assignmentId}/file`;
export const myAnswerFileUrl = (workspaceId: string, assignmentId: string) => `/api/student/w/${workspaceId}/homework/${assignmentId}/file`;

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'homework'] as const,
  list: (workspaceId: string, filter: HomeworkFilter) => ['w', workspaceId, 'homework', 'list', filter] as const,
  one: (workspaceId: string, homeworkId: string) => ['w', workspaceId, 'homework', homeworkId] as const,
  mine: (workspaceId: string) => ['student', 'w', workspaceId, 'homework'] as const,
};

export interface HomeworkFilter {
  q: string;
  archived: boolean;
  page: number;
  lessonId?: string;
}

export function useHomeworkList(workspaceId: string, filter: HomeworkFilter) {
  return useQuery({
    queryKey: keys.list(workspaceId, filter),
    placeholderData: (previous) => previous,
    queryFn: async () =>
      (
        await api.client.GET('/api/w/{workspaceId}/homework', {
          params: { path: { workspaceId }, query: { q: filter.q.trim() || undefined, lessonId: filter.lessonId, archived: filter.archived || undefined, page: filter.page } },
        })
      ).data!,
  });
}

export function useHomework(workspaceId: string, homeworkId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, homeworkId ?? 'new'),
    enabled: homeworkId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/homework/{homeworkId}', { params: { path: { workspaceId, homeworkId: homeworkId! } } })).data!,
  });
}

function useHomeworkMutation<TInput>(workspaceId: string, run: (input: TInput) => Promise<Homework>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (homework) => {
      queryClient.setQueryData(keys.one(workspaceId, homework.id), homework);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export interface HomeworkInput {
  title: string;
  description: string | null;
  dueDate: string | null;
  lessonId: string | null;
  version: number | null;
}

export function useSaveHomework(workspaceId: string, homeworkId?: string) {
  return useHomeworkMutation(workspaceId, async (body: HomeworkInput) =>
    homeworkId
      ? (await api.client.PUT('/api/w/{workspaceId}/homework/{homeworkId}', { params: { path: { workspaceId, homeworkId } }, body })).data!
      : (await api.client.POST('/api/w/{workspaceId}/homework', { params: { path: { workspaceId } }, body: { ...body, version: null } })).data!,
  );
}

export function useArchiveHomework(workspaceId: string, homeworkId: string) {
  return useHomeworkMutation(workspaceId, async ({ archived, version }: { archived: boolean; version: number }) =>
    archived
      ? (await api.client.POST('/api/w/{workspaceId}/homework/{homeworkId}/archive', { params: { path: { workspaceId, homeworkId } }, body: { version } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/homework/{homeworkId}/restore', { params: { path: { workspaceId, homeworkId } }, body: { version } })).data!,
  );
}

export function useAssignHomework(workspaceId: string, homeworkId: string) {
  return useHomeworkMutation(workspaceId, async (target: { studentIds: string[] } | { groupId: string }) =>
    (
      await api.client.POST('/api/w/{workspaceId}/homework/{homeworkId}/assignments', {
        params: { path: { workspaceId, homeworkId } },
        body: 'studentIds' in target ? { studentIds: target.studentIds, groupId: null } : { studentIds: null, groupId: target.groupId },
      })
    ).data!,
  );
}

export function useUnassignHomework(workspaceId: string, homeworkId: string) {
  return useHomeworkMutation(workspaceId, async (assignmentId: string) =>
    (await api.client.DELETE('/api/w/{workspaceId}/homework/{homeworkId}/assignments/{assignmentId}', { params: { path: { workspaceId, homeworkId, assignmentId } } })).data!,
  );
}

export function useReviewHomework(workspaceId: string, homeworkId: string) {
  return useHomeworkMutation(workspaceId, async ({ assignmentId, comment, version }: { assignmentId: string; comment: string | null; version: number }) =>
    (
      await api.client.POST('/api/w/{workspaceId}/homework/{homeworkId}/assignments/{assignmentId}/review', {
        params: { path: { workspaceId, homeworkId, assignmentId } },
        body: { comment, version },
      })
    ).data!,
  );
}

/** The student portal: the student's own homework. */
export function useMyHomeworkList(workspaceId: string) {
  return useQuery({
    queryKey: keys.mine(workspaceId),
    queryFn: async () => (await api.client.GET('/api/student/w/{workspaceId}/homework', { params: { path: { workspaceId } } })).data!,
  });
}

export function useMyHomework(workspaceId: string, assignmentId: string) {
  return useQuery({
    queryKey: [...keys.mine(workspaceId), assignmentId],
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/student/w/{workspaceId}/homework/{assignmentId}', { params: { path: { workspaceId, assignmentId } } })).data!,
  });
}

export function useSubmitHomework(workspaceId: string, assignmentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ text, file, removeFile }: { text: string; file: File | null; removeFile: boolean }) => {
      const form = new FormData();
      form.append('text', text);
      if (removeFile) form.append('removeFile', 'true');
      if (file) form.append('file', file, file.name);
      return (
        await api.client.POST('/api/student/w/{workspaceId}/homework/{assignmentId}/submission', {
          params: { path: { workspaceId, assignmentId } },
          body: form as never,
          bodySerializer: (body: unknown) => body as FormData,
        })
      ).data!;
    },
    onSuccess: async (homework) => {
      queryClient.setQueryData([...keys.mine(workspaceId), assignmentId], homework);
      await queryClient.invalidateQueries({ queryKey: keys.mine(workspaceId), exact: true });
    },
  });
}
