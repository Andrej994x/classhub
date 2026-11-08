import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type StudentTeacher = components['schemas']['StudentTeacherResponse'];
export type InvitationPreview = components['schemas']['InvitationPreviewResponse'];

/** The teachers the signed-in user is a student of (D-16: the portal groups everything per teacher). */
export function useMyTeachers() {
  return useQuery({
    queryKey: ['student', 'teachers'],
    queryFn: async () => (await api.client.GET('/api/student/teachers')).data!,
  });
}

export function useMyStudentProfile(workspaceId: string) {
  return useQuery({
    queryKey: ['student', 'w', workspaceId, 'profile'],
    queryFn: async () => (await api.client.GET('/api/student/w/{workspaceId}/profile', { params: { path: { workspaceId } } })).data!,
  });
}

export function useInvitationPreview(token: string | null) {
  return useQuery({
    queryKey: ['student-invitation', token],
    enabled: token !== null,
    retry: false,
    queryFn: async () => (await api.client.POST('/api/student-invitations/preview', { body: { token } })).data!,
  });
}

/** Links the signed-in account. */
export function useAcceptInvitation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { token: string; guardianConsent: boolean }) => (await api.client.POST('/api/student-invitations/accept', { body: input })).data!,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['student'] }),
  });
}

/** Creates the account and signs it in: nothing cached for an earlier identity may survive. */
export function useAcceptWithAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { token: string; password: string; locale: string; guardianConsent: boolean }) =>
      (await api.client.POST('/api/student-invitations/accept-with-account', { body: input })).data!,
    onSuccess: () => queryClient.clear(),
  });
}

const pendingKey = 'classhub.studentInvitation';

/** Keeps the invitation token for this tab while the student signs in (never in the URL after the first read). */
export const pendingInvitation = {
  read(): string | null {
    try {
      return sessionStorage.getItem(pendingKey);
    } catch {
      return null;
    }
  },
  write(token: string) {
    try {
      sessionStorage.setItem(pendingKey, token);
    } catch {
      // storage unavailable: the student opens the link again after signing in
    }
  },
  clear() {
    try {
      sessionStorage.removeItem(pendingKey);
    } catch {
      // ignore
    }
  },
};
