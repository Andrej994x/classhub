import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { DIAGNOSTIC_TEST_TEXT, DiagnosticTest } from './DiagnosticTest.tsx';

const TEST = {
  title: 'Дропки',
  totalPoints: 5,
  questions: [
    { id: 'q1', text: 'Колку е 1/2 + 1/4?', points: 2, selectSeveral: false, options: [{ id: 'a', text: '3/4' }, { id: 'b', text: '2/6' }] },
    { id: 'q2', text: 'Кои се прости?', points: 3, selectSeveral: true, options: [{ id: 'c', text: '2' }, { id: 'd', text: '3' }, { id: 'e', text: '4' }] },
  ],
};

const FORM = {
  available: true,
  formToken: 'token-1',
  offers: [],
  subjects: [],
  consentVersion: '2026-11-05.1',
  consentText: 'Се согласувам.',
  minorNotice: 'Имам 16 години.',
  minorAgeThreshold: 16,
  minimumFillSeconds: 0,
};

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

function server(answer: (url: string, init?: RequestInit) => Response) {
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

describe('diagnostic-test island', () => {
  it('sends the chosen options without cookies, shows the score and offers a request with the result', async () => {
    const user = userEvent.setup();
    const calls = server((url, init) => {
      if (url.endsWith('/lead-form')) return json(200, FORM);
      if (url.endsWith('/attempts')) return json(200, { attemptId: 'attempt-1', points: 2, maxPoints: 5, questionsRight: 1, totalQuestions: 2 });
      if (url.endsWith('/leads')) return new Response(JSON.stringify({ received: true }), { status: 202 });
      return init?.method ? json(404, {}) : json(200, TEST);
    });
    render(<DiagnosticTest siteSlug="ana-math" testSlug="dropki" language="mk" />);

    await user.click(await screen.findByRole('radio', { name: '3/4' }));
    await user.click(screen.getByRole('checkbox', { name: '2' }));
    await user.click(screen.getByRole('checkbox', { name: '4' }));
    await user.click(screen.getByRole('checkbox', { name: '4' })); // unchecked again
    expect(screen.getByText('Изберете ги сите точни одговори.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Заврши го тестот' }));

    expect(await screen.findByTestId('test-score')).toHaveTextContent('2 од 5 бода (40%)');
    const attempt = calls.find((c) => c.url.endsWith('/attempts'))!;
    expect(attempt.url).toBe('/api/public/sites/ana-math/tests/dropki/attempts');
    expect(attempt.init?.credentials).toBe('omit');
    expect(JSON.parse(String(attempt.init?.body))).toEqual({ answers: [{ questionId: 'q1', optionIds: ['a'] }, { questionId: 'q2', optionIds: ['c'] }] });

    // The request form is about this attempt; sharing the score is opt-in.
    const form = await screen.findByRole('form', { name: 'Побарајте час според резултатот' });
    expect(screen.queryByRole('radio', { name: 'Да побарам час' })).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Име и презиме'), 'Марија');
    await user.type(screen.getByLabelText('Е-пошта'), 'marija@example.test');
    await user.click(screen.getByRole('checkbox', { name: FORM.consentText }));
    await user.click(screen.getByRole('checkbox', { name: FORM.minorNotice }));
    await user.click(screen.getByRole('checkbox', { name: /Сподели го мојот резултат/ }));
    await user.click(screen.getByRole('button', { name: 'Испрати' }));
    expect(await screen.findByText(/Ви благодариме!/)).toBeInTheDocument();
    expect(form).not.toBeInTheDocument();
    const lead = JSON.parse(String(calls.find((c) => c.url.endsWith('/leads'))!.init?.body)) as Record<string, unknown>;
    expect(lead).toMatchObject({ type: 'Diagnostic', attemptId: 'attempt-1', shareResult: true, offerId: null, name: 'Марија' });
  });

  it('says how many questions are unanswered and explains a busy test', async () => {
    const user = userEvent.setup();
    server((url) => (url.endsWith('/attempts') ? json(429, { code: 'RATE_LIMITED' }) : json(200, TEST)));
    render(<DiagnosticTest siteSlug="ana-math" testSlug="dropki" language="en" />);

    expect(await screen.findByText('You answered 0 of 2 questions. Unanswered ones earn 0 points.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Finish the test' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('This test was taken very often');
  });

  it('explains an unavailable test', async () => {
    server(() => json(404, {}));
    render(<DiagnosticTest siteSlug="ana-math" testSlug="dropki" language="mk" />);
    expect(await screen.findByText('Тестот моментално не е достапен.')).toBeInTheDocument();
  });

  it('has the same texts in both languages', () => {
    expect(Object.keys(DIAGNOSTIC_TEST_TEXT.en).toSorted()).toEqual(Object.keys(DIAGNOSTIC_TEST_TEXT.mk).toSorted());
  });
});
