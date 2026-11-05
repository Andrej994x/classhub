import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, SECTION_TYPES, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const PAGE = `/app/w/${WORKSPACE_ID}/site`;
const API = `/api/w/${WORKSPACE_ID}/site`;

type SavedBody = { slug: string; template: string; palette: string; language: string; version: number | null; draft: { sections: { type: string; visible: boolean; items: unknown[] }[] } };

describe('site editor', () => {
  it('starts from the suggestions: address, all sections, preview only after saving', async () => {
    fakeApi({ me: teacher() });
    await renderRoute(PAGE);

    expect(await screen.findByLabelText('Веб-адреса')).toHaveValue('ana-petrovska');
    expect(screen.getByText('classhub.app/', { exact: false })).toHaveTextContent('classhub.app/ana-petrovska');
    const sections = within(screen.getByRole('list', { name: 'Делови' })).getAllByRole('listitem');
    expect(sections).toHaveLength(8);
    expect(screen.getByRole('button', { name: 'Преглед' })).toBeDisabled();
    expect(screen.getByText('Зачувајте за да ги видите последните промени.')).toBeInTheDocument();
  });

  it('saves the draft with settings, section order, visibility and items, then previews it in a sandboxed frame', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    await renderRoute(PAGE);

    await user.click(await screen.findByRole('radio', { name: /Модерен/ }));
    await user.click(screen.getByRole('radio', { name: 'Шума' }));
    await user.selectOptions(screen.getByLabelText('Јазик на страницата'), 'en');
    await user.click(screen.getByRole('checkbox', { name: 'Прикажи го делот „Често поставувани прашања“' }));
    await user.click(screen.getByRole('button', { name: 'Додај прашање' }));
    await user.type(screen.getByLabelText('Прашање'), 'Дали предавате онлајн?');
    await user.type(screen.getByLabelText('Одговор'), 'Да.');
    await user.click(screen.getByRole('button', { name: 'Помести „За мене“ нагоре' }));
    await user.click(screen.getByRole('button', { name: 'Зачувај скица' }));

    expect(await screen.findByText('Скицата е зачувана.')).toBeInTheDocument();
    const body = api.to(API, 'PUT')[0]?.body as SavedBody;
    expect(body).toMatchObject({ slug: 'ana-petrovska', template: 'modern', palette: 'forest', language: 'en', version: null });
    expect(body.draft.sections.map((s) => s.type)).toEqual(['about', 'hero', ...SECTION_TYPES.slice(2)]);
    expect(body.draft.sections.find((s) => s.type === 'faq')).toMatchObject({ visible: true, items: [{ title: 'Дали предавате онлајн?', subtitle: null, text: 'Да.' }] });

    await user.click(screen.getByRole('button', { name: 'Преглед' }));
    const frame = await screen.findByTitle('Преглед на скицата');
    expect(frame).toHaveAttribute('src', '/preview/token-123');
    expect(frame).toHaveAttribute('sandbox', 'allow-same-origin');
  });

  it('checks the address while typing and explains a taken one', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.slugTaken = ['ana-math'];
    await renderRoute(PAGE);

    const slug = await screen.findByLabelText('Веб-адреса');
    await user.clear(slug);
    await user.type(slug, 'ana-math');

    // The suggested address was checked first ("available"); then the typed one, after the debounce.
    await expect.poll(() => screen.queryByTestId('slug-availability')?.textContent ?? '', { timeout: 3000 }).toContain('зафатена');
    await user.click(screen.getByRole('button', { name: 'Зачувај скица' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Оваа адреса е зафатена');
  });

  it('validates the address in the browser', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    await renderRoute(PAGE);

    const slug = await screen.findByLabelText('Веб-адреса');
    await user.clear(slug);
    await user.type(slug, 'Ana--Math');
    await user.click(screen.getByRole('button', { name: 'Зачувај скица' }));

    expect(await screen.findByText(/Користете мали латинични букви, бројки и единечни цртички/)).toBeInTheDocument();
    expect(api.to(API, 'PUT')).toHaveLength(0);
  });

  it('limits item fields while typing and never fails silently on invalid input', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    await renderRoute(PAGE);

    await user.click(await screen.findByRole('button', { name: 'Додај искуство' }));
    expect(screen.getByLabelText('Улога или место')).toHaveAttribute('maxLength', '200');
    expect(screen.getByLabelText('Период')).toHaveAttribute('maxLength', '80');

    const slug = screen.getByLabelText('Веб-адреса');
    await user.clear(slug);
    await user.type(slug, 'x');
    await user.click(screen.getByRole('button', { name: 'Зачувај скица' }));

    expect(await screen.findByText('Некои полиња треба да се поправат; погледнете ги означените пораки подолу.')).toBeInTheDocument();
    expect(api.to(API, 'PUT')).toHaveLength(0);
  });

  it('explains a save that conflicts with a newer one', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.site = { ...api.state.site, exists: true, version: 3 };
    await renderRoute(PAGE);
    await screen.findByLabelText('Веб-адреса');
    api.state.site = { ...api.state.site, version: 4 }; // saved meanwhile elsewhere

    await user.click(screen.getByRole('radio', { name: /Минималистички/ }));
    await user.click(screen.getByRole('button', { name: 'Зачувај скица' }));

    expect(await screen.findByText(/Страницата е променета на друго место/)).toBeInTheDocument();
    expect((api.to(API, 'PUT')[0]!.body as SavedBody).version).toBe(3);
  });
});
