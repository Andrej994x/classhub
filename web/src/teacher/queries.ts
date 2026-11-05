import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Catalog = components['schemas']['CatalogResponse'];
export type TeacherProfile = components['schemas']['TeacherProfileResponse'];
export type UpdateTeacherProfile = components['schemas']['UpdateTeacherProfileRequest'];
export type Onboarding = components['schemas']['OnboardingResponse'];
export type Workspace = components['schemas']['WorkspaceResponse'];

/** Client-side photo checks mirror the API (the API stays authoritative and re-encodes the image). */
export const PHOTO_MAX_BYTES = 5 * 1024 * 1024;
export const PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

const keys = {
  catalog: ['catalog'] as const,
  workspace: (id: string) => ['w', id] as const,
  profile: (id: string) => ['w', id, 'profile'] as const,
  onboarding: (id: string) => ['w', id, 'onboarding'] as const,
};

export function useCatalog() {
  return useQuery({
    queryKey: keys.catalog,
    queryFn: async () => (await api.client.GET('/api/catalog')).data!,
    staleTime: 60 * 60_000, // reference data
  });
}

export function useWorkspace(workspaceId: string) {
  return useQuery({
    queryKey: keys.workspace(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}', { params: { path: { workspaceId } } })).data!,
  });
}

export function useProfile(workspaceId: string) {
  return useQuery({
    queryKey: keys.profile(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/profile', { params: { path: { workspaceId } } })).data!,
  });
}

export function useOnboarding(workspaceId: string) {
  return useQuery({
    queryKey: keys.onboarding(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/onboarding', { params: { path: { workspaceId } } })).data!,
  });
}

/** Saves the profile and, when they changed, the workspace's time zone and currency (atomically, audited by the API). */
export function useSaveProfile(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    // One request: the API saves the profile and the (optional) time zone/currency in one transaction.
    mutationFn: async (profile: UpdateTeacherProfile) =>
      (await api.client.PUT('/api/w/{workspaceId}/profile', { params: { path: { workspaceId } }, body: profile })).data!,
    onSuccess: async (saved) => {
      queryClient.setQueryData(keys.profile(workspaceId), saved);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.onboarding(workspaceId) }),
        queryClient.invalidateQueries({ queryKey: keys.workspace(workspaceId) }),
      ]);
    },
  });
}

export function usePhoto(workspaceId: string) {
  const queryClient = useQueryClient();
  const refresh = async (profile?: TeacherProfile) => {
    if (profile) queryClient.setQueryData(keys.profile(workspaceId), profile);
    else await queryClient.invalidateQueries({ queryKey: keys.profile(workspaceId) });
    await queryClient.invalidateQueries({ queryKey: keys.onboarding(workspaceId) });
  };
  const upload = useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData();
      form.append('file', file);
      const { data } = await api.client.PUT('/api/w/{workspaceId}/profile/photo', {
        params: { path: { workspaceId } },
        body: form as never,
        bodySerializer: (body: unknown) => body as FormData,
      });
      return data!;
    },
    onSuccess: refresh,
  });
  const remove = useMutation({
    mutationFn: async () => {
      await api.client.DELETE('/api/w/{workspaceId}/profile/photo', { params: { path: { workspaceId } } });
    },
    onSuccess: () => refresh(),
  });
  return { upload, remove };
}

/** Same-origin URL of the private photo; `version` changes with every new photo (cache busting). */
export const photoUrl = (workspaceId: string, version: string | null | undefined) =>
  `/api/w/${workspaceId}/profile/photo${version ? `?v=${encodeURIComponent(version)}` : ''}`;
