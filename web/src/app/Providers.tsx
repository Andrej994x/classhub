import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { I18nextProvider } from 'react-i18next';
import { api } from '../api/client.ts';
import { ApiError } from '../api/errors.ts';
import { meQueryKey } from '../auth/session.ts';
import { i18n } from '../i18n/index.ts';

export function createQueryClient() {
  // A 401 from any protected request means the session ended (expired, revoked, signed out elsewhere):
  // drop the cached account so the guards send the user to sign in instead of showing a dead dashboard.
  const onAuthFailure = (error: unknown) => {
    if (error instanceof ApiError && error.status === 401 && client.getQueryData(meQueryKey)) {
      api.resetToken();
      client.setQueryData(meQueryKey, null);
    }
  };
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: onAuthFailure }),
    mutationCache: new MutationCache({ onError: onAuthFailure }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Do not retry client errors (401/403/404/validation); retry transient failures once.
        retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 1,
      },
    },
  });
  return client;
}

export function Providers({ children, queryClient = createQueryClient() }: { children: ReactNode; queryClient?: QueryClient }) {
  return (
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </I18nextProvider>
  );
}
