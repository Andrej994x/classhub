import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { formatBytes, materialFileProblem } from '../../../teacher/materials.ts';
import { fakeApi, json, problem, SUBJECT_MATH, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/materials`;
const STUDENTS = `/api/w/${WORKSPACE_ID}/students`;
const GROUPS = `/api/w/${WORKSPACE_ID}/groups`;

type Assignment = { id: string; studentId: string | null; groupId: string | null; name: string; assignedAtUtc: string };

const material = (overrides: Record<string, unknown> = {}) => ({
  id: 'm1',
  title: 'Равенки',
  description: 'Вежби за дома' as string | null,
  subjectId: SUBJECT_MATH as string | null,
  fileName: 'ravenki.pdf',
  contentType: 'application/pdf',
  sizeBytes: 350 * 1024,
  archived: false,
  assignments: [] as Assignment[],
  createdAtUtc: '2026-11-09T09:00:00Z',
  updatedAtUtc: '2026-11-09T09:00:00Z',
  version: 0,
  ...overrides,
});

const studentItem = (id: string, firstName: string, lastName: string) => ({
  id,
  firstName,
  lastName,
  email: null,
  phone: null,
  isMinor: false,
  subjectIds: [],
  archived: false,
  updatedAtUtc: '2026-11-05T09:00:00Z',
});

function materialsApi() {
  const state = { materials: [material()] };
  const students = [studentItem('s1', 'Ана', 'Илиева')];
  const groups = [{ id: 'g1', name: 'Алгебра 8', subjectId: null, memberCount: 3, archived: false, updatedAtUtc: '2026-11-05T09:00:00Z' }];
  const find = (id: string) => state.materials.find((m) => m.id === id)!;
  const replace = (next: ReturnType<typeof material>) => {
    state.materials = state.materials.map((m) => (m.id === next.id ? next : m));
    return next;
  };
  const handler = (call: FakeCall) => {
    if (call.path === STUDENTS && call.method === 'GET') return json(200, { items: students, total: students.length, page: 1, pageSize: 20, activeCount: students.length });
    if (call.path === GROUPS && call.method === 'GET') return json(200, groups);
    if (call.path === BASE && call.method === 'GET') {
      const params = new URLSearchParams(call.search);
      const archived = params.get('archived') === 'true';
      const q = params.get('q') ?? '';
      const items = state.materials
        .filter((m) => m.archived === archived && (!q || m.title.includes(q)))
        .map((m) => ({ id: m.id, title: m.title, subjectId: m.subjectId, fileName: m.fileName, contentType: m.contentType, sizeBytes: m.sizeBytes, assignmentCount: m.assignments.length, archived: m.archived, updatedAtUtc: m.updatedAtUtc }));
      return json(200, { items, total: items.length, page: 1, pageSize: 20, storageUsedBytes: state.materials.reduce((sum, m) => sum + m.sizeBytes, 0) });
    }
    if (call.path === BASE && call.method === 'POST') {
      const created = material({ id: 'm2', title: 'Нов материјал', description: null, subjectId: null, fileName: 'tabela.png', contentType: 'image/png', sizeBytes: 2048 });
      state.materials.push(created);
      return json(201, created);
    }
    const match = /\/materials\/(m\d)(\/.*)?$/.exec(call.path);
    if (!match) return undefined;
    const current = find(match[1]!);
    if (!current) return problem(404, 'NOT_FOUND');
    const rest = match[2] ?? '';
    if (rest === '' && call.method === 'GET') return json(200, current);
    if (rest === '' && call.method === 'PUT') {
      if ((call.body as { version: number }).version !== current.version) return problem(409, 'CONCURRENCY_CONFLICT');
      return json(200, replace(material({ ...current, ...(call.body as object), version: current.version + 1 })));
    }
    if (rest === '/archive') return json(200, replace(material({ ...current, archived: true, version: current.version + 1 })));
    if (rest === '/delete') {
      state.materials = state.materials.filter((m) => m.id !== current.id);
      return new Response(null, { status: 204 });
    }
    if (rest === '/assignments' && call.method === 'POST') {
      const body = call.body as { studentId: string | null; groupId: string | null };
      const assignment = {
        id: `a${current.assignments.length + 1}`,
        studentId: body.studentId,
        groupId: body.groupId,
        name: body.studentId ? 'Ана Илиева' : 'Алгебра 8',
        assignedAtUtc: '2026-11-09T10:00:00Z',
      };
      return json(200, replace(material({ ...current, assignments: [...current.assignments, assignment] })));
    }
    if (rest.startsWith('/assignments/') && call.method === 'DELETE') {
      const id = rest.slice('/assignments/'.length);
      return json(200, replace(material({ ...current, assignments: current.assignments.filter((a) => a.id !== id) })));
    }
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('materials', () => {
  it('lists the materials with the storage the workspace uses', async () => {
    materialsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/materials`);

    const list = await screen.findByRole('list', { name: 'Материјали' });
    const link = within(list).getByRole('link', { name: /Равенки/ });
    expect(link).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/materials/m1`);
    expect(link).toHaveTextContent('ravenki.pdf · 350 KB · доделен на 0');
    expect(screen.getByText('Искористен простор: 350 KB')).toBeInTheDocument();
  });

  it('checks the file before uploading and opens the new material', async () => {
    const user = userEvent.setup({ applyAccept: false });
    const { api } = materialsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/materials`);

    const input = await screen.findByLabelText('Датотека');
    await user.click(screen.getByRole('button', { name: 'Постави' }));
    expect(await screen.findByText('Изберете датотека.')).toBeInTheDocument();

    await user.upload(input, new File(['MZ'], 'program.exe', { type: 'application/octet-stream' }));
    expect(await screen.findByText('Овој тип датотека не е дозволен.')).toBeInTheDocument();
    expect(api.to(BASE, 'POST')).toHaveLength(0);

    await user.upload(input, new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'Табела за час.png', { type: 'image/png' }));
    expect(screen.getByLabelText('Наслов')).toHaveValue('Табела за час'); // the file name is the first title
    await user.click(screen.getByRole('button', { name: 'Постави' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/materials/m2`);
    const upload = api.to(BASE, 'POST')[0]!;
    expect(upload.xsrf).toBe('xsrf');
    expect(upload.contentType).toMatch(/^multipart\/form-data; boundary=/);
    expect(String(upload.body)).toContain('Табела за час');
    expect(String(upload.body)).toContain('name="file"'); // the file name itself is checked end to end (jsdom loses it)
    expect(await screen.findByRole('heading', { level: 1, name: 'Нов материјал' })).toBeInTheDocument();
  });

  it('assigns to a student and a group and removes an assignment', async () => {
    const user = userEvent.setup();
    const { api } = materialsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/materials/m1`);

    expect(await screen.findByText('Сè уште не е доделен: само вие можете да го отворите.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Преземи' })).toHaveAttribute('href', `${BASE}/m1/file`);

    await user.selectOptions(await screen.findByLabelText('Додели на ученик'), 's1');
    await user.click(screen.getAllByRole('button', { name: 'Додели' })[0]!);
    const assigned = await screen.findByRole('list', { name: 'Кој може да го отвори' });
    expect(within(assigned).getByRole('link', { name: /Ана Илиева/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students/s1`);
    expect(api.to(`${BASE}/m1/assignments`, 'POST')[0]!.body).toEqual({ studentId: 's1', groupId: null });

    await user.selectOptions(screen.getByLabelText('Додели на група'), 'g1');
    await user.click(screen.getAllByRole('button', { name: 'Додели' })[1]!);
    expect(await within(assigned).findByRole('link', { name: /Алгебра 8/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/groups/g1`);
    expect(api.to(`${BASE}/m1/assignments`, 'POST')[1]!.body).toEqual({ studentId: null, groupId: 'g1' });
    // An assigned group is not offered again.
    expect(within(screen.getByLabelText('Додели на група')).queryByRole('option', { name: 'Алгебра 8' })).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Отстрани Ана Илиева' }));
    await expect.poll(() => api.to(`${BASE}/m1/assignments/a1`, 'DELETE').length).toBe(1);
    expect(await screen.findByRole('button', { name: 'Отстрани Алгебра 8' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Отстрани Ана Илиева' })).toBeNull();
  });

  it('keeps unsaved edits when another tab changed the material, and reports the conflict on save', async () => {
    const user = userEvent.setup();
    const { state } = materialsApi();
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}/materials/m1`);

    const title = await screen.findByLabelText('Наслов');
    await user.clear(title);
    await user.type(title, 'Мое уредување');
    state.materials[0] = material({ ...state.materials[0], description: 'Друго', version: 3 });
    await act(() => queryClient.invalidateQueries());

    expect(screen.getByLabelText('Наслов')).toHaveValue('Мое уредување');
    await user.click(screen.getByRole('button', { name: 'Зачувај ги промените' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Материјалот е променет во друг прозорец.');
  });

  it('archives with the current version, then deletes after confirming', async () => {
    const user = userEvent.setup();
    const { api, state } = materialsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/materials/m1`);

    await user.click(await screen.findByRole('button', { name: 'Архивирај' }));
    expect(await screen.findByRole('button', { name: 'Врати' })).toBeInTheDocument();
    expect(api.to(`${BASE}/m1/archive`)[0]!.body).toEqual({ version: 0 });
    expect(screen.getByText('Вратете го материјалот за да го доделите.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Избриши…' }));
    expect(api.to(`${BASE}/m1/delete`)).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Избриши трајно' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/materials`);
    expect(api.to(`${BASE}/m1/delete`)[0]!.body).toEqual({ version: 1 }); // the version after archiving
    expect(state.materials).toHaveLength(0);
  });

  it('shows a students own materials with download links in the portal', async () => {
    const linked = { workspaceId: WORKSPACE_ID, teacherName: 'Математика со Ана', studentId: 's1', firstName: 'Марко', lastName: null };
    fakeApi({
      me: teacher({ email: 'marko@example.test' }),
      extra: [
        (call) => {
          if (call.path === '/api/student/teachers') return json(200, [linked]);
          if (call.path === `/api/student/w/${WORKSPACE_ID}/profile`) return json(200, { ...linked, subjectIds: [] });
          if (call.path === `/api/student/w/${WORKSPACE_ID}/materials`) {
            return json(200, [
              { id: 'm1', title: 'Равенки', description: 'Вежби за дома', subjectId: null, fileName: 'ravenki.pdf', contentType: 'application/pdf', sizeBytes: 4 * 1024 * 1024 + 300 * 1024, assignedAtUtc: '2026-11-09T10:00:00Z' },
            ]);
          }
          return undefined;
        },
      ],
    });
    await renderRoute(`/student/w/${WORKSPACE_ID}`);

    const list = await screen.findByRole('list', { name: 'Материјали' });
    expect(within(list).getByText('Вежби за дома')).toBeInTheDocument();
    expect(within(list).getByText('ravenki.pdf · 4,3 MB')).toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'Преземи Равенки' })).toHaveAttribute('href', `/api/student/w/${WORKSPACE_ID}/materials/m1/file`);
  });

  it('tells a student when nothing was shared yet', async () => {
    const linked = { workspaceId: WORKSPACE_ID, teacherName: 'Математика со Ана', studentId: 's1', firstName: 'Марко', lastName: null };
    fakeApi({
      me: teacher(),
      extra: [
        (call) => {
          if (call.path === '/api/student/teachers') return json(200, [linked]);
          if (call.path === `/api/student/w/${WORKSPACE_ID}/profile`) return json(200, { ...linked, subjectIds: [] });
          return undefined;
        },
      ],
    });
    await renderRoute(`/student/w/${WORKSPACE_ID}`);
    expect(await screen.findByText('Наставникот сè уште не споделил материјали со вас.')).toBeInTheDocument();
  });
});

describe('material file checks', () => {
  it('mirrors the API allow-list and size limit', () => {
    expect(materialFileProblem(new File(['x'], 'a.PDF'))).toBeNull();
    expect(materialFileProblem(new File(['x'], 'a.docx'))).toBeNull();
    expect(materialFileProblem(new File(['x'], 'a.svg'))).toBe('type');
    expect(materialFileProblem(new File(['x'], 'pdf'))).toBe('type');
    expect(materialFileProblem(new File([], 'a.pdf'))).toBe('size');
    const big = new File(['x'], 'a.pdf');
    Object.defineProperty(big, 'size', { value: 20 * 1024 * 1024 + 1 });
    expect(materialFileProblem(big)).toBe('size');
  });

  it('formats sizes in the UI language', () => {
    expect(formatBytes(900, 'en')).toBe('900 B');
    expect(formatBytes(1536, 'en')).toBe('1.5 KB');
    expect(formatBytes(25 * 1024 * 1024, 'en')).toBe('25 MB');
  });
});
