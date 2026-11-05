import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Site = components['schemas']['SiteResponse'];
export type SaveSite = components['schemas']['SaveSiteRequest'];
export type SiteSection = components['schemas']['SiteSection'];

export const SITE_TEMPLATES = ['classic', 'modern', 'minimal'] as const;

/** Same palettes as the API (SitePalettes): key and the colours the swatches show. */
export const SITE_PALETTES = [
  { key: 'ocean', primary: '#0B5FA5', background: '#F7FAFC' },
  { key: 'forest', primary: '#1F6F43', background: '#F6FAF7' },
  { key: 'sunset', primary: '#B23A1E', background: '#FFF8F3' },
  { key: 'plum', primary: '#6B2D8C', background: '#FAF7FC' },
  { key: 'graphite', primary: '#1F2933', background: '#F5F7FA' },
] as const;

/** Section types with a list of items, and what each item field means. */
export const ITEM_SECTIONS = ['experience', 'testimonials', 'faq'] as const;

/** Mirrors SiteSlugs (the API stays authoritative and also checks the reserved list). */
export const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*$/;

const keys = {
  site: (workspaceId: string) => ['w', workspaceId, 'site'] as const,
  slug: (workspaceId: string, slug: string) => ['w', workspaceId, 'site', 'slug', slug] as const,
  onboarding: (workspaceId: string) => ['w', workspaceId, 'onboarding'] as const,
};

/** Always refetched on mount: the editor never starts from a stale cached version. */
export function useSite(workspaceId: string) {
  return useQuery({
    queryKey: keys.site(workspaceId),
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/site', { params: { path: { workspaceId } } })).data!,
  });
}

export function useSaveSite(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: SaveSite) => (await api.client.PUT('/api/w/{workspaceId}/site', { params: { path: { workspaceId } }, body })).data!,
    onSuccess: async (saved) => {
      queryClient.setQueryData(keys.site(workspaceId), saved);
      await queryClient.invalidateQueries({ queryKey: keys.onboarding(workspaceId) });
    },
  });
}

export function useSlugAvailability(workspaceId: string, slug: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.slug(workspaceId, slug),
    enabled,
    staleTime: 10_000,
    queryFn: async () =>
      (await api.client.GET('/api/w/{workspaceId}/site/slug-availability', { params: { path: { workspaceId }, query: { slug } } })).data!,
  });
}

export function usePreview(workspaceId: string) {
  return useMutation({
    mutationFn: async () => (await api.client.POST('/api/w/{workspaceId}/site/preview', { params: { path: { workspaceId } } })).data!,
  });
}
