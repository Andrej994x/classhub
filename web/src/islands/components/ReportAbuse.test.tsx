import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ReportAbuse } from './ReportAbuse.tsx';
import { REPORT_TEXT } from './reportAbuseText.ts';

const keysOf = (value: object): string[] =>
  Object.entries(value).flatMap(([key, inner]) => (typeof inner === 'object' && inner !== null ? keysOf(inner).map((k) => `${key}.${k}`) : [key]));

function stubFetch(status = 202) {
  const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => new Response(null, { status }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe('report-abuse island', () => {
  it('has the same texts in both languages', () => {
    expect(keysOf(REPORT_TEXT.en).toSorted()).toEqual(keysOf(REPORT_TEXT.mk).toSorted());
  });

  it('reports a section with an empty honeypot and without cookies', async () => {
    const user = userEvent.setup();
    const fetchMock = stubFetch();
    render(<ReportAbuse slug="ana" language="en" sections={[{ type: 'hero', label: 'Ana Petrovska' }, { type: 'faq', label: 'Questions' }]} />);

    await user.click(screen.getByRole('button', { name: 'Report a problem' }));
    await user.click(screen.getByLabelText('One part of the page'));
    await user.selectOptions(screen.getByLabelText('Which part?'), 'faq');
    await user.selectOptions(screen.getByLabelText('Reason'), 'misleading');
    await user.type(screen.getByLabelText('Description (optional)'), 'Fake diplomas');
    await user.click(screen.getByRole('button', { name: 'Send report' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Your report was sent to the ClassHub team.');
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('/api/public/sites/ana/reports');
    expect(init).toMatchObject({ method: 'POST', credentials: 'omit' });
    expect(JSON.parse(String(init!.body))).toEqual({
      target: 'section',
      sectionType: 'faq',
      testSlug: null,
      reason: 'misleading',
      details: 'Fake diplomas',
      email: null,
      website: '',
    });
  });

  it('checks the fields before sending and explains failures', async () => {
    const user = userEvent.setup();
    const fetchMock = stubFetch(429);
    render(<ReportAbuse slug="ana" testSlug="algebra" />);

    await user.click(screen.getByRole('button', { name: 'Пријави проблем' }));
    expect(screen.getByLabelText('Овој тест')).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Испрати пријава' }));
    expect(screen.getByText('Пополнете го ова поле.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();

    await user.selectOptions(screen.getByLabelText('Причина'), 'other');
    await user.click(screen.getByRole('button', { name: 'Испрати пријава' }));
    expect(fetchMock).not.toHaveBeenCalled(); // "other" needs a description

    await user.type(screen.getByLabelText('Опис'), 'Copied from a book');
    await user.type(screen.getByLabelText(/Ваша е-пошта/), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'Испрати пријава' }));
    expect(screen.getByText('Внесете важечка е-пошта.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText(/Ваша е-пошта/));
    await user.click(screen.getByRole('button', { name: 'Испрати пријава' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Испративте многу пријави.');
    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]!.body))).toMatchObject({ target: 'test', testSlug: 'algebra', reason: 'other' });
  });
});
