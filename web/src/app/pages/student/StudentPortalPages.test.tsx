import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, SUBJECT_MATH, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const OTHER_WORKSPACE = '55555555-0000-4000-8000-000000000001';
const preview = (overrides: Record<string, unknown> = {}) => ({
  teacherName: 'Математика со Ана',
  studentFirstName: 'Марко',
  email: 'marko@example.test',
  isMinor: false,
  expiresAtUtc: '2026-11-13T09:00:00Z',
  ...overrides,
});
const linked = { workspaceId: WORKSPACE_ID, teacherName: 'Математика со Ана', studentId: 's1', firstName: 'Марко', lastName: 'Стојанов' };

function portalApi({ signedIn = false, invitation = preview() as ReturnType<typeof preview> | null, teachers = [linked] } = {}) {
  const handler = (call: FakeCall) => {
    if (call.path === '/api/student-invitations/preview') return invitation ? json(200, invitation) : problem(400, 'INVALID_TOKEN');
    if (call.path === '/api/student-invitations/accept') return json(200, linked);
    if (call.path === '/api/student-invitations/accept-with-account') {
      if ((call.body as { password: string }).password === 'Taken-Account-123') return problem(409, 'EMAIL_REGISTERED');
      api.state.me = teacher({ email: 'marko@example.test' });
      return json(200, linked);
    }
    if (call.path === '/api/student/teachers' && api.state.me) return json(200, teachers);
    if (call.path === `/api/student/w/${WORKSPACE_ID}/profile`) return json(200, { ...linked, subjectIds: [SUBJECT_MATH] });
    if (call.path === `/api/student/w/${OTHER_WORKSPACE}/profile`) return problem(404, 'NOT_FOUND');
    return undefined;
  };
  const api = fakeApi({ me: signedIn ? teacher() : null, extra: [handler] });
  return api;
}

describe('student invitation', () => {
  it('creates an account for the invited email and opens the portal', async () => {
    const user = userEvent.setup();
    const api = portalApi();
    window.history.replaceState(null, '', '/student/invitation#token=tok-1');
    const { router } = await renderRoute('/student/invitation');

    expect(await screen.findByText('Математика со Ана го/ја поканува Марко на сопствена ClassHub сметка.')).toBeInTheDocument();
    expect(window.location.hash).toBe(''); // the token left the address bar
    expect(screen.getByLabelText('Е-пошта')).toHaveValue('marko@example.test');
    await user.type(screen.getByLabelText('Лозинка'), 'Correct-Horse-7');
    await user.type(screen.getByLabelText('Повторете ја лозинката'), 'Correct-Horse-7');
    await user.click(screen.getByRole('button', { name: 'Создај сметка и прифати' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/student/w/${WORKSPACE_ID}`);
    expect(api.to('/api/student-invitations/accept-with-account')[0]).toMatchObject({
      body: { token: 'tok-1', password: 'Correct-Horse-7', locale: 'mk', guardianConsent: false },
      xsrf: 'xsrf',
    });
    expect(await screen.findByRole('heading', { name: 'Математика со Ана' })).toBeInTheDocument();
    expect(window.sessionStorage.length).toBe(0); // the pending token is gone
  });

  it('asks for the guardian agreement for a minor', async () => {
    const user = userEvent.setup();
    const api = portalApi({ invitation: preview({ isMinor: true }) });
    window.history.replaceState(null, '', '/student/invitation#token=tok-2');
    await renderRoute('/student/invitation');

    await user.type(await screen.findByLabelText('Лозинка'), 'Correct-Horse-7');
    await user.type(screen.getByLabelText('Повторете ја лозинката'), 'Correct-Horse-7');
    await user.click(screen.getByRole('button', { name: 'Создај сметка и прифати' }));
    expect(await screen.findByText('Потврдете дека родител или старател се согласи.')).toBeInTheDocument();
    expect(api.to('/api/student-invitations/accept-with-account')).toHaveLength(0);

    await user.click(screen.getByLabelText('Јас сум родител или старател, или родител или старател се согласи.'));
    await user.click(screen.getByRole('button', { name: 'Создај сметка и прифати' }));
    await expect.poll(() => api.to('/api/student-invitations/accept-with-account').length).toBe(1);
    expect(api.to('/api/student-invitations/accept-with-account')[0]!.body).toMatchObject({ guardianConsent: true });
  });

  it('sends an existing account to sign in and keeps the invitation for after', async () => {
    const user = userEvent.setup();
    portalApi();
    window.history.replaceState(null, '', '/student/invitation#token=tok-3');
    await renderRoute('/student/invitation');

    await user.type(await screen.findByLabelText('Лозинка'), 'Taken-Account-123');
    await user.type(screen.getByLabelText('Повторете ја лозинката'), 'Taken-Account-123');
    await user.click(screen.getByRole('button', { name: 'Создај сметка и прифати' }));

    expect(await screen.findByText(/Веќе постои сметка со оваа е-пошта\. Најавете се, па/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Најава' })).toHaveAttribute('href', '/login?returnTo=%2Fstudent%2Finvitation');
    expect(window.sessionStorage.getItem('classhub.studentInvitation')).toBe('tok-3');
  });

  it('links the signed-in account with the token kept from before signing in', async () => {
    const user = userEvent.setup();
    const api = portalApi({ signedIn: true });
    window.sessionStorage.setItem('classhub.studentInvitation', 'tok-4');
    const { router } = await renderRoute('/student/invitation');

    expect(await screen.findByText('Најавени сте како ana@example.com. Со прифаќање се поврзува оваа сметка.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Прифати ја поканата' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/student/w/${WORKSPACE_ID}`);
    expect(api.to('/api/student-invitations/accept')[0]!.body).toEqual({ token: 'tok-4', guardianConsent: false });
  });

  it('explains an invalid or expired link', async () => {
    portalApi({ invitation: null });
    window.history.replaceState(null, '', '/student/invitation#token=old');
    await renderRoute('/student/invitation');

    expect(await screen.findByText('Поканата не е важечка или е истечена. Побарајте нова од наставникот.')).toBeInTheDocument();
  });
});

describe('student portal', () => {
  it('lists several teachers and shows the own record with one of them', async () => {
    const user = userEvent.setup();
    portalApi({ signedIn: true, teachers: [linked, { ...linked, workspaceId: OTHER_WORKSPACE, teacherName: 'Англиски со Мила' }] });
    await renderRoute('/student');

    const list = await screen.findByRole('list', { name: 'Ваши наставници' });
    await user.click(within(list).getByRole('link', { name: /Математика со Ана/ }));
    expect(await screen.findByRole('heading', { name: 'Математика со Ана' })).toBeInTheDocument();
    expect(screen.getByText('Вие сте Марко Стојанов')).toBeInTheDocument();
    expect(await screen.findByText('Математика')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Сите наставници' })).toHaveAttribute('href', '/student');
  });

  it('opens the only teacher directly, and another workspace is not found', async () => {
    portalApi({ signedIn: true });
    const { router } = await renderRoute('/student');
    await expect.poll(() => router.state.location.pathname).toBe(`/student/w/${WORKSPACE_ID}`);

    await router.navigate(`/student/w/${OTHER_WORKSPACE}`);
    expect(await screen.findByText('Страницата не е пронајдена')).toBeInTheDocument();
  });

  it('a signed-in account without a workspace lands in the student portal', async () => {
    fakeApi({
      me: teacher(),
      extra: [
        (call) => (call.path === '/api/me/workspaces' ? json(200, []) : undefined),
        (call) => (call.path === '/api/student/teachers' ? json(200, [linked]) : undefined),
        (call) => (call.path === `/api/student/w/${WORKSPACE_ID}/profile` ? json(200, { ...linked, subjectIds: [] }) : undefined),
      ],
    });
    const { router } = await renderRoute('/app');

    await expect.poll(() => router.state.location.pathname).toBe(`/student/w/${WORKSPACE_ID}`);
  });
});

describe('student settings', () => {
  it('switches the optional notifications on and off', async () => {
    const user = userEvent.setup();
    const saved = { lessonReminders: true, homeworkEmails: true };
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call) => {
          if (call.path !== '/api/me/notification-preferences') return undefined;
          if (call.method === 'PUT') {
            const body = call.body as { lessonReminders: boolean | null; homeworkEmails: boolean | null };
            saved.lessonReminders = body.lessonReminders ?? saved.lessonReminders;
            saved.homeworkEmails = body.homeworkEmails ?? saved.homeworkEmails;
          }
          return json(200, saved);
        },
      ],
    });
    await renderRoute('/student/settings');

    const reminders = await screen.findByRole('checkbox', { name: 'Потсетници еден ден пред секој час' });
    expect(reminders).toBeChecked();
    expect(screen.getByText(/Промените на вашите часови секогаш стигнуваат по е-пошта/)).toBeInTheDocument();
    await user.click(reminders);

    await expect.poll(() => api.to('/api/me/notification-preferences', 'PUT').length).toBe(1);
    expect(api.to('/api/me/notification-preferences', 'PUT')[0]).toMatchObject({ body: { lessonReminders: false, homeworkEmails: null }, xsrf: 'xsrf' });
    await expect.poll(() => (screen.getByRole('checkbox', { name: 'Потсетници еден ден пред секој час' }) as HTMLInputElement).checked).toBe(false);
    expect(screen.getByRole('checkbox', { name: 'Пораки за нови домашни задачи' })).toBeChecked();
  });
});
