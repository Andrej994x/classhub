import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { BookingWidget } from './BookingWidget.tsx';
import { BOOKING_TEXT } from './bookingText.ts';

const FORM = {
  available: true,
  formToken: 'token-1',
  offers: [],
  subjects: [],
  consentVersion: '2026-11-05.1',
  consentText: 'Се согласувам наставникот да ги добие моите податоци.',
  minorNotice: 'Имам најмалку 16 години, или сум родител.',
  minorAgeThreshold: 16,
  minimumFillSeconds: 0,
};

const PAGE = { available: true, timeZone: 'Europe/Skopje', horizonDays: 30, offers: [{ id: 'offer-1', title: 'Час по математика', durationMinutes: 60, mode: 'Online' }] };

const SLOTS = {
  timeZone: 'Europe/Skopje',
  days: [
    { date: '2030-11-12', slots: [{ startUtc: '2030-11-12T08:00:00Z', time: '09:00' }, { startUtc: '2030-11-12T08:30:00Z', time: '09:30' }] },
    { date: '2030-11-13', slots: [{ startUtc: '2030-11-13T15:00:00Z', time: '16:00' }] },
  ],
};

type Answer = (url: string, init?: RequestInit) => Response | Promise<Response>;

function server(answer: Answer) {
  const calls: { url: string; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input, init) => {
      calls.push({ url: String(input), init });
      return answer(String(input), init);
    }),
  );
  return calls;
}

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

const site = (post: Answer = () => json(202, { received: true })): Answer => (url, init) =>
  url.endsWith('/lead-form') ? json(200, FORM) : url.endsWith('/booking') ? json(200, PAGE) : url.includes('/booking/slots') ? json(200, SLOTS) : post(url, init);

async function fillIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Име и презиме'), 'Марија Илиевска');
  await user.type(screen.getByLabelText('Е-пошта'), 'marija@example.test');
  await user.click(screen.getByRole('checkbox', { name: FORM.consentText }));
  await user.click(screen.getByRole('checkbox', { name: FORM.minorNotice }));
}

describe('booking-widget island', () => {
  it('books a free time of the only offer through the protected request form, without cookies', async () => {
    const user = userEvent.setup();
    const calls = server(site());
    render(<BookingWidget slug="ana-math" language="mk" />);

    expect(await screen.findByText(/Сите времиња се во временската зона на наставникот: Europe\/Skopje\./)).toBeInTheDocument();
    const tuesday = await screen.findByRole('list', { name: /12 ноември/ });
    await user.click(within(tuesday).getByRole('button', { name: '09:30' }));
    expect(screen.getByText(/Избран термин: .*09:30 \(Europe\/Skopje\)/)).toBeInTheDocument();
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Испрати барање за термин' }));

    expect(await screen.findByText(/Барањето е испратено/)).toBeInTheDocument();
    expect(calls.some((c) => c.url.startsWith('/api/public/sites/ana-math/booking/slots?offerId=offer-1&from='))).toBe(true);
    const post = calls.find((c) => c.init?.method === 'POST')!;
    expect(post.url).toBe('/api/public/sites/ana-math/leads');
    expect(post.init?.credentials).toBe('omit');
    expect(JSON.parse(String(post.init?.body))).toMatchObject({
      type: 'LessonRequest',
      offerId: 'offer-1',
      slotStartUtc: '2030-11-12T08:30:00Z',
      consent: true,
      ageConfirmed: true,
      consentVersion: FORM.consentVersion,
      formToken: 'token-1',
      website: '',
    });
  });

  it('asks for another time when the chosen one was just taken', async () => {
    const user = userEvent.setup();
    const calls = server(site(() => json(400, { code: 'VALIDATION_FAILED', errors: { slotStartUtc: ['taken'] } })));
    render(<BookingWidget slug="ana-math" language="mk" />);

    await user.click(within(await screen.findByRole('list', { name: /12 ноември/ })).getByRole('button', { name: '09:00' }));
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Испрати барање за термин' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Терминот во меѓувреме е зафатен. Изберете друг.');
    await expect.poll(() => calls.filter((c) => c.url.includes('/booking/slots')).length).toBe(2); // the times were loaded again
    expect(await screen.findByRole('list', { name: /12 ноември/ })).toBeInTheDocument();
  });

  it('checks the fields before sending', async () => {
    const user = userEvent.setup();
    const calls = server(site());
    render(<BookingWidget slug="ana-math" language="en" />);

    await user.click(within(await screen.findByRole('list', { name: /12 November/ })).getByRole('button', { name: '09:00' }));
    await user.type(screen.getByLabelText('Email'), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'Send the booking request' }));

    expect(await screen.findByText(BOOKING_TEXT.en.checkFields)).toBeInTheDocument();
    expect(screen.getByText(BOOKING_TEXT.en.invalidEmail)).toBeInTheDocument();
    expect(calls.some((c) => c.init?.method === 'POST')).toBe(false);
  });

  it('points to the contact form when booking is off', async () => {
    server((url) => (url.endsWith('/lead-form') ? json(200, FORM) : json(200, { ...PAGE, available: false, offers: [] })));
    render(<BookingWidget slug="ana-math" language="mk" />);

    expect(await screen.findByText(BOOKING_TEXT.mk.unavailable)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: BOOKING_TEXT.mk.toContact })).toHaveAttribute('href', '/ana-math#contact');
  });

  it('keeps both languages in sync', () => {
    expect(Object.keys(BOOKING_TEXT.en).toSorted()).toEqual(Object.keys(BOOKING_TEXT.mk).toSorted());
  });
});
