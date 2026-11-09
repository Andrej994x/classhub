import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useNavigate, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { PASSWORD_MIN, readFragment, resetSchema, type ResetForm } from '../../../auth/schemas.ts';
import { useMe } from '../../../auth/session.ts';
import { LanguageSwitcher } from '../../components/LanguageSwitcher.tsx';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { isLanguage } from '../../../i18n/index.ts';
import { pendingInvitation, useAcceptInvitation, useAcceptWithAccount, useInvitationPreview, useMyStudentProfile, useMyTeachers, useNotificationPreferences, useSaveNotificationPreferences, type InvitationPreview } from '../../../student/portal.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { applyServerFieldErrors } from '../auth/serverErrors.ts';
import { NotFoundContent } from '../Pages.tsx';
import { MyHomeworkList } from '../homework/HomeworkPages.tsx';
import { MyMaterials } from '../materials/MaterialsPages.tsx';
import { MyLessons } from './MyLessons.tsx';

const linkClass = 'text-primary underline-offset-4 hover:underline';

/** The token from the link, read once and kept for this tab only (signing in first keeps it). */
function useInvitationToken(): string | null {
  const [token] = useState(() => {
    const fromLink = typeof window === 'undefined' ? null : readFragment(window.location.hash).get('token');
    if (fromLink) {
      pendingInvitation.write(fromLink);
      window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search);
      return fromLink;
    }
    return pendingInvitation.read();
  });
  return token;
}

/** `/student/invitation#token=…`: accept with a new account or with the account the visitor is signed in with. */
export function InvitationPage() {
  const { t } = useTranslation();
  const token = useInvitationToken();
  const me = useMe();
  const preview = useInvitationPreview(token);

  if (!token || preview.error instanceof ApiError) {
    return (
      <Card>
        <CardHeader>
          <h1 className="text-2xl font-semibold tracking-tight">{t('studentPortal.invitation.title')}</h1>
          <Alert variant="error">{t('studentPortal.invitation.invalid')}</Alert>
        </CardHeader>
      </Card>
    );
  }
  if (preview.isError) return <Alert variant="error">{errorMessage(preview.error, t)}</Alert>;
  if (!preview.data || me.isPending) return <output className="block">{t('common.loading')}</output>;

  return (
    <Card>
      <CardHeader>
        <h1 className="text-2xl font-semibold tracking-tight">{t('studentPortal.invitation.title')}</h1>
        <CardDescription>{t('studentPortal.invitation.lead', { teacher: preview.data.teacherName, name: preview.data.studentFirstName })}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {me.data ? <AcceptSignedIn token={token} preview={preview.data} email={me.data.email} /> : <AcceptWithAccount token={token} preview={preview.data} />}
      </CardContent>
    </Card>
  );
}

function GuardianConsent({ checked, onChange, error }: { checked: boolean; onChange: (value: boolean) => void; error: boolean }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-2 rounded-md border border-border p-3">
      <p className="text-sm">{t('studentPortal.invitation.minorNotice')}</p>
      <label className="flex items-start gap-2 text-sm">
        <input type="checkbox" className="mt-0.5 h-4 w-4" checked={checked} onChange={(e) => onChange(e.target.checked)} aria-invalid={error || undefined} />
        {t('studentPortal.invitation.guardianConsent')}
      </label>
      {error && <p className="text-sm text-destructive">{t('studentPortal.invitation.guardianConsentRequired')}</p>}
    </div>
  );
}

function useAccepted() {
  const navigate = useNavigate();
  return (workspaceId: string) => {
    pendingInvitation.clear();
    void navigate(`/student/w/${workspaceId}`, { replace: true });
  };
}

function AcceptSignedIn({ token, preview, email }: { token: string; preview: InvitationPreview; email: string }) {
  const { t } = useTranslation();
  const accept = useAcceptInvitation();
  const accepted = useAccepted();
  const [consent, setConsent] = useState(false);
  const [tried, setTried] = useState(false);

  return (
    <>
      <p className="text-sm">{t('studentPortal.invitation.signedInAs', { email })}</p>
      {preview.isMinor && <GuardianConsent checked={consent} onChange={setConsent} error={tried && !consent} />}
      {accept.error && <Alert variant="error">{errorMessage(accept.error, t)}</Alert>}
      <Button
        type="button"
        disabled={accept.isPending}
        onClick={() => {
          setTried(true);
          if (preview.isMinor && !consent) return;
          accept.mutate({ token, guardianConsent: consent }, { onSuccess: (linked) => accepted(linked.workspaceId) });
        }}
      >
        {accept.isPending ? t('common.loading') : t('studentPortal.invitation.accept')}
      </Button>
    </>
  );
}

function AcceptWithAccount({ token, preview }: { token: string; preview: InvitationPreview }) {
  const { t, i18n } = useTranslation();
  const accept = useAcceptWithAccount();
  const accepted = useAccepted();
  const [consent, setConsent] = useState(false);
  const form = useForm<ResetForm>({ resolver: zodResolver(resetSchema), defaultValues: { password: '', confirmPassword: '' } });
  const { errors, isSubmitting, isSubmitted } = form.formState;

  const submit = form.handleSubmit(async (values) => {
    if (preview.isMinor && !consent) return;
    try {
      const linked = await accept.mutateAsync({
        token,
        password: values.password,
        locale: isLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'mk',
        guardianConsent: consent,
      });
      accepted(linked.workspaceId);
    } catch (error) {
      applyServerFieldErrors(error, form.setError, { password: { field: 'password', message: 'auth.validation.passwordRejected' } });
    }
  });
  const registered = accept.error instanceof ApiError && accept.error.code === 'EMAIL_REGISTERED';
  const fieldsMarked = accept.error instanceof ApiError && accept.error.code === 'VALIDATION_FAILED' && accept.error.fieldErrors.password;

  return (
    <>
      <form noValidate onSubmit={(e) => void submit(e)} className="flex flex-col gap-4">
        <FormField label={t('auth.fields.email')} type="email" value={preview.email} readOnly />
        <FormField
          label={t('auth.fields.password')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.fields.passwordHint', { min: PASSWORD_MIN })}
          error={errors.password?.message && t(errors.password.message, { min: PASSWORD_MIN })}
          {...form.register('password')}
        />
        <FormField
          label={t('auth.fields.confirmPassword')}
          type="password"
          autoComplete="new-password"
          error={errors.confirmPassword?.message && t(errors.confirmPassword.message)}
          {...form.register('confirmPassword')}
        />
        {preview.isMinor && <GuardianConsent checked={consent} onChange={setConsent} error={isSubmitted && !consent} />}
        {registered ? (
          <Alert variant="error">{t('studentPortal.invitation.emailRegistered')}</Alert>
        ) : (
          accept.error && !fieldsMarked && <Alert variant="error">{errorMessage(accept.error, t)}</Alert>
        )}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('studentPortal.invitation.create')}
        </Button>
      </form>
      <p className="text-sm">
        {t('studentPortal.invitation.haveAccount')}{' '}
        <Link className={linkClass} to={`/login?returnTo=${encodeURIComponent('/student/invitation')}`}>
          {t('nav.signIn')}
        </Link>
      </p>
    </>
  );
}

/** `/student`: the teachers the user is a student of; with one teacher, straight to that page. */
export function StudentHomePage() {
  const { t } = useTranslation();
  const teachers = useMyTeachers();
  if (teachers.isError) return <Alert variant="error">{errorMessage(teachers.error, t)}</Alert>;
  if (!teachers.data) return <output className="block">{t('common.loading')}</output>;
  if (teachers.data.length === 1) return <Navigate to={`/student/w/${teachers.data[0]!.workspaceId}`} replace />;
  return (
    <section className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">{t('student.title')}</h1>
      {teachers.data.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('studentPortal.noTeachers')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('studentPortal.teachers')} className="grid gap-3 sm:grid-cols-2">
          {teachers.data.map((teacher) => (
            <li key={teacher.workspaceId}>
              <Link
                to={`/student/w/${teacher.workspaceId}`}
                className="flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="font-semibold">{teacher.teacherName}</span>
                <span className="text-sm text-muted-foreground">{t('studentPortal.as', { name: [teacher.firstName, teacher.lastName].filter(Boolean).join(' ') })}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** `/student/w/:workspaceId`: the student's own page with one teacher (R2-P12 dashboard): lessons, subjects, homework and materials. */
export function StudentTeacherPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const profile = useMyStudentProfile(workspaceId);
  const teachers = useMyTeachers();
  const catalog = useCatalog();

  if (profile.isError) return profile.error instanceof ApiError && profile.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(profile.error, t)}</Alert>;
  if (!profile.data) return <output className="block">{t('common.loading')}</output>;
  const subjects = profile.data.subjectIds
    .map((id) => catalog.data?.subjects.find((s) => s.id === id))
    .filter((s) => s !== undefined)
    .map((s) => (i18n.resolvedLanguage === 'en' ? s.nameEn : s.nameMk));

  return (
    <section className="flex flex-col gap-6">
      {(teachers.data?.length ?? 0) > 1 && (
        <Link to="/student" className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
          <ArrowLeft className="h-4 w-4" aria-hidden />
          {t('studentPortal.allTeachers')}
        </Link>
      )}
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{profile.data.teacherName}</h1>
        <p className="text-sm text-muted-foreground">{t('studentPortal.as', { name: [profile.data.firstName, profile.data.lastName].filter(Boolean).join(' ') })}</p>
      </div>
      <MyLessons workspaceId={workspaceId} />
      {subjects.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('studentPortal.subjects')}</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="flex flex-wrap gap-2">
              {subjects.map((name) => (
                <li key={name} className="rounded-full bg-muted px-3 py-1 text-sm">
                  {name}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
      <MyHomeworkList workspaceId={workspaceId} />
      <MyMaterials workspaceId={workspaceId} />
    </section>
  );
}

/** `/student/settings`: language (saved on the account) and, later, notification preferences. */
export function StudentSettingsPage() {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">{t('studentPortal.settings.title')}</h1>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('studentPortal.settings.language')}</CardTitle>
          <CardDescription>{t('studentPortal.settings.languageLead')}</CardDescription>
        </CardHeader>
        <CardContent>
          <LanguageSwitcher />
        </CardContent>
      </Card>
      <NotificationSettings />
    </section>
  );
}

/** Lesson changes always arrive by email; reminders and new homework can be switched off (R2-P11). */
function NotificationSettings() {
  const { t } = useTranslation();
  const preferences = useNotificationPreferences();
  const save = useSaveNotificationPreferences();
  const toggle = (key: 'lessonReminders' | 'homeworkEmails', value: boolean) => save.mutate({ [key]: value });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('studentPortal.settings.notifications')}</CardTitle>
        <CardDescription>{t('studentPortal.settings.notificationsLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {preferences.isError ? (
          <Alert variant="error">{errorMessage(preferences.error, t)}</Alert>
        ) : !preferences.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : (
          <>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={preferences.data.lessonReminders} disabled={save.isPending} onChange={(e) => toggle('lessonReminders', e.target.checked)} />
              {t('studentPortal.settings.lessonReminders')}
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={preferences.data.homeworkEmails} disabled={save.isPending} onChange={(e) => toggle('homeworkEmails', e.target.checked)} />
              {t('studentPortal.settings.homeworkEmails')}
            </label>
            {save.error && <Alert variant="error">{errorMessage(save.error, t)}</Alert>}
          </>
        )}
      </CardContent>
    </Card>
  );
}
