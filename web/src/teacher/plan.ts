import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type TeacherPlan = components['schemas']['TeacherPlanResponse'];

/** The workspace's plan and its limits; always fresh (staff may have activated Pro meanwhile). */
export function usePlan(workspaceId: string) {
  return useQuery({
    queryKey: ['w', workspaceId, 'plan'] as const,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/plan', { params: { path: { workspaceId } } })).data!,
  });
}
