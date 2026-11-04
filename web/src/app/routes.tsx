import type { RouteObject } from 'react-router';
import { AdminLayout, AuthLayout, StudentLayout, TeacherLayout } from './layouts/AppShell.tsx';
import { DevMailboxPage } from './pages/DevMailboxPage.tsx';
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyEmailPage } from './pages/auth/AuthPages.tsx';
import {
  AdminHomePage,
  HomePage,
  NotFoundContent,
  NotFoundPage,
  StudentHomePage,
  TeacherHomePage,
  WorkspaceDashboardPage,
} from './pages/Pages.tsx';

/**
 * SPA routes. Area prefixes must match the server-side SPA fallback in ClassHub.Api (SpaHosting) and the
 * reserved slugs (public teacher sites live at /{slug} and are rendered by Razor Pages from R1-P4).
 * Route guards (RequireAuth in the area layouts) are UX only; the API enforces access.
 */
export const routes: RouteObject[] = [
  { path: '/', element: <HomePage /> },
  {
    element: <AuthLayout />,
    children: [
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/verify-email', element: <VerifyEmailPage /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
      { path: '/reset-password', element: <ResetPasswordPage /> },
      // Development and E2E only (the API and the server route do not exist elsewhere).
      { path: '/dev/mailbox', element: <DevMailboxPage /> },
    ],
  },
  {
    path: '/app',
    element: <TeacherLayout />,
    children: [
      { index: true, element: <TeacherHomePage /> },
      { path: 'w/:workspaceId', element: <WorkspaceDashboardPage /> },
      { path: '*', element: <NotFoundContent /> },
    ],
  },
  { path: '/student', element: <StudentLayout />, children: [{ index: true, element: <StudentHomePage /> }] },
  { path: '/admin', element: <AdminLayout />, children: [{ index: true, element: <AdminHomePage /> }] },
  { path: '*', element: <NotFoundPage /> },
];
