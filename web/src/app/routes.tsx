import type { RouteObject } from 'react-router';
import { AdminLayout, StudentLayout, TeacherLayout } from './layouts/AppShell.tsx';
import { AdminHomePage, HomePage, NotFoundPage, StudentHomePage, TeacherDashboardPage } from './pages/Pages.tsx';

/**
 * SPA routes. Area prefixes must match the server-side SPA fallback in ClassHub.Api (SpaHosting) and the
 * reserved slugs (public teacher sites live at /{slug} and are rendered by Razor Pages from R1-P4).
 * Auth screens are added in R0-P7; route guards there are UX only, the API enforces access.
 */
export const routes: RouteObject[] = [
  { path: '/', element: <HomePage /> },
  {
    path: '/app',
    element: <TeacherLayout />,
    children: [
      { index: true, element: <TeacherDashboardPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  { path: '/student', element: <StudentLayout />, children: [{ index: true, element: <StudentHomePage /> }] },
  { path: '/admin', element: <AdminLayout />, children: [{ index: true, element: <AdminHomePage /> }] },
  { path: '*', element: <NotFoundPage /> },
];
