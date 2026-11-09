import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Material = components['schemas']['MaterialResponse'];
export type MaterialListItem = components['schemas']['MaterialListItem'];
export type StudentMaterial = components['schemas']['StudentMaterialResponse'];

/** Mirrors the API's allow-list and size limit (R2-P8); the API stays authoritative and checks the file's signature. */
export const MATERIAL_EXTENSIONS = ['.pdf', '.png', '.jpg', '.jpeg', '.webp', '.docx', '.xlsx', '.pptx', '.txt'] as const;
export const MATERIAL_MAX_BYTES = 20 * 1024 * 1024;

export function materialFileProblem(file: File): 'type' | 'size' | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!(MATERIAL_EXTENSIONS as readonly string[]).includes(extension)) return 'type';
  if (file.size === 0 || file.size > MATERIAL_MAX_BYTES) return 'size';
  return null;
}

/** "350 KB", "4.2 MB" in the UI language. */
export function formatBytes(bytes: number, locale: string): string {
  const format = (value: number, unit: string) => `${new Intl.NumberFormat(locale, { maximumFractionDigits: value < 10 ? 1 : 0 }).format(value)} ${unit}`;
  if (bytes < 1024) return format(bytes, 'B');
  if (bytes < 1024 * 1024) return format(bytes / 1024, 'KB');
  return format(bytes / (1024 * 1024), 'MB');
}

/** Same-origin download URLs (cookie authenticated; the API sends attachments, images inline). */
export const materialFileUrl = (workspaceId: string, materialId: string) => `/api/w/${workspaceId}/materials/${materialId}/file`;
export const myMaterialFileUrl = (workspaceId: string, materialId: string) => `/api/student/w/${workspaceId}/materials/${materialId}/file`;

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'materials'] as const,
  list: (workspaceId: string, filter: MaterialFilter) => ['w', workspaceId, 'materials', 'list', filter] as const,
  one: (workspaceId: string, materialId: string) => ['w', workspaceId, 'materials', materialId] as const,
};

export interface MaterialFilter {
  q: string;
  subjectId: string;
  archived: boolean;
  page: number;
}

export function useMaterials(workspaceId: string, filter: MaterialFilter) {
  return useQuery({
    queryKey: keys.list(workspaceId, filter),
    placeholderData: (previous) => previous,
    queryFn: async () =>
      (
        await api.client.GET('/api/w/{workspaceId}/materials', {
          params: { path: { workspaceId }, query: { q: filter.q.trim() || undefined, subjectId: filter.subjectId || undefined, archived: filter.archived || undefined, page: filter.page } },
        })
      ).data!,
  });
}

export function useMaterial(workspaceId: string, materialId: string) {
  return useQuery({
    queryKey: keys.one(workspaceId, materialId),
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/materials/{materialId}', { params: { path: { workspaceId, materialId } } })).data!,
  });
}

function useMaterialMutation<TInput>(workspaceId: string, run: (input: TInput) => Promise<Material>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (material) => {
      queryClient.setQueryData(keys.one(workspaceId, material.id), material);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useUploadMaterial(workspaceId: string) {
  return useMaterialMutation(workspaceId, async ({ file, title, description, subjectId }: { file: File; title: string; description: string; subjectId: string }) => {
    const form = new FormData();
    form.append('title', title);
    if (description) form.append('description', description);
    if (subjectId) form.append('subjectId', subjectId);
    form.append('file', file, file.name);
    return (
      await api.client.POST('/api/w/{workspaceId}/materials', {
        params: { path: { workspaceId } },
        body: form as never,
        bodySerializer: (body: unknown) => body as FormData,
      })
    ).data!;
  });
}

export function useUpdateMaterial(workspaceId: string, materialId: string) {
  return useMaterialMutation(workspaceId, async (body: { title: string; description: string | null; subjectId: string | null; version: number }) =>
    (await api.client.PUT('/api/w/{workspaceId}/materials/{materialId}', { params: { path: { workspaceId, materialId } }, body })).data!,
  );
}

export function useArchiveMaterial(workspaceId: string, materialId: string) {
  return useMaterialMutation(workspaceId, async ({ archived, version }: { archived: boolean; version: number }) =>
    archived
      ? (await api.client.POST('/api/w/{workspaceId}/materials/{materialId}/archive', { params: { path: { workspaceId, materialId } }, body: { version } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/materials/{materialId}/restore', { params: { path: { workspaceId, materialId } }, body: { version } })).data!,
  );
}

export function useDeleteMaterial(workspaceId: string, materialId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (version: number) => {
      await api.client.POST('/api/w/{workspaceId}/materials/{materialId}/delete', { params: { path: { workspaceId, materialId } }, body: { version } });
    },
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: keys.one(workspaceId, materialId) });
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'list'] });
    },
  });
}

export function useAssignMaterial(workspaceId: string, materialId: string) {
  return useMaterialMutation(workspaceId, async (target: { studentId: string } | { groupId: string }) =>
    (
      await api.client.POST('/api/w/{workspaceId}/materials/{materialId}/assignments', {
        params: { path: { workspaceId, materialId } },
        body: 'studentId' in target ? { studentId: target.studentId, groupId: null } : { studentId: null, groupId: target.groupId },
      })
    ).data!,
  );
}

export function useUnassignMaterial(workspaceId: string, materialId: string) {
  return useMaterialMutation(workspaceId, async (assignmentId: string) =>
    (await api.client.DELETE('/api/w/{workspaceId}/materials/{materialId}/assignments/{assignmentId}', { params: { path: { workspaceId, materialId, assignmentId } } })).data!,
  );
}

/** The student portal: materials assigned to the student or their groups. */
export function useMyMaterials(workspaceId: string) {
  return useQuery({
    queryKey: ['student', 'w', workspaceId, 'materials'],
    queryFn: async () => (await api.client.GET('/api/student/w/{workspaceId}/materials', { params: { path: { workspaceId } } })).data!,
  });
}
