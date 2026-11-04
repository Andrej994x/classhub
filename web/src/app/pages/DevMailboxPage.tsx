import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../api/errors.ts';
import { Alert } from '../../components/ui/alert.tsx';
import { Button } from '../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card.tsx';
import { FormField } from '../../components/ui/form-field.tsx';
import { formatDateTime } from '../../lib/format.ts';

const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

interface DevMailboxItem {
  id: string;
  to: string;
  subject: string;
  text: string;
  template: string;
  receivedAtUtc: string;
}

/** Splits plain text into text and links (links in dev emails point back to this app). */
function Linkified({ text }: { text: string }) {
  const parts = text.split(/(https?:\/\/[^\s<>"]+)/g);
  return (
    <p className="whitespace-pre-wrap break-words text-sm">
      {parts.map((part, index) =>
        /^https?:\/\//.test(part) ? (
          <a key={index} href={part} className="text-primary underline">
            {part}
          </a>
        ) : (
          part
        ),
      )}
    </p>
  );
}

/**
 * Development and E2E only: shows the emails the API "sent" to the local dev mailbox. The API endpoint (and
 * this page's server route) do not exist in other environments; then this page shows "not available".
 */
export function DevMailboxPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const to = params.get('to') ?? '';
  const [draft, setDraft] = useState(to);
  const mailbox = useQuery({
    queryKey: ['dev', 'mailbox', to],
    // The endpoint is excluded from the OpenAPI contract (development only), so it is fetched untyped.
    queryFn: async () => {
      const query = to ? `?to=${encodeURIComponent(to)}` : '';
      const response = await fetch(`/api/dev/mailbox${query}`, { credentials: 'include', cache: 'no-store' });
      if (!response.ok) throw new ApiError(response.status, response.status === 404 || response.status === 401 ? 'NOT_FOUND' : 'INTERNAL_ERROR', response.statusText);
      return (await response.json()) as DevMailboxItem[];
    },
    refetchInterval: 2_000,
    retry: false,
  });

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setParams(draft ? { to: draft.trim() } : {});
  };

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t('devMailbox.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('devMailbox.lead')}</p>
      <form onSubmit={onSubmit} className="flex items-end gap-2">
        <div className="flex-1">
          <FormField label={t('devMailbox.to')} type="email" value={draft} onChange={(event) => setDraft(event.target.value)} />
        </div>
        <Button type="submit">{t('devMailbox.show')}</Button>
      </form>
      {mailbox.isError && <Alert variant="error">{mailbox.error instanceof ApiError && mailbox.error.code === 'NOT_FOUND' ? t('devMailbox.unavailable') : errorMessage(mailbox.error, t)}</Alert>}
      {mailbox.data?.length === 0 && <Alert>{t('devMailbox.empty')}</Alert>}
      <ul className="flex flex-col gap-3" aria-label={t('devMailbox.messages')}>
        {mailbox.data?.map((message) => (
          <li key={message.id}>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">{message.subject}</CardTitle>
                <CardDescription>
                  {message.to} · {formatDateTime(message.receivedAtUtc, i18n.resolvedLanguage ?? 'mk', browserTimeZone)}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Linkified text={message.text} />
              </CardContent>
            </Card>
          </li>
        ))}
      </ul>
    </section>
  );
}
