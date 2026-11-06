import { ArrowLeft } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { NEXT_STATUSES, useAddLeadNote, useArchiveLead, useChangeLeadStatus, useLead, type Lead, type LeadStatus } from '../../../teacher/leads.ts';
import { useConvertLead } from '../../../teacher/students.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;

export function LeadDetailPage() {
  const { t } = useTranslation();
  const { workspaceId = '', leadId = '' } = useParams();
  const lead = useLead(workspaceId, leadId);

  if (lead.isPending || (!lead.isFetchedAfterMount && !lead.isError)) return <output className="block">{t('common.loading')}</output>;
  if (lead.isError) return lead.error instanceof ApiError && lead.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(lead.error, t)}</Alert>;
  return <LeadDetail workspaceId={workspaceId} lead={lead.data} />;
}

function LeadDetail({ workspaceId, lead }: { workspaceId: string; lead: Lead }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  const catalog = useCatalog();
  const status = useChangeLeadStatus(workspaceId, lead.id);
  const archive = useArchiveLead(workspaceId, lead.id);
  const addNote = useAddLeadNote(workspaceId, lead.id);
  const [note, setNote] = useState('');
  const version = num(lead.version) ?? 0;
  const current = lead.status as LeadStatus;
  const subject = catalog.data?.subjects.find((s) => s.id === lead.subjectId);
  const busy = status.isPending || archive.isPending;

  const changeError = status.error ?? archive.error;
  const errorText =
    changeError instanceof ApiError && changeError.code === 'CONCURRENCY_CONFLICT' ? t('leads.changedElsewhere') : changeError ? errorMessage(changeError, t) : null;

  const submitNote = (event: FormEvent) => {
    event.preventDefault();
    if (!note.trim()) return;
    addNote.mutate(note.trim(), { onSuccess: () => setNote('') });
  };

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/leads`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('leads.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{lead.name}</h1>
        <p className="text-sm text-muted-foreground">
          {t(`leads.types.${lead.type}`)} · {formatDateTime(lead.createdAtUtc, language, browserTimeZone)}
          {lead.archived && ` · ${t('leads.archived')}`}
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('leads.request')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <dl className="grid gap-2 text-sm sm:grid-cols-[auto_1fr] sm:gap-x-6">
            <dt className="font-medium">{t('leads.email')}</dt>
            <dd>
              <a className="text-primary underline-offset-4 hover:underline" href={`mailto:${lead.email}`}>
                {lead.email}
              </a>
            </dd>
            {lead.phone && (
              <>
                <dt className="font-medium">{t('leads.phone')}</dt>
                <dd>
                  <a className="text-primary underline-offset-4 hover:underline" href={`tel:${lead.phone.replace(/[^0-9+]/g, '')}`}>
                    {lead.phone}
                  </a>
                </dd>
              </>
            )}
            {lead.offerTitle && (
              <>
                <dt className="font-medium">{t('leads.offer')}</dt>
                <dd>{lead.offerTitle}</dd>
              </>
            )}
            {subject && (
              <>
                <dt className="font-medium">{t('leads.subject')}</dt>
                <dd>{language === 'en' ? subject.nameEn : subject.nameMk}</dd>
              </>
            )}
            {lead.diagnosticResult && (
              <>
                <dt className="font-medium">{t('leads.test')}</dt>
                <dd>
                  <Link className="text-primary underline-offset-4 hover:underline" to={`/app/w/${workspaceId}/tests/${lead.diagnosticResult.testId}`}>
                    {lead.diagnosticResult.testTitle}
                  </Link>
                  {' · '}
                  {lead.diagnosticResult.points === null || lead.diagnosticResult.points === undefined
                    ? t('leads.resultNotShared')
                    : t('leads.resultShared', { points: num(lead.diagnosticResult.points) ?? 0, max: num(lead.diagnosticResult.maxPoints) ?? 0 })}
                </dd>
              </>
            )}
            <dt className="font-medium">{t('leads.consent')}</dt>
            <dd className="text-muted-foreground">{t('leads.consentGiven', { date: formatDateTime(lead.consentedAtUtc, language, browserTimeZone), version: lead.consentVersion })}</dd>
          </dl>
          {lead.message && <p className="whitespace-pre-line rounded-md bg-muted p-3 text-sm">{lead.message}</p>}
        </CardContent>
      </Card>

      <ConvertCard workspaceId={workspaceId} lead={lead} />

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">
            {t('leads.status')}: {t(`leads.statuses.${current}`)}
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {errorText && <Alert variant="error">{errorText}</Alert>}
          <div className="flex flex-wrap gap-2">
            {NEXT_STATUSES[current].map((next) => (
              <Button key={next} type="button" variant={next === 'Closed' ? 'outline' : 'default'} disabled={busy} onClick={() => status.mutate({ status: next, version })}>
                {t(`leads.moveTo.${next}`)}
              </Button>
            ))}
            <Button type="button" variant="ghost" disabled={busy} onClick={() => archive.mutate({ archived: !lead.archived, version })}>
              {lead.archived ? t('leads.restore') : t('leads.archive')}
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('leads.notes')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {lead.notes.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('leads.noNotes')}</p>
          ) : (
            <ol aria-label={t('leads.notes')} className="flex flex-col gap-3">
              {lead.notes.map((n) => (
                <li key={n.id} className="rounded-md border border-border p-3 text-sm">
                  <p className="whitespace-pre-line">{n.text}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{formatDateTime(n.createdAtUtc, language, browserTimeZone)}</p>
                </li>
              ))}
            </ol>
          )}
          <form onSubmit={submitNote} className="flex flex-col gap-2">
            <Label htmlFor="lead-note">{t('leads.addNote')}</Label>
            <textarea id="lead-note" rows={3} maxLength={2000} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={note} onChange={(e) => setNote(e.target.value)} />
            {addNote.error && <Alert variant="error">{errorMessage(addNote.error, t)}</Alert>}
            <Button type="submit" className="self-start" disabled={addNote.isPending || !note.trim()}>
              {t('leads.saveNote')}
            </Button>
          </form>
        </CardContent>
      </Card>
    </section>
  );
}

/** D-12: turn the request into a student (linked by email to an existing one), or open the student it became. */
function ConvertCard({ workspaceId, lead }: { workspaceId: string; lead: Lead }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const convert = useConvertLead(workspaceId);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('leads.convert.title')}</CardTitle>
        <CardDescription>{lead.studentId ? t('leads.convert.done') : lead.status === 'Closed' ? t('leads.convert.closed') : t('leads.convert.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {convert.error && <Alert variant="error">{errorMessage(convert.error, t)}</Alert>}
        {lead.studentId ? (
          <Link className="self-start text-sm font-medium text-primary underline-offset-4 hover:underline" to={`/app/w/${workspaceId}/students/${lead.studentId}`}>
            {t('leads.convert.open')}
          </Link>
        ) : lead.status === 'Closed' ? null : (
          <Button
            type="button"
            className="self-start"
            disabled={convert.isPending}
            onClick={() => convert.mutate(lead.id, { onSuccess: (result) => void navigate(`/app/w/${workspaceId}/students/${result.student.id}`) })}
          >
            {convert.isPending ? t('common.loading') : t('leads.convert.action')}
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
