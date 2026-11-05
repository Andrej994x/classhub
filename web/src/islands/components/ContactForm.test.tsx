import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ContactForm } from './ContactForm.tsx';
import { CONTACT_FORM_TEXT } from './contactFormText.ts';

const FORM = {
  available: true,
  formToken: 'token-1',
  offers: [{ id: 'offer-1', name: 'Пакет 10 часа' }],
  subjects: [{ id: 'subject-1', name: 'Математика' }],
  consentVersion: '2026-11-05.1',
  consentText: 'Се согласувам наставникот да ги добие моите податоци.',
  minorNotice: 'Имам најмалку 16 години, или сум родител.',
  minorAgeThreshold: 16,
  minimumFillSeconds: 0,
};

type Answer = (url: string, init?: RequestInit) => Response | Promise<Response>;

function server(answer: Answer) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input, init) => {
    calls.push({ url: String(input), init });
    return answer(String(input), init);
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

async function fillIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Име и презиме'), 'Марија Илиевска');
  await user.type(screen.getByLabelText('Е-пошта'), 'marija@example.test');
  await user.type(screen.getByLabelText(/^Порака/), 'Здраво!');
  await user.click(screen.getByRole('checkbox', { name: FORM.consentText }));
  await user.click(screen.getByRole('checkbox', { name: FORM.minorNotice }));
}

describe('contact-form island', () => {
  it('sends a lesson request with the token, consent version and an empty honeypot, without cookies', async () => {
    const user = userEvent.setup();
    const calls = server((url) => (url.endsWith('/lead-form') ? json(200, FORM) : json(202, { received: true })));
    render(<ContactForm slug="ana-math" language="mk" />);

    await user.click(await screen.findByRole('radio', { name: 'Да побарам час' }));
    await fillIn(user);
    await user.selectOptions(screen.getByLabelText('Понуда (по желба)'), 'offer-1');
    await user.selectOptions(screen.getByLabelText('Предмет (по желба)'), 'subject-1');
    await user.click(screen.getByRole('button', { name: 'Испрати' }));

    expect(await screen.findByText(/Ви благодариме!/)).toBeInTheDocument();
    const post = calls.find((c) => c.init?.method === 'POST')!;
    expect(post.url).toBe('/api/public/sites/ana-math/leads');
    expect(post.init?.credentials).toBe('omit');
    expect(JSON.parse(String(post.init?.body))).toEqual({
      type: 'LessonRequest',
      name: 'Марија Илиевска',
      email: 'marija@example.test',
      phone: null,
      message: 'Здраво!',
      offerId: 'offer-1',
      subjectId: 'subject-1',
      consent: true,
      ageConfirmed: true,
      consentVersion: '2026-11-05.1',
      formToken: 'token-1',
      website: '',
    });
  });

  it('checks the fields in the browser before sending', async () => {
    const user = userEvent.setup();
    const calls = server(() => json(200, FORM));
    render(<ContactForm slug="ana-math" language="mk" />);

    await user.type(await screen.findByLabelText('Е-пошта'), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'Испрати' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Проверете ги означените полиња.');
    expect(screen.getByText('Внесете важечка е-пошта.')).toBeInTheDocument();
    expect(screen.getByText('Потребна е согласност.')).toBeInTheDocument();
    expect(screen.getByLabelText('Име и презиме')).toHaveAttribute('aria-invalid', 'true');
    expect(calls.filter((c) => c.init?.method === 'POST')).toHaveLength(0);
  });

  it('renders nothing when the teacher cannot receive requests (D-10b)', async () => {
    const calls = server(() => json(200, { ...FORM, available: false, formToken: null }));
    const { container } = render(<ContactForm slug="ana-math" language="en" />);
    await vi.waitFor(() => expect(calls).toHaveLength(1));
    await vi.waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it('renews an expired form and explains a rate limit', async () => {
    const user = userEvent.setup();
    let posts = 0;
    let forms = 0;
    server((url) => {
      if (url.endsWith('/lead-form')) {
        forms++;
        return json(200, { ...FORM, formToken: `token-${forms}` });
      }
      posts++;
      return posts === 1 ? json(422, { code: 'FORM_EXPIRED' }) : json(429, { code: 'RATE_LIMITED' });
    });
    render(<ContactForm slug="ana-math" language="en" />);

    await user.type(await screen.findByLabelText('Full name'), 'Maria');
    await user.type(screen.getByLabelText('Email'), 'maria@example.test');
    await user.type(screen.getByLabelText('Message'), 'Hi');
    await user.click(screen.getByRole('checkbox', { name: FORM.consentText }));
    await user.click(screen.getByRole('checkbox', { name: FORM.minorNotice }));
    await user.click(screen.getByRole('button', { name: 'Send' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The form had expired');
    expect(forms).toBe(2);

    await user.click(screen.getByRole('button', { name: 'Send' }));
    expect(await screen.findByText(/The teacher received many requests/)).toBeInTheDocument();
  });

  it('shows localized field errors from the server and refreshes the offers', async () => {
    const user = userEvent.setup();
    let forms = 0;
    server((url) => {
      if (url.endsWith('/lead-form')) {
        forms++;
        return json(200, forms === 1 ? FORM : { ...FORM, offers: [] });
      }
      return json(400, { code: 'VALIDATION_FAILED', errors: { offerId: ['This offer cannot be requested.'] } });
    });
    render(<ContactForm slug="ana-math" language="mk" />);

    await user.click(await screen.findByRole('radio', { name: 'Да побарам час' }));
    await fillIn(user);
    await user.selectOptions(screen.getByLabelText('Понуда (по желба)'), 'offer-1');
    await user.click(screen.getByRole('button', { name: 'Испрати' }));

    expect(await screen.findByText('Оваа понуда веќе не е достапна. Изберете друга.')).toBeInTheDocument();
    expect(screen.queryByText('This offer cannot be requested.')).not.toBeInTheDocument();
    expect(forms).toBe(2);
    expect(screen.queryByLabelText('Понуда (по желба)')).not.toBeInTheDocument(); // no offers left to choose
  });

  it('loads the new consent text and asks again when it changed meanwhile', async () => {
    const user = userEvent.setup();
    let forms = 0;
    const calls = server((url) => {
      if (url.endsWith('/lead-form')) {
        forms++;
        return json(200, forms === 1 ? FORM : { ...FORM, consentVersion: '2026-12-01.1', consentText: 'Нов текст за согласност.', formToken: 'token-2' });
      }
      return calls.filter((c) => c.init?.method === 'POST').length === 1
        ? json(400, { code: 'VALIDATION_FAILED', errors: { consentVersion: ['The consent text has changed.'] } })
        : json(202, { received: true });
    });
    render(<ContactForm slug="ana-math" language="mk" />);

    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Испрати' }));
    expect(await screen.findByText('Текстот за согласност е ажуриран. Прочитајте го и потврдете повторно.')).toBeInTheDocument();
    const consent = screen.getByRole('checkbox', { name: 'Нов текст за согласност.' });
    expect(consent).not.toBeChecked();

    await user.click(consent);
    await user.click(screen.getByRole('checkbox', { name: FORM.minorNotice }));
    await user.click(screen.getByRole('button', { name: 'Испрати' }));
    expect(await screen.findByText(/Ви благодариме!/)).toBeInTheDocument();
    const second = JSON.parse(String(calls.filter((c) => c.init?.method === 'POST')[1]!.init?.body)) as { consentVersion: string; formToken: string };
    expect(second).toMatchObject({ consentVersion: '2026-12-01.1', formToken: 'token-2' });
  });

  it('waits out the minimum fill time of a renewed form before sending again', async () => {
    const user = userEvent.setup();
    const posts: number[] = [];
    let forms = 0;
    server((url) => {
      if (url.endsWith('/lead-form')) {
        forms++;
        return json(200, { ...FORM, minimumFillSeconds: forms === 1 ? 0 : 1 });
      }
      posts.push(Date.now());
      return posts.length === 1 ? json(422, { code: 'FORM_EXPIRED' }) : json(202, { received: true });
    });
    render(<ContactForm slug="ana-math" language="mk" />);

    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Испрати' }));
    expect(await screen.findByText(/Формуларот истече/)).toBeInTheDocument();
    const renewedAt = Date.now();
    await user.click(screen.getByRole('button', { name: 'Испрати' })); // at once
    expect(await screen.findByText(/Ви благодариме!/, undefined, { timeout: 4000 })).toBeInTheDocument();
    expect(posts[1]! - renewedAt).toBeGreaterThanOrEqual(1000);
  });

  it('has the same texts in both languages', () => {
    expect(Object.keys(CONTACT_FORM_TEXT.en).toSorted()).toEqual(Object.keys(CONTACT_FORM_TEXT.mk).toSorted());
  });
});
