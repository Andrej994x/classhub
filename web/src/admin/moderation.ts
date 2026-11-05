import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type AdminReport = components['schemas']['AdminReportResponse'];
export type HiddenContent = components['schemas']['HiddenContentResponse'];
export type CatalogEntry = components['schemas']['AdminCatalogEntry'];
export type CatalogKind = 'categories' | 'subjects' | 'levels';

export const REPORT_STATUSES = ['Open', 'Resolved', 'Dismissed'] as const;

export function useReports(status: string, page: number) {
  return useQuery({
    queryKey: ['admin', 'reports', status, page] as const,
    placeholderData: keepPreviousData,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/reports', { params: { query: { status: status || undefined, page } } })).data!,
  });
}

/** Hide the reported content (reason shown to the teacher) or dismiss the report. */
export function useDecideReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ reportId, hide, reason }: { reportId: string; hide: boolean; reason: string }) =>
      hide
        ? (await api.client.POST('/api/admin/reports/{reportId}/hide', { params: { path: { reportId } }, body: { reason } })).data!
        : (await api.client.POST('/api/admin/reports/{reportId}/dismiss', { params: { path: { reportId } }, body: { reason: reason || null } })).data!,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin'] }),
  });
}

export function useHiddenContent() {
  return useQuery({
    queryKey: ['admin', 'hidden'] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/moderation/hidden')).data!,
  });
}

export function useRestoreContent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { item: HiddenContent; reason: string }) => {
      await api.client.POST('/api/admin/moderation/restore', {
        body: { workspaceId: input.item.workspaceId, target: input.item.target, sectionType: input.item.sectionType ?? null, testId: input.item.testId ?? null, reason: input.reason },
      });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin'] }),
  });
}

export function useAdminCatalog() {
  return useQuery({
    queryKey: ['admin', 'catalog'] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/admin/catalog')).data!,
  });
}

export interface CatalogEntryInput {
  code?: string | null;
  nameMk: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
  categoryId?: string | null;
}

export function useSaveCatalogEntry(kind: CatalogKind) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, input }: { id?: string; input: CatalogEntryInput }) => {
      const body = { code: input.code ?? null, nameMk: input.nameMk, nameEn: input.nameEn, sortOrder: input.sortOrder, isActive: input.isActive, categoryId: input.categoryId ?? null };
      return id
        ? (await api.client.PUT('/api/admin/catalog/{kind}/{id}', { params: { path: { kind, id } }, body })).data!
        : (await api.client.POST('/api/admin/catalog/{kind}', { params: { path: { kind } }, body })).data!;
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'catalog'] });
      // Teachers' pickers read the public catalog: refresh it too.
      await queryClient.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}
