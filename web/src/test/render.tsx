import { render } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { Providers, createQueryClient } from '../app/Providers.tsx';
import { routes } from '../app/routes.tsx';
import { i18n } from '../i18n/index.ts';

/** Renders the real route tree at a path, in the given UI language. */
export async function renderRoute(path: string, language: 'mk' | 'en' = 'mk') {
  await i18n.changeLanguage(language);
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  const queryClient = createQueryClient();
  return { router, queryClient, ...render(<Providers queryClient={queryClient}><RouterProvider router={router} /></Providers>) };
}
