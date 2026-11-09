import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/homework`;
const PORTAL = `/api/student/w/${WORKSPACE_ID}/homework`;
const STUDENTS = `/api/w/${WORKSPACE_ID}/students`;
const GROUPS = `/api/w/${WORKSPACE_ID}/groups`;

type Assignment = {
  id: string;
  studentId: string;
  studentName: string;
  studentArchived: boolean;
  status: string;
  assignedAtUtc: string;
  submittedAtUtc: string | null;
  late: boolean;
  answerText: string | null;
  file: { fileName: string; contentType: string; sizeBytes: number } | null;
  reviewedAtUtc: string | null;
  teacherComment: string | null;
  version: number;
};

const assignment = (overrides: Partial<Assignment> = {}): Assignment => ({
  id: 'a1',
  studentId: 's1',
  studentName: 'Ана Илиева',
  studentArchived: false,
  status: 'Assigned',
  assignedAtUtc: '2026-11-09T09:00:00Z',
  submittedAtUtc: null,
  late: false,
  answerText: null,
  file: null,
  reviewedAtUtc: null,
  teacherComment: null,
  version: 0,
  ...overrides,
});

const homework = (overrides: Record<string, unknown> = {}) => ({
  id: 'h1',
  title: 'Равенки',
  description: 'Задачи 1–5' as string | null,
  dueDate: '2026-11-20' as string | null,
  dueAtUtc: '2026-11-20T23:00:00Z' as string | null,
  lesson: null as { id: string; title: string; startUtc: string; timeZone: string } | null,
  archived: false,
  assignments: [] as Assignment[],
  createdAtUtc: '2026-11-09T09:00:00Z',
  updatedAtUtc: '2026-11-09T09:00:00Z',
  version: 0,
  ...overrides,
});

const studentItem = { id: 's1', firstName: 'Ана', lastName: 'Илиева', email: null, phone: null, isMinor: false, subjectIds: [], archived: false, updatedAtUtc: '2026-11-05T09:00:00Z' };

function homeworkApi() {
  const state = { homework: [homework()] };
  const find = (id: string) => state.homework.find((h) => h.id === id)!;
  const replace = (next: ReturnType<typeof homework>) => {
    state.homework = state.homework.map((h) => (h.id === next.id ? next : h));
    return next;
  };
  const handler = (call: FakeCall) => {
    if (call.path === STUDENTS && call.method === 'GET') return json(200, { items: [studentItem], total: 1, page: 1, pageSize: 20, activeCount: 1 });
    if (call.path === GROUPS && call.method === 'GET') return json(200, [{ id: 'g1', name: 'Алгебра 8', subjectId: null, memberCount: 2, archived: false, updatedAtUtc: '2026-11-05T09:00:00Z' }]);
    if (call.path === `/api/w/${WORKSPACE_ID}/lessons/l1`) {
      return json(200, { id: 'l1', title: 'Линеарни равенки', startUtc: '2026-11-10T16:00:00Z', timeZone: 'Europe/Skopje' });
    }
    if (call.path === BASE && call.method === 'GET') {
      const params = new URLSearchParams(call.search);
      const archived = params.get('archived') === 'true';
      const items = state.homework
        .filter((h) => h.archived === archived)
        .map((h) => ({
          id: h.id,
          title: h.title,
          dueDate: h.dueDate,
          lessonId: h.lesson?.id ?? null,
          assigned: h.assignments.length,
          submitted: h.assignments.filter((a) => a.status === 'Submitted').length,
          reviewed: h.assignments.filter((a) => a.status === 'Reviewed').length,
          archived: h.archived,
          updatedAtUtc: h.updatedAtUtc,
        }));
      return json(200, { items, total: items.length, page: 1, pageSize: 20 });
    }
    if (call.path === BASE && call.method === 'POST') {
      const body = call.body as { title: string; lessonId: string | null };
      const created = homework({
        ...(call.body as object),
        id: 'h2',
        lesson: body.lessonId ? { id: body.lessonId, title: 'Линеарни равенки', startUtc: '2026-11-10T16:00:00Z', timeZone: 'Europe/Skopje' } : null,
      });
      state.homework.push(created);
      return json(201, created);
    }
    const match = /\/homework\/(h\d)(\/.*)?$/.exec(call.path);
    if (!match || call.path.startsWith('/api/student')) return undefined;
    const current = find(match[1]!);
    const rest = match[2] ?? '';
    if (rest === '' && call.method === 'GET') return json(200, current);
    if (rest === '/assignments' && call.method === 'POST') {
      const body = call.body as { studentIds: string[] | null; groupId: string | null };
      const added = body.groupId
        ? [assignment({ id: 'a2', studentId: 's2', studentName: 'Борис Трајков' }), assignment({ id: 'a3', studentId: 's3', studentName: 'Мила Петрова' })]
        : [assignment()];
      return json(200, replace(homework({ ...current, assignments: [...current.assignments, ...added] })));
    }
    const review = /^\/assignments\/(a\d)\/review$/.exec(rest);
    if (review) {
      const body = call.body as { comment: string | null; version: number };
      const target = current.assignments.find((a) => a.id === review[1])!;
      if (body.version !== target.version) return problem(409, 'CONCURRENCY_CONFLICT');
      const reviewed = { ...target, status: 'Reviewed', teacherComment: body.comment, reviewedAtUtc: '2026-11-11T09:00:00Z', version: target.version + 1 };
      return json(200, replace(homework({ ...current, assignments: current.assignments.map((a) => (a.id === target.id ? reviewed : a)) })));
    }
    if (rest.startsWith('/assignments/') && call.method === 'DELETE') {
      const id = rest.slice('/assignments/'.length);
      return json(200, replace(homework({ ...current, assignments: current.assignments.filter((a) => a.id !== id) })));
    }
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('homework (teacher)', () => {
  it('lists homework with the due date and the counts per status', async () => {
    const { state } = homeworkApi();
    state.homework = [homework({ assignments: [assignment(), assignment({ id: 'a2', studentId: 's2', status: 'Submitted' })] })];
    await renderRoute(`/app/w/${WORKSPACE_ID}/homework`);

    const list = await screen.findByRole('list', { name: 'Домашни задачи' });
    const link = within(list).getByRole('link', { name: /Равенки/ });
    expect(link).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/homework/h1`);
    expect(link).toHaveTextContent('доделена: 2 · за преглед: 1 · прегледана: 0');
    expect(link).toHaveTextContent('Рок:');
  });

  it('creates homework for a lesson and opens it', async () => {
    const user = userEvent.setup();
    const { api } = homeworkApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/homework/new?lessonId=l1`);

    expect(await screen.findByRole('link', { name: /Линеарни равенки/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/lessons/l1`);
    await user.click(screen.getByRole('button', { name: 'Создај домашна задача' }));
    expect(await screen.findByText('Внесете наслов.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Наслов'), 'Вежби');
    await user.type(screen.getByLabelText('Рок (по избор)'), '2026-11-20');
    await user.click(screen.getByRole('button', { name: 'Создај домашна задача' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/homework/h2`);
    expect(api.to(BASE, 'POST')[0]!.body).toEqual({ title: 'Вежби', description: null, dueDate: '2026-11-20', lessonId: 'l1', version: null });
    expect(await screen.findByRole('heading', { level: 1, name: 'Вежби' })).toBeInTheDocument();
  });

  it('assigns students and groups, removes an unanswered assignment', async () => {
    const user = userEvent.setup();
    const { api } = homeworkApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/homework/h1`);

    expect(await screen.findByText('Сè уште не е доделена.')).toBeInTheDocument();
    await user.selectOptions(await screen.findByLabelText('Додели на ученик'), 's1');
    await user.click(screen.getAllByRole('button', { name: 'Додели' })[0]!);
    const students = await screen.findByRole('list', { name: 'Ученици' });
    expect(within(students).getByRole('link', { name: 'Ана Илиева' })).toBeInTheDocument();
    expect(within(students).getByText('Доделена')).toBeInTheDocument();
    expect(api.to(`${BASE}/h1/assignments`, 'POST')[0]!.body).toEqual({ studentIds: ['s1'], groupId: null });

    await user.selectOptions(screen.getByLabelText('Додели на група'), 'g1');
    await user.click(screen.getAllByRole('button', { name: 'Додели' })[1]!);
    expect(await within(students).findByRole('link', { name: 'Мила Петрова' })).toBeInTheDocument();
    expect(api.to(`${BASE}/h1/assignments`, 'POST')[1]!.body).toEqual({ studentIds: null, groupId: 'g1' });

    await user.click(screen.getByRole('button', { name: 'Отстрани Ана Илиева' }));
    await expect.poll(() => api.to(`${BASE}/h1/assignments/a1`, 'DELETE').length).toBe(1);
  });

  it('shows an answer with its file and reviews it with the version the teacher saw', async () => {
    const user = userEvent.setup();
    const { api, state } = homeworkApi();
    state.homework = [
      homework({
        assignments: [
          assignment({ status: 'Submitted', late: true, submittedAtUtc: '2026-11-21T08:00:00Z', answerText: 'x = 4', file: { fileName: 'reshenie.pdf', contentType: 'application/pdf', sizeBytes: 2048 }, version: 3 }),
        ],
      }),
    ];
    await renderRoute(`/app/w/${WORKSPACE_ID}/homework/h1`);

    const students = await screen.findByRole('list', { name: 'Ученици' });
    expect(within(students).getByText('x = 4')).toBeInTheDocument();
    expect(within(students).getByText('Задоцнета')).toBeInTheDocument();
    expect(within(students).getByRole('link', { name: 'Преземи го одговорот од Ана Илиева' })).toHaveAttribute('href', `${BASE}/h1/assignments/a1/file`);
    // Answered: no longer removable.
    expect(screen.queryByRole('button', { name: 'Отстрани Ана Илиева' })).toBeNull();

    await user.type(screen.getByLabelText('Коментар за Ана Илиева (по избор)'), 'Точно!');
    await user.click(screen.getByRole('button', { name: 'Означи како прегледана' }));

    expect(await within(students).findByText('Прегледана')).toBeInTheDocument();
    expect(api.to(`${BASE}/h1/assignments/a1/review`)[0]!.body).toEqual({ comment: 'Точно!', version: 3 });
    expect(screen.getByLabelText('Коментар за Ана Илиева (по избор)')).toHaveValue('Точно!');
    expect(screen.getByRole('button', { name: 'Ажурирај го прегледот' })).toBeInTheDocument();
  });

  it('tells the teacher when a new answer arrived before the review', async () => {
    const user = userEvent.setup();
    const { state } = homeworkApi();
    state.homework = [homework({ assignments: [assignment({ status: 'Submitted', submittedAtUtc: '2026-11-10T08:00:00Z', answerText: 'Прв обид', version: 1 })] })];
    await renderRoute(`/app/w/${WORKSPACE_ID}/homework/h1`);

    await screen.findByText('Прв обид');
    state.homework[0]!.assignments[0]!.version = 2; // the student resubmitted
    await user.click(screen.getByRole('button', { name: 'Означи како прегледана' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Ученикот во меѓувреме предал нов одговор.');
  });
});

describe('homework (student portal)', () => {
  const linked = { workspaceId: WORKSPACE_ID, teacherName: 'Математика со Ана', studentId: 's1', firstName: 'Марко', lastName: null };
  const mine = (overrides: Record<string, unknown> = {}) => ({
    id: 'a1',
    title: 'Равенки',
    description: 'Задачи 1–5',
    dueDate: '2026-11-20',
    status: 'Assigned',
    assignedAtUtc: '2026-11-09T09:00:00Z',
    submittedAtUtc: null as string | null,
    late: false,
    answerText: null as string | null,
    file: null as { fileName: string; contentType: string; sizeBytes: number } | null,
    reviewedAtUtc: null as string | null,
    teacherComment: null as string | null,
    version: 0,
    ...overrides,
  });

  function portalApi(initial = mine(), submitAnswer?: (call: FakeCall) => Response) {
    const state = { homework: initial };
    const api = fakeApi({
      me: teacher({ email: 'marko@example.test' }),
      extra: [
        (call) => {
          if (call.path === '/api/student/teachers') return json(200, [linked]);
          if (call.path === `/api/student/w/${WORKSPACE_ID}/profile`) return json(200, { ...linked, subjectIds: [] });
          if (call.path === PORTAL) return json(200, [state.homework]);
          if (call.path === `${PORTAL}/a1`) return json(200, state.homework);
          if (call.path === `${PORTAL}/a1/submission`) {
            if (submitAnswer) return submitAnswer(call);
            state.homework = mine({ ...state.homework, status: 'Submitted', submittedAtUtc: '2026-11-10T08:00:00Z', answerText: 'x = 4', version: state.homework.version + 1 });
            return json(200, state.homework);
          }
          return undefined;
        },
      ],
    });
    return { api, state };
  }

  it('lists the homework on the teacher page and opens it', async () => {
    portalApi();
    await renderRoute(`/student/w/${WORKSPACE_ID}`);

    const list = await screen.findByRole('list', { name: 'Домашни задачи' });
    expect(within(list).getByRole('link', { name: /Равенки/ })).toHaveAttribute('href', `/student/w/${WORKSPACE_ID}/homework/a1`);
    expect(within(list).getByText('Доделена')).toBeInTheDocument();
  });

  it('hands in an answer with a file as multipart, then can hand in again', async () => {
    const user = userEvent.setup();
    const { api } = portalApi();
    await renderRoute(`/student/w/${WORKSPACE_ID}/homework/a1`);

    expect(await screen.findByText('Задачи 1–5')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Предај' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Напишете одговор или прикачете датотека.');
    expect(api.to(`${PORTAL}/a1/submission`)).toHaveLength(0);

    await user.type(screen.getByLabelText('Одговор'), 'x = 4');
    await user.upload(screen.getByLabelText('Прикачи датотека (по избор)'), new File(['%PDF-1.4'], 'reshenie.pdf', { type: 'application/pdf' }));
    await user.click(screen.getByRole('button', { name: 'Предај' }));

    expect(await screen.findByRole('button', { name: 'Предај повторно' })).toBeInTheDocument();
    const sent = api.to(`${PORTAL}/a1/submission`)[0]!;
    expect(sent.xsrf).toBe('xsrf');
    expect(sent.contentType).toMatch(/^multipart\/form-data; boundary=/);
    expect(String(sent.body)).toContain('x = 4');
    expect(String(sent.body)).toContain('name="file"');
    expect(screen.getByText('Предадена')).toBeInTheDocument();
  });

  it('explains a full teacher storage in the students words', async () => {
    const user = userEvent.setup();
    portalApi(mine(), () => problem(422, 'PLAN_LIMIT_REACHED'));
    await renderRoute(`/student/w/${WORKSPACE_ID}/homework/a1`);

    await user.upload(await screen.findByLabelText('Прикачи датотека (по избор)'), new File(['%PDF-1.4'], 'reshenie.pdf', { type: 'application/pdf' }));
    await user.click(screen.getByRole('button', { name: 'Предај' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Просторот на наставникот е полн');
  });

  it('shows the review and no answer form once reviewed', async () => {
    portalApi(
      mine({
        status: 'Reviewed',
        submittedAtUtc: '2026-11-10T08:00:00Z',
        reviewedAtUtc: '2026-11-11T08:00:00Z',
        answerText: 'x = 4',
        file: { fileName: 'reshenie.pdf', contentType: 'application/pdf', sizeBytes: 100 },
        teacherComment: 'Браво!',
      }),
    );
    await renderRoute(`/student/w/${WORKSPACE_ID}/homework/a1`);

    expect(await screen.findByText('Браво!')).toBeInTheDocument();
    expect(screen.getByText('Прегледана: одговорот повеќе не може да се менува.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Одговор')).toBeNull();
    expect(screen.getByRole('link', { name: 'Преземи ја вашата датотека reshenie.pdf' })).toHaveAttribute('href', `${PORTAL}/a1/file`);
  });
});
