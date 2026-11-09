import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const ADMIN = teacher({ email: 'admin@example.com', platformRoles: ['PlatformAdmin'] });
const MFA = { enabled: true, sessionVerified: true, required: true, recoveryCodesLeft: 10 };
const CATEGORIES = ['Geometry', 'Graphing', 'Simulations', 'Chemistry', 'Drawing', 'Other'];
const tool = (code: string, overrides: Record<string, unknown> = {}) => ({
  id: `id-${code}`,
  code,
  nameMk: code,
  nameEn: code,
  descriptionMk: `Опис ${code}`,
  descriptionEn: `About ${code}`,
  kind: 'External',
  category: 'Geometry',
  url: `https://www.${code}.org/`,
  builtInKey: null,
  subjectIds: [],
  isFeatured: false,
  licenseNoteMk: null,
  licenseNoteEn: null,
  ...overrides,
});

describe('teacher toolbox', () => {
  it('lists tools, filters by search and category, and opens external tools in a new tab without the opener', async () => {
    const user = userEvent.setup();
    const searches: string[] = [];
    fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path !== `/api/w/${WORKSPACE_ID}/toolbox`) return undefined;
          searches.push(call.search);
          const query = new URLSearchParams(call.search);
          const all = [tool('geogebra', { isFeatured: true, nameMk: 'GeoGebra', licenseNoteMk: 'Бесплатно', licenseNoteEn: 'Free' }), tool('desmos', { category: 'Graphing', nameMk: 'Desmos' })];
          const q = query.get('q')?.toLowerCase();
          const category = query.get('category');
          return json(200, { tools: all.filter((t) => (!q || t.nameMk.toLowerCase().includes(q)) && (!category || t.category === category)), categories: CATEGORIES });
        },
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox`);

    const list = await screen.findByRole('list', { name: 'Алатки' });
    const geogebra = within(list).getByRole('article', { name: 'GeoGebra' });
    expect(geogebra).toHaveTextContent('Издвоено');
    expect(geogebra).toHaveTextContent('Геометрија · Надворешна страница');
    expect(geogebra).toHaveTextContent('Бесплатно');
    expect(geogebra).not.toHaveTextContent('Free'); // the license note in the UI language
    const open = within(geogebra).getByRole('link', { name: /Отвори GeoGebra/ });
    expect(open).toHaveAttribute('href', 'https://www.geogebra.org/');
    expect(open).toHaveAttribute('target', '_blank');
    expect(open).toHaveAttribute('rel', 'noopener noreferrer');

    await user.type(screen.getByRole('searchbox', { name: 'Пребарај алатки' }), 'desm');
    await expect.poll(() => within(screen.getByRole('list', { name: 'Алатки' })).queryAllByRole('article').map((a) => a.getAttribute('aria-label'))).toEqual(['Desmos']);
    expect(searches.at(-1)).toContain('q=desm');

    await user.clear(screen.getByRole('searchbox', { name: 'Пребарај алатки' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Категорија' }), 'Simulations');
    expect(await screen.findByText('Нема алатки според пребарувањето.')).toBeInTheDocument();
    expect(searches.at(-1)).toContain('category=Simulations');
  });
});

describe('admin toolbox', () => {
  it('adds an external tool and edits one with the version the form was opened with', async () => {
    const user = userEvent.setup();
    const posted: unknown[] = [];
    const put: unknown[] = [];
    let gets = 0;
    const existing = { tool: tool('geogebra', { nameMk: 'GeoGebra', nameEn: 'GeoGebra' }), isActive: true, sortOrder: 10, updatedAtUtc: '2026-11-10T00:00:00Z', version: 3 };
    fakeApi({
      me: ADMIN,
      extra: [
        (call: FakeCall) => {
          if (call.path === '/api/auth/mfa') return json(200, MFA);
          // After the first load, another staff member's change makes the tool version 4.
          if (call.path === '/api/admin/toolbox' && call.method === 'GET') return json(200, [{ ...existing, version: gets++ === 0 ? 3 : 4 }]);
          if (call.path === '/api/admin/toolbox' && call.method === 'POST') {
            posted.push(call.body);
            return json(200, existing);
          }
          if (call.path === `/api/admin/toolbox/${existing.tool.id}` && call.method === 'PUT') {
            put.push(call.body);
            return json(200, existing);
          }
          return undefined;
        },
      ],
    });
    await renderRoute('/admin/toolbox');

    await user.click(await screen.findByRole('button', { name: 'Уреди GeoGebra' }));
    const edit = screen.getByRole('form', { name: 'Уреди GeoGebra' });
    await user.click(within(edit).getByRole('checkbox', { name: 'Активна' }));

    // Adding another tool refetches the list while the edit form is open.
    const form = screen.getByRole('form', { name: 'Додај алатка' });
    await user.type(within(form).getByLabelText('Код (траен)'), 'molview');
    await user.type(within(form).getByLabelText('Име (македонски)'), 'MolView');
    await user.type(within(form).getByLabelText('Име (англиски)'), 'MolView');
    await user.type(within(form).getByLabelText('Опис (македонски)'), 'Молекули');
    await user.type(within(form).getByLabelText('Опис (англиски)'), 'Molecules');
    await user.selectOptions(within(form).getByLabelText('Категорија'), 'Chemistry');
    await user.clear(within(form).getByLabelText('Официјален линк (HTTPS)'));
    await user.type(within(form).getByLabelText('Официјален линк (HTTPS)'), 'https://molview.org/');
    await user.click(within(form).getByRole('button', { name: 'Додај ја алатката' }));
    await expect.poll(() => posted.length).toBe(1);
    expect(posted[0]).toMatchObject({ code: 'molview', nameMk: 'MolView', kind: 'External', category: 'Chemistry', url: 'https://molview.org/', builtInKey: null, version: null });

    await expect.poll(() => gets).toBeGreaterThan(1);
    await user.click(within(edit).getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => put.length).toBe(1);
    expect(put[0]).toMatchObject({ code: null, isActive: false, version: 3 }); // not the refetched 4: the server answers with a conflict
  });
});
