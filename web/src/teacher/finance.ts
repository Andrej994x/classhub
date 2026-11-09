import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type StudentFinance = components['schemas']['StudentFinanceResponse'];
export type Package = components['schemas']['PackageResponse'];
export type FinanceEntry = components['schemas']['FinanceEntryResponse'];
export type FinanceOverview = components['schemas']['FinanceOverviewResponse'];
export type FinanceReport = components['schemas']['FinanceReportResponse'];

export const PAYMENT_METHODS = ['Cash', 'BankTransfer', 'Card', 'Other'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'finance'] as const,
  student: (workspaceId: string, studentId: string) => ['w', workspaceId, 'finance', 'student', studentId] as const,
  overview: (workspaceId: string) => ['w', workspaceId, 'finance', 'overview'] as const,
  report: (workspaceId: string, from: string, to: string) => ['w', workspaceId, 'finance', 'report', from, to] as const,
};

export function useStudentFinance(workspaceId: string, studentId: string) {
  return useQuery({
    queryKey: keys.student(workspaceId, studentId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/finance/students/{studentId}', { params: { path: { workspaceId, studentId } } })).data!,
  });
}

export function useFinanceOverview(workspaceId: string) {
  return useQuery({
    queryKey: keys.overview(workspaceId),
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/finance/overview', { params: { path: { workspaceId } } })).data!,
  });
}

export function useFinanceReport(workspaceId: string, from: string | null, to: string | null) {
  return useQuery({
    queryKey: keys.report(workspaceId, from ?? '', to ?? ''),
    enabled: !!from && !!to,
    retry: false,
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/finance/report', { params: { path: { workspaceId }, query: { from: from!, to: to! } } })).data!,
  });
}

/** Every change answers with the student's finance; the overview and reports are refreshed. */
function useFinanceMutation<TInput>(workspaceId: string, studentId: string, run: (input: TInput) => Promise<StudentFinance>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (finance) => {
      queryClient.setQueryData(keys.student(workspaceId, studentId), finance);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.overview(workspaceId) }),
        queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'report'] }),
      ]);
    },
  });
}

export type PackageInput = { offerId: string } | { title: string; lessonCount: number; priceMinor: number; currency: string };

export function useAssignPackage(workspaceId: string, studentId: string) {
  return useFinanceMutation(workspaceId, studentId, async (input: PackageInput) =>
    (
      await api.client.POST('/api/w/{workspaceId}/finance/students/{studentId}/packages', {
        params: { path: { workspaceId, studentId } },
        body: 'offerId' in input ? { offerId: input.offerId, title: null, lessonCount: null, priceMinor: null, currency: null } : { offerId: null, ...input },
      })
    ).data!,
  );
}

export function useClosePackage(workspaceId: string, studentId: string) {
  return useFinanceMutation(workspaceId, studentId, async ({ packageId, version }: { packageId: string; version: number }) =>
    (await api.client.POST('/api/w/{workspaceId}/finance/packages/{packageId}/close', { params: { path: { workspaceId, packageId } }, body: { version } })).data!,
  );
}

export function useRecordPayment(workspaceId: string, studentId: string) {
  return useFinanceMutation(
    workspaceId,
    studentId,
    async (body: { amountMinor: number; currency: string; paidOn: string; method: PaymentMethod; note: string | null; packageId: string | null }) =>
      (await api.client.POST('/api/w/{workspaceId}/finance/students/{studentId}/payments', { params: { path: { workspaceId, studentId } }, body })).data!,
  );
}

export function useAdjustBalance(workspaceId: string, studentId: string) {
  return useFinanceMutation(workspaceId, studentId, async (body: { amountMinor: number; currency: string; reason: string }) =>
    (await api.client.POST('/api/w/{workspaceId}/finance/students/{studentId}/adjustments', { params: { path: { workspaceId, studentId } }, body })).data!,
  );
}

export function useAdjustLessons(workspaceId: string, studentId: string) {
  return useFinanceMutation(workspaceId, studentId, async ({ packageId, quantity, reason }: { packageId: string; quantity: number; reason: string }) =>
    (
      await api.client.POST('/api/w/{workspaceId}/finance/packages/{packageId}/lesson-adjustments', {
        params: { path: { workspaceId, packageId } },
        body: { quantity, reason, lessonId: null },
      })
    ).data!,
  );
}

/** Reverses a payment or a lesson entry (the entry's kind tells which). */
export function useReverse(workspaceId: string, studentId: string) {
  return useFinanceMutation(workspaceId, studentId, async ({ entry, reason }: { entry: FinanceEntry; reason: string }) =>
    entry.kind === 'Payment'
      ? (await api.client.POST('/api/w/{workspaceId}/finance/payments/{paymentId}/reverse', { params: { path: { workspaceId, paymentId: entry.id } }, body: { reason } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/finance/ledger/{entryId}/reverse', { params: { path: { workspaceId, entryId: entry.id } }, body: { reason } })).data!,
  );
}
