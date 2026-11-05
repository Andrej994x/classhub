import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { EMPTY_PROFILE, fakeApi, json, LEVEL_SECONDARY, ONBOARDING, problem, SUBJECT_MATH, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const PROFILE_PATH = `/api/w/${WORKSPACE_ID}/profile`;

describe('onboarding checklist', () => {
  it('shows the five steps with progress; later steps are marked as coming soon', async () => {
    const api = fakeApi({ me: teacher() });
    api.state.onboarding = ONBOARDING(1);
    await renderRoute(`/app/w/${WORKSPACE_ID}`);

    expect(await screen.findByText('Завршени 1 од 5')).toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Вашите први чекори' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(5);
    expect(within(list).getByRole('link', { name: 'Изберете предмети' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/profile#teaching`);
    expect(within(list).queryByRole('link', { name: 'Пополнете го профилот' })).not.toBeInTheDocument(); // done
    expect(within(list).getAllByText('Наскоро')).toHaveLength(3);
    expect(screen.getByRole('progressbar', { name: 'Вашите први чекори' })).toHaveAttribute('value', '1');
  });
});

describe('profile page', () => {
  it('saves the profile and the changed workspace settings', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call) => (call.method === 'PUT' && call.path === PROFILE_PATH ? json(200, { ...EMPTY_PROFILE, ...(call.body as object) }) : undefined),
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    await user.type(await screen.findByLabelText('Име'), 'Ана');
    await user.type(screen.getByLabelText('Презиме'), 'Петровска');
    await user.type(screen.getByLabelText('Кратка биографија'), 'Наставничка по математика.');
    await user.click(screen.getByRole('radio', { name: 'Комбинирано (онлајн и физички)' }));
    await user.click(screen.getByRole('checkbox', { name: 'Математика' }));
    await user.click(screen.getByRole('checkbox', { name: 'Средно' }));
    await user.click(screen.getByRole('checkbox', { name: 'Македонски' }));
    await user.selectOptions(screen.getByLabelText('Земја'), 'MK');
    await user.selectOptions(screen.getByLabelText('Валута'), 'EUR');
    await user.click(screen.getByRole('button', { name: 'Зачувај профил' }));

    expect(await screen.findByText('Профилот е зачуван.')).toBeInTheDocument();
    expect(api.to(PROFILE_PATH, 'PUT')[0]).toMatchObject({
      xsrf: 'xsrf',
      body: {
        firstName: 'Ана',
        lastName: 'Петровска',
        bio: 'Наставничка по математика.',
        teachingMode: 'Hybrid',
        countryCode: 'MK',
        subjectIds: [SUBJECT_MATH],
        levelIds: [LEVEL_SECONDARY],
        languages: ['mk'],
        timeZone: 'Europe/Skopje',
        currency: 'EUR',
        version: null, // the version the form was loaded from (no profile yet)
      },
    });
    // One request, so the API can save profile and settings atomically.
    expect(api.to(`/api/w/${WORKSPACE_ID}`, 'PATCH')).toHaveLength(0);
  });

  it('explains a save that conflicts with a newer one (another tab or device)', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [(call) => (call.method === 'PUT' && call.path === PROFILE_PATH ? problem(409, 'CONCURRENCY_CONFLICT') : undefined)],
    });
    api.state.profile = { ...EMPTY_PROFILE, firstName: 'Ана', version: 3 };
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    await user.type(await screen.findByLabelText('Презиме'), 'Петровска');
    await user.click(screen.getByRole('button', { name: 'Зачувај профил' }));

    expect(await screen.findByText(/Профилот е променет на друго место/)).toBeInTheDocument();
    expect(api.to(PROFILE_PATH, 'PUT')[0]?.body).toMatchObject({ version: 3 });
  });

  it('locks the form while a save is pending, so no edit can be lost by the reset afterwards', async () => {
    const user = userEvent.setup();
    let finish!: (response: Response) => void;
    const pending = new Promise<Response>((resolve) => {
      finish = resolve;
    });
    fakeApi({ me: teacher(), extra: [(call) => (call.method === 'PUT' && call.path === PROFILE_PATH ? pending : undefined)] });
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    await user.type(await screen.findByLabelText('Име'), 'Ана');
    await user.click(screen.getByRole('button', { name: 'Зачувај профил' }));

    expect(await screen.findByRole('button', { name: 'Се вчитува…' })).toBeDisabled();
    expect(screen.getByLabelText('Презиме')).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Математика' })).toBeDisabled();

    finish(json(200, { ...EMPTY_PROFILE, firstName: 'Ана', version: 1 }));
    expect(await screen.findByText('Профилот е зачуван.')).toBeInTheDocument();
    expect(screen.getByLabelText('Презиме')).toBeEnabled();
  });

  it('does not touch the workspace settings when only the profile changed', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [(call) => (call.method === 'PUT' && call.path === PROFILE_PATH ? json(200, { ...EMPTY_PROFILE, ...(call.body as object) }) : undefined)],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    await user.type(await screen.findByLabelText('Име'), 'Ана');
    await user.click(screen.getByRole('button', { name: 'Зачувај профил' }));

    expect(await screen.findByText('Профилот е зачуван.')).toBeInTheDocument();
    expect(api.to(PROFILE_PATH, 'PUT')[0]?.body).toMatchObject({ timeZone: null, currency: null });
  });

  it('rejects other file types in the browser and uploads a photo as multipart with the antiforgery header', async () => {
    const user = userEvent.setup({ applyAccept: false });
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call) =>
          call.method === 'PUT' && call.path === `${PROFILE_PATH}/photo`
            ? json(200, { ...EMPTY_PROFILE, hasPhoto: true, photoVersion: 'abc123' })
            : undefined,
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);
    const input = await screen.findByLabelText('Прикачи фотографија');

    await user.upload(input, new File(['<svg/>'], 'x.svg', { type: 'image/svg+xml' }));
    expect(await screen.findByText('Изберете JPEG, PNG или WebP слика.')).toBeInTheDocument();
    expect(api.to(`${PROFILE_PATH}/photo`)).toHaveLength(0);

    await user.upload(input, new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'me.png', { type: 'image/png' }));

    expect(await screen.findByText('Фотографијата е зачувана.')).toBeInTheDocument();
    const upload = api.to(`${PROFILE_PATH}/photo`, 'PUT')[0];
    expect(upload?.xsrf).toBe('xsrf');
    expect(upload?.contentType).toMatch(/^multipart\/form-data; boundary=/);
    expect(screen.getByRole('img', { name: 'Вашата профилна фотографија' })).toHaveAttribute('src', `${PROFILE_PATH}/photo?v=abc123`);
  });

  it('shows the translated API message when the photo is refused', async () => {
    const user = userEvent.setup();
    fakeApi({ me: teacher(), extra: [(call) => (call.path === `${PROFILE_PATH}/photo` ? problem(400, 'IMAGE_INVALID') : undefined)] });
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    await user.upload(await screen.findByLabelText('Прикачи фотографија'), new File(['x'], 'tiny.png', { type: 'image/png' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Оваа слика не може да се користи');
  });

  it('shows "not found" for another teacher\'s workspace', async () => {
    fakeApi({ me: teacher() });
    await renderRoute('/app/w/99999999-9999-4999-8999-999999999999/profile');
    expect(await screen.findByRole('heading', { level: 1, name: 'Страницата не е пронајдена' })).toBeInTheDocument();
  });

  it('explains instead of silently refusing to save when too many languages are chosen', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    const languages = Array.from({ length: 11 }, (_, i) => ({ code: `l${i}`, nameMk: `Јазик ${i}`, nameEn: `Language ${i}` }));
    api.state.catalog = { ...api.state.catalog, languages };
    await renderRoute(`/app/w/${WORKSPACE_ID}/profile`);

    for (const language of languages) {
      await user.click(await screen.findByRole('checkbox', { name: language.nameMk }));
    }
    await user.click(screen.getByRole('button', { name: 'Зачувај профил' }));

    expect(await screen.findByText('Изберете најмногу 10 јазици на кои предавате.')).toBeInTheDocument();
    expect(api.to(PROFILE_PATH, 'PUT')).toHaveLength(0);
  });
});
