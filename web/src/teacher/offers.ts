import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Offer = components['schemas']['OfferResponse'];
export type SaveOffer = components['schemas']['SaveOfferRequest'];

export const OFFER_KINDS = ['SingleLesson', 'Package', 'GroupProgram'] as const;
export const OFFER_MODES = ['Online', 'InPerson', 'Hybrid'] as const;

const keys = {
  list: (workspaceId: string) => ['w', workspaceId, 'offers'] as const,
  one: (workspaceId: string, offerId: string) => ['w', workspaceId, 'offers', offerId] as const,
  onboarding: (workspaceId: string) => ['w', workspaceId, 'onboarding'] as const,
};

/** Integers may arrive as strings in the generated types (OpenAPI int32/int64 patterns); normalize. */
export const num = (value: number | string | null | undefined): number | null =>
  value === null || value === undefined ? null : Number(value);

export function useOffers(workspaceId: string) {
  return useQuery({
    queryKey: keys.list(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/offers', { params: { path: { workspaceId } } })).data!,
  });
}

export function useOffer(workspaceId: string, offerId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, offerId ?? 'new'),
    enabled: offerId !== undefined,
    queryFn: async () =>
      (await api.client.GET('/api/w/{workspaceId}/offers/{offerId}', { params: { path: { workspaceId, offerId: offerId! } } })).data!,
  });
}

export function useSaveOffer(workspaceId: string, offerId: string | undefined) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: SaveOffer) =>
      offerId
        ? (await api.client.PUT('/api/w/{workspaceId}/offers/{offerId}', { params: { path: { workspaceId, offerId } }, body })).data!
        : (await api.client.POST('/api/w/{workspaceId}/offers', { params: { path: { workspaceId } }, body })).data!,
    onSuccess: async (saved) => {
      queryClient.setQueryData(keys.one(workspaceId, saved.id), saved);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.list(workspaceId), exact: true }),
        queryClient.invalidateQueries({ queryKey: keys.onboarding(workspaceId) }),
      ]);
    },
  });
}

export function useArchiveOffer(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (offerId: string) => {
      await api.client.DELETE('/api/w/{workspaceId}/offers/{offerId}', { params: { path: { workspaceId, offerId } } });
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.list(workspaceId) }),
        queryClient.invalidateQueries({ queryKey: keys.onboarding(workspaceId) }),
      ]);
    },
  });
}

export function useReorderOffers(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (offerIds: string[]) =>
      (await api.client.PUT('/api/w/{workspaceId}/offers/order', { params: { path: { workspaceId } }, body: { offerIds } })).data!,
    onSuccess: (offers) => queryClient.setQueryData(keys.list(workspaceId), offers),
  });
}
