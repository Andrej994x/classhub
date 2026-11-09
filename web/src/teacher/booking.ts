import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type BookingSettings = components['schemas']['BookingSettingsResponse'];
export type BookingRequest = components['schemas']['BookingRequestResponse'];
export type WorkingHoursItem = components['schemas']['WorkingHoursItem'];
export type TimeOffItem = components['schemas']['TimeOffItem'];

const keys = {
  settings: (workspaceId: string) => ['w', workspaceId, 'booking', 'settings'] as const,
  requests: (workspaceId: string) => ['w', workspaceId, 'booking', 'requests'] as const,
  page: (workspaceId: string, page: number) => ['w', workspaceId, 'booking', 'requests', page] as const,
};

export function useBookingSettings(workspaceId: string) {
  return useQuery({
    queryKey: keys.settings(workspaceId),
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/booking/settings', { params: { path: { workspaceId } } })).data!,
  });
}

export function useSaveBookingSettings(workspaceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: components['schemas']['SaveBookingSettingsRequest']) =>
      (await api.client.PUT('/api/w/{workspaceId}/booking/settings', { params: { path: { workspaceId } }, body })).data!,
    onSuccess: (saved) => queryClient.setQueryData(keys.settings(workspaceId), saved),
  });
}

/** One page of requests (pending first, then by time). */
export function useBookingRequests(workspaceId: string, page: number) {
  return useQuery({
    queryKey: keys.page(workspaceId, page),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (await api.client.GET('/api/w/{workspaceId}/booking/requests', { params: { path: { workspaceId }, query: { page, pageSize: 20 } } })).data!,
  });
}

/** Confirm, decline, reschedule or cancel a request; every change reloads the requests and the lessons. */
export function useBookingActions(workspaceId: string, bookingId: string) {
  const queryClient = useQueryClient();
  const onSuccess = async () => {
    await queryClient.invalidateQueries({ queryKey: keys.requests(workspaceId) });
    await queryClient.invalidateQueries({ queryKey: ['w', workspaceId, 'lessons'] });
  };
  const path = { params: { path: { workspaceId, bookingId } } };
  return {
    confirm: useMutation({
      mutationFn: async (version: number) => (await api.client.POST('/api/w/{workspaceId}/booking/requests/{bookingId}/confirm', { ...path, body: { version, reason: null } })).data!,
      onSuccess,
    }),
    decline: useMutation({
      mutationFn: async ({ version, reason }: { version: number; reason: string | null }) =>
        (await api.client.POST('/api/w/{workspaceId}/booking/requests/{bookingId}/decline', { ...path, body: { version, reason } })).data!,
      onSuccess,
    }),
    reschedule: useMutation({
      mutationFn: async ({ version, startLocal }: { version: number; startLocal: string }) =>
        (await api.client.POST('/api/w/{workspaceId}/booking/requests/{bookingId}/reschedule', { ...path, body: { version, startLocal } })).data!,
      onSuccess,
    }),
    cancel: useMutation({
      mutationFn: async ({ version, reason }: { version: number; reason: string }) =>
        (await api.client.POST('/api/w/{workspaceId}/booking/requests/{bookingId}/cancel', { ...path, body: { version, reason } })).data!,
      onSuccess,
    }),
  };
}
