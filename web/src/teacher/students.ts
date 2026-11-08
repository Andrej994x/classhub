import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Student = components['schemas']['StudentResponse'];
export type StudentListItem = components['schemas']['StudentListItem'];
export type StudentNote = components['schemas']['StudentNoteResponse'];

export interface StudentInput {
  firstName: string;
  lastName: string | null;
  email: string | null;
  phone: string | null;
  isMinor: boolean;
  guardianName: string | null;
  guardianEmail: string | null;
  guardianPhone: string | null;
  subjectIds: string[];
}

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'students'] as const,
  list: (workspaceId: string, filters: { q: string; archived: boolean; page: number }) => ['w', workspaceId, 'students', 'list', filters] as const,
  one: (workspaceId: string, studentId: string) => ['w', workspaceId, 'students', studentId] as const,
  notes: (workspaceId: string, studentId: string) => ['w', workspaceId, 'students', studentId, 'notes'] as const,
};

export function useStudents(workspaceId: string, filters: { q: string; archived: boolean; page: number }) {
  return useQuery({
    queryKey: keys.list(workspaceId, filters),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await api.client.GET('/api/w/{workspaceId}/students', {
          params: { path: { workspaceId }, query: { q: filters.q.trim() || undefined, archived: filters.archived || undefined, page: filters.page, pageSize: 20 } },
        })
      ).data!,
  });
}

/** Always refetched on mount: saves send the version that was really loaded. */
export function useStudent(workspaceId: string, studentId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, studentId ?? 'new'),
    enabled: studentId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/students/{studentId}', { params: { path: { workspaceId, studentId: studentId! } } })).data!,
  });
}

export function useSaveStudent(workspaceId: string, studentId?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ input, version }: { input: StudentInput; version: number | null }) =>
      studentId
        ? (await api.client.PUT('/api/w/{workspaceId}/students/{studentId}', { params: { path: { workspaceId, studentId } }, body: { ...input, version } })).data!
        : (await api.client.POST('/api/w/{workspaceId}/students', { params: { path: { workspaceId } }, body: { ...input, version: null } })).data!,
    onSuccess: async (student) => {
      queryClient.setQueryData(keys.one(workspaceId, student.id), student);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useArchiveStudent(workspaceId: string, studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ archived, version }: { archived: boolean; version: number }) =>
      archived
        ? (await api.client.POST('/api/w/{workspaceId}/students/{studentId}/archive', { params: { path: { workspaceId, studentId } }, body: { version } })).data!
        : (await api.client.POST('/api/w/{workspaceId}/students/{studentId}/restore', { params: { path: { workspaceId, studentId } }, body: { version } })).data!,
    onSuccess: async (student) => {
      queryClient.setQueryData(keys.one(workspaceId, studentId), student);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useStudentNotes(workspaceId: string, studentId: string) {
  return useQuery({
    queryKey: keys.notes(workspaceId, studentId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/students/{studentId}/notes', { params: { path: { workspaceId, studentId } } })).data!,
  });
}

export function useAddStudentNote(workspaceId: string, studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (text: string) =>
      (await api.client.POST('/api/w/{workspaceId}/students/{studentId}/notes', { params: { path: { workspaceId, studentId } }, body: { text } })).data!,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.notes(workspaceId, studentId) }),
  });
}

/** D-12: link the request to the student with the same email, or create one. */
export function useConvertLead(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (leadId: string) => (await api.client.POST('/api/w/{workspaceId}/students/from-lead', { params: { path: { workspaceId } }, body: { leadId } })).data!,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: keys.all(workspaceId) });
      await queryClient.invalidateQueries({ queryKey: ['w', workspaceId, 'leads'] });
    },
  });
}

/** R2-P3: invite the student to their own account, take back a pending invitation, or end the account's access. */
export function useStudentAccount(workspaceId: string, studentId: string) {
  const queryClient = useQueryClient();
  const onSuccess = (student: Student) => queryClient.setQueryData(keys.one(workspaceId, studentId), student);
  const path = { params: { path: { workspaceId, studentId } } };
  return {
    invite: useMutation({
      mutationFn: async (email: string | null) => (await api.client.POST('/api/w/{workspaceId}/students/{studentId}/invitation', { ...path, body: { email } })).data!,
      onSuccess,
    }),
    revoke: useMutation({
      mutationFn: async () => (await api.client.DELETE('/api/w/{workspaceId}/students/{studentId}/invitation', path)).data!,
      onSuccess,
    }),
    unlink: useMutation({
      mutationFn: async () => (await api.client.DELETE('/api/w/{workspaceId}/students/{studentId}/account', path)).data!,
      onSuccess,
    }),
  };
}
