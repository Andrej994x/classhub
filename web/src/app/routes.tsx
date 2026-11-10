import type { RouteObject } from 'react-router';
import { AdminLayout, AuthLayout, StudentLayout, TeacherLayout } from './layouts/AppShell.tsx';
import { AdminAuditPage } from './pages/admin/AdminAuditPage.tsx';
import { AdminOverviewPage } from './pages/admin/AdminOverviewPage.tsx';
import { AdminCatalogPage } from './pages/admin/AdminCatalogPage.tsx';
import { AdminToolboxPage } from './pages/admin/AdminToolboxPage.tsx';
import { AdminHiddenPage, AdminReportsPage } from './pages/admin/AdminModerationPages.tsx';
import { AdminPlansPage } from './pages/admin/AdminPlansPage.tsx';
import { AdminSecurityPage } from './pages/admin/AdminSecurityPage.tsx';
import { AdminUserPage } from './pages/admin/AdminUserPage.tsx';
import { AdminUsersPage } from './pages/admin/AdminUsersPage.tsx';
import { DevMailboxPage } from './pages/DevMailboxPage.tsx';
import { LeadDetailPage } from './pages/leads/LeadDetailPage.tsx';
import { LeadsPage } from './pages/leads/LeadsPage.tsx';
import { TestEditPage } from './pages/tests/TestEditPage.tsx';
import { TestsPage } from './pages/tests/TestsPage.tsx';
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyEmailPage } from './pages/auth/AuthPages.tsx';
import { OfferEditPage } from './pages/offers/OfferEditPage.tsx';
import { OffersPage } from './pages/offers/OffersPage.tsx';
import { PlanPage } from './pages/plan/PlanPage.tsx';
import { GroupEditPage, GroupsPage } from './pages/groups/GroupsPages.tsx';
import { MaterialPage, MaterialsPage } from './pages/materials/MaterialsPages.tsx';
import { ToolboxPage } from './pages/toolbox/ToolboxPage.tsx';
import { ActivityLibraryPage } from './pages/activities/ActivityLibraryPage.tsx';
import { DrawingBoardPage } from './pages/boards/DrawingBoardPage.tsx';
import { GeometryBoardPage } from './pages/boards/GeometryBoardPage.tsx';
import { HomeworkEditPage, HomeworkPage, StudentHomeworkPage } from './pages/homework/HomeworkPages.tsx';
import { FinancePage } from './pages/finance/FinancePages.tsx';
import { BookingPage } from './pages/booking/BookingPage.tsx';
import { CalendarPage } from './pages/lessons/CalendarPage.tsx';
import { LessonEditPage } from './pages/lessons/LessonEditPage.tsx';
import { SeriesPage } from './pages/lessons/SeriesPage.tsx';
import { StudentEditPage, StudentsPage } from './pages/students/StudentsPages.tsx';
import { InvitationPage, StudentHomePage, StudentSettingsPage, StudentTeacherPage } from './pages/student/StudentPortalPages.tsx';
import { ProfilePage } from './pages/profile/ProfilePage.tsx';
import { SiteEditorPage } from './pages/site/SiteEditorPage.tsx';
import {
  HomePage,
  NotFoundContent,
  NotFoundPage,
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
      // The student invitation link: works signed in (link the account) or not (create one).
      { path: '/student/invitation', element: <InvitationPage /> },
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
      { path: 'w/:workspaceId/profile', element: <ProfilePage /> },
      { path: 'w/:workspaceId/plan', element: <PlanPage /> },
      { path: 'w/:workspaceId/students', element: <StudentsPage /> },
      { path: 'w/:workspaceId/students/new', element: <StudentEditPage /> },
      { path: 'w/:workspaceId/students/:studentId', element: <StudentEditPage /> },
      { path: 'w/:workspaceId/calendar', element: <CalendarPage /> },
      { path: 'w/:workspaceId/booking', element: <BookingPage /> },
      { path: 'w/:workspaceId/lessons/new', element: <LessonEditPage /> },
      { path: 'w/:workspaceId/lessons/:lessonId', element: <LessonEditPage /> },
      { path: 'w/:workspaceId/series/:seriesId', element: <SeriesPage /> },
      { path: 'w/:workspaceId/groups', element: <GroupsPage /> },
      { path: 'w/:workspaceId/groups/new', element: <GroupEditPage /> },
      { path: 'w/:workspaceId/groups/:groupId', element: <GroupEditPage /> },
      { path: 'w/:workspaceId/materials', element: <MaterialsPage /> },
      { path: 'w/:workspaceId/materials/:materialId', element: <MaterialPage /> },
      { path: 'w/:workspaceId/homework', element: <HomeworkPage /> },
      { path: 'w/:workspaceId/homework/new', element: <HomeworkEditPage /> },
      { path: 'w/:workspaceId/homework/:homeworkId', element: <HomeworkEditPage /> },
      { path: 'w/:workspaceId/finance', element: <FinancePage /> },
      { path: 'w/:workspaceId/toolbox', element: <ToolboxPage /> },
      { path: 'w/:workspaceId/activities', element: <ActivityLibraryPage /> },
      { path: 'w/:workspaceId/toolbox/drawing-board', element: <DrawingBoardPage /> },
      { path: 'w/:workspaceId/toolbox/geometry-board', element: <GeometryBoardPage /> },
      { path: 'w/:workspaceId/offers', element: <OffersPage /> },
      { path: 'w/:workspaceId/offers/new', element: <OfferEditPage /> },
      { path: 'w/:workspaceId/offers/:offerId', element: <OfferEditPage /> },
      { path: 'w/:workspaceId/site', element: <SiteEditorPage /> },
      { path: 'w/:workspaceId/leads', element: <LeadsPage /> },
      { path: 'w/:workspaceId/leads/:leadId', element: <LeadDetailPage /> },
      { path: 'w/:workspaceId/tests', element: <TestsPage /> },
      { path: 'w/:workspaceId/tests/new', element: <TestEditPage /> },
      { path: 'w/:workspaceId/tests/:testId', element: <TestEditPage /> },
      { path: '*', element: <NotFoundContent /> },
    ],
  },
  {
    path: '/student',
    element: <StudentLayout />,
    children: [
      { index: true, element: <StudentHomePage /> },
      { path: 'w/:workspaceId', element: <StudentTeacherPage /> },
      { path: 'w/:workspaceId/homework/:assignmentId', element: <StudentHomeworkPage /> },
      { path: 'settings', element: <StudentSettingsPage /> },
    ],
  },
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <AdminOverviewPage /> },
      { path: 'users', element: <AdminUsersPage /> },
      { path: 'users/:userId', element: <AdminUserPage /> },
      { path: 'audit', element: <AdminAuditPage /> },
      { path: 'plans', element: <AdminPlansPage /> },
      { path: 'reports', element: <AdminReportsPage /> },
      { path: 'moderation', element: <AdminHiddenPage /> },
      { path: 'catalog', element: <AdminCatalogPage /> },
      { path: 'toolbox', element: <AdminToolboxPage /> },
      { path: 'security', element: <AdminSecurityPage /> },
      { path: '*', element: <NotFoundContent /> },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
];
