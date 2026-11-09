import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type StudentTeacher = components['schemas']['StudentTeacherResponse'];
export type NotificationPreferences = components['schemas']['NotificationPreferencesResponse'];
export type InvitationPreview = components['schemas']['InvitationPreviewResponse'];
type MyLessonWire = components['schemas']['StudentLessonResponse'];
/** A lesson in the portal; the API sends its enums as names. */
export type MyLesson = Omit<MyLessonWire, 'mode' | 'status' | 'attendance'> & {
  mode: 'Online' | 'InPerson';
  status: 'Scheduled' | 'Completed' | 'Cancelled';
  attendance: 'Present' | 'Absent' | 'Excused' | null;
};
export type MyLessons = Omit<components['schemas']['StudentLessonsResponse'], 'upcoming' | 'past'> & { upcoming: MyLesson[]; past: MyLesson[] };

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

/** The student's own lessons with one teacher (R2-P12): the next ones, recent past ones and their attendance. */
export function useMyLessons(workspaceId: string) {
  return useQuery({
    queryKey: ['student', 'w', workspaceId, 'lessons'],
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/student/w/{workspaceId}/lessons', { params: { path: { workspaceId } } })).data as unknown as MyLessons,
  });
}

/** The optional notifications of the signed-in user (R2-P11): lesson reminders and new homework. */
export function useNotificationPreferences() {
  return useQuery({
    queryKey: ['me', 'notification-preferences'],
    queryFn: async () => (await api.client.GET('/api/me/notification-preferences')).data!,
  });
}

export function useSaveNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: Partial<NotificationPreferences>) =>
      (await api.client.PUT('/api/me/notification-preferences', { body: { lessonReminders: body.lessonReminders ?? null, homeworkEmails: body.homeworkEmails ?? null } })).data!,
    onSuccess: (saved) => queryClient.setQueryData(['me', 'notification-preferences'], saved),
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
