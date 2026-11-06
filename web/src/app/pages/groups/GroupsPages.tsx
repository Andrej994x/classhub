import { ArrowLeft } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { useAddMember, useArchiveGroup, useGroup, useGroups, useRemoveMember, useSaveGroup, type Group } from '../../../teacher/groups.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useStudents } from '../../../teacher/students.ts';
import { NotFoundContent } from '../Pages.tsx';

const select = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const name = (s: { firstName: string; lastName?: string | null }) => [s.firstName, s.lastName].filter(Boolean).join(' ');

/** The teacher's groups (R2-P2). */
export function GroupsPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const archived = params.get('archived') === 'true';
  const groups = useGroups(workspaceId, archived);
  const catalog = useCatalog();
  const subject = (id?: string | null) => catalog.data?.subjects.find((s) => s.id === id);

  if (groups.isError) return groups.error instanceof ApiError && groups.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(groups.error, t)}</Alert>;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('groups.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('groups.lead')}</p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/groups/new`}>
          {t('groups.add')}
        </Link>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" className="h-4 w-4" checked={archived} onChange={(e) => setParams(e.target.checked ? { archived: 'true' } : {}, { replace: true })} />
        {t('groups.showArchived')}
      </label>
      {!groups.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : groups.data.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{archived ? t('groups.noArchived') : t('groups.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('groups.title')} className="grid gap-3 sm:grid-cols-2">
          {groups.data.map((group) => (
            <li key={group.id}>
              <Link
                to={`/app/w/${workspaceId}/groups/${group.id}`}
                className="flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="font-semibold">{group.name}</span>
                <span className="text-sm text-muted-foreground">
                  {t('groups.memberCount', { count: num(group.memberCount) ?? 0 })}
                  {subject(group.subjectId) && ` · ${i18n.resolvedLanguage === 'en' ? subject(group.subjectId)!.nameEn : subject(group.subjectId)!.nameMk}`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export function GroupEditPage() {
  const { t } = useTranslation();
  const { workspaceId = '', groupId } = useParams();
  const group = useGroup(workspaceId, groupId);

  if (groupId && group.isError) return group.error instanceof ApiError && group.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(group.error, t)}</Alert>;
  // Start from the server's current state; later refetches never touch the open details form (see GroupDetails).
  if (groupId && (!group.data || !group.isFetchedAfterMount)) return <output className="block">{t('common.loading')}</output>;
  return <GroupEditor key={groupId ?? 'new'} workspaceId={workspaceId} group={groupId ? group.data : undefined} />;
}

function GroupEditor({ workspaceId, group }: { workspaceId: string; group: Group | undefined }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const catalog = useCatalog();
  const save = useSaveGroup(workspaceId, group?.id);
  const [values, setValues] = useState({ name: group?.name ?? '', subjectId: group?.subjectId ?? '', description: group?.description ?? '' });
  const [version, setVersion] = useState<number | null>(group ? (num(group.version) ?? 0) : null);
  const [missing, setMissing] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!values.name.trim()) {
      setMissing(true);
      return;
    }
    try {
      const saved = await save.mutateAsync({ name: values.name.trim(), subjectId: values.subjectId || null, description: values.description.trim() || null, version });
      setVersion(num(saved.version) ?? 0);
      if (!group) void navigate(`/app/w/${workspaceId}/groups/${saved.id}`, { replace: true });
    } catch {
      // shown below
    }
  };
  const saveError = save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('groups.changedElsewhere') : save.error ? errorMessage(save.error, t) : null;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/groups`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('groups.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{group ? group.name : t('groups.newTitle')}</h1>
        {group?.archived && <p className="text-sm text-muted-foreground">{t('groups.archivedLead')}</p>}
      </div>

      <form noValidate onSubmit={(e) => void submit(e)}>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('groups.details')}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('groups.fields.name')} maxLength={80} value={values.name} error={missing && !values.name.trim() ? t('groups.validation.name') : undefined} onChange={(e) => setValues({ ...values, name: e.target.value })} />
            <div className="flex flex-col gap-2">
              <Label htmlFor="group-subject">{t('groups.fields.subject')}</Label>
              <select id="group-subject" className={select} value={values.subjectId} onChange={(e) => setValues({ ...values, subjectId: e.target.value })}>
                <option value="">{t('groups.fields.noSubject')}</option>
                {/* A saved subject the ClassHub team deactivated is not in the catalog but stays valid on this group. */}
                {values.subjectId && !catalog.data?.subjects.some((s) => s.id === values.subjectId) && (
                  <option value={values.subjectId}>{catalog.data ? t('groups.fields.retiredSubject') : t('common.loading')}</option>
                )}
                {catalog.data?.subjects.map((s) => (
                  <option key={s.id} value={s.id}>
                    {i18n.resolvedLanguage === 'en' ? s.nameEn : s.nameMk}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label htmlFor="group-description">{t('groups.fields.description')}</Label>
              <textarea id="group-description" rows={2} maxLength={500} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={values.description} onChange={(e) => setValues({ ...values, description: e.target.value })} />
            </div>
            {saveError && (
              <Alert variant="error" className="sm:col-span-2">
                {saveError}
              </Alert>
            )}
            {save.isSuccess && group && (
              <Alert variant="success" className="sm:col-span-2">
                {t('groups.saved')}
              </Alert>
            )}
            <Button type="submit" className="self-start" disabled={save.isPending}>
              {save.isPending ? t('common.loading') : group ? t('groups.save') : t('groups.create')}
            </Button>
          </CardContent>
        </Card>
      </form>

      {group && <Members workspaceId={workspaceId} groupId={group.id} />}
      {group && version !== null && <ArchiveGroup workspaceId={workspaceId} group={group} version={version} onChanged={(changed) => setVersion(num(changed.version) ?? 0)} />}
    </section>
  );
}

/** Members follow the cache (each add/remove is its own operation, independent of the details form). */
function Members({ workspaceId, groupId }: { workspaceId: string; groupId: string }) {
  const { t } = useTranslation();
  const group = useGroup(workspaceId, groupId);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const students = useStudents(workspaceId, { q: search, archived: false, page });
  const pages = Math.max(1, Math.ceil((num(students.data?.total) ?? 0) / (num(students.data?.pageSize) ?? 20)));
  const add = useAddMember(workspaceId, groupId);
  const remove = useRemoveMember(workspaceId, groupId);
  const [pick, setPick] = useState('');
  const members = group.data?.members ?? [];
  const candidates = (students.data?.items ?? []).filter((s) => !members.some((m) => m.studentId === s.id));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('groups.members.title')}</CardTitle>
        <CardDescription>{t('groups.members.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {members.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('groups.members.none')}</p>
        ) : (
          <ul aria-label={t('groups.members.title')} className="flex flex-col divide-y divide-border">
            {members.map((member) => (
              <li key={member.studentId} className="flex items-center justify-between gap-3 py-2">
                <Link className="text-sm font-medium hover:underline" to={`/app/w/${workspaceId}/students/${member.studentId}`}>
                  {name(member)}
                  {member.studentArchived && <span className="ml-2 text-xs text-muted-foreground">({t('students.archived')})</span>}
                </Link>
                <Button type="button" size="sm" variant="ghost" disabled={remove.isPending} aria-label={t('groups.members.remove', { name: name(member) })} onClick={() => remove.mutate(member.studentId)}>
                  {t('groups.members.removeShort')}
                </Button>
              </li>
            ))}
          </ul>
        )}
        {!group.data?.archived && (
          <form
            className="flex flex-col gap-2 sm:flex-row sm:items-end"
            onSubmit={(e) => {
              e.preventDefault();
              if (pick) add.mutate(pick, { onSuccess: () => setPick('') });
            }}
          >
            <div className="flex flex-1 flex-col gap-1">
              <Label htmlFor="group-member-search">{t('groups.members.search')}</Label>
              <input
                id="group-member-search"
                type="search"
                className={select}
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  setPage(1);
                  setPick('');
                }}
              />
            </div>
            <div className="flex flex-1 flex-col gap-1">
              <Label htmlFor="group-add-member">{t('groups.members.add')}</Label>
              <select id="group-add-member" className={select} value={pick} onChange={(e) => setPick(e.target.value)}>
                <option value="">{t('groups.members.choose')}</option>
                {candidates.map((s) => (
                  <option key={s.id} value={s.id}>
                    {/* Contact details tell apart students with the same name. */}
                    {[name(s), s.email ?? s.phone].filter(Boolean).join(' · ')}
                  </option>
                ))}
              </select>
            </div>
            <Button type="submit" disabled={add.isPending || !pick}>
              {t('groups.members.addButton')}
            </Button>
          </form>
        )}
        {!group.data?.archived && pages > 1 && (
          <nav aria-label={t('groups.members.pages')} className="flex items-center gap-2">
            <Button type="button" size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              {t('leads.previous')}
            </Button>
            <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
            <Button type="button" size="sm" variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
              {t('leads.next')}
            </Button>
          </nav>
        )}
        {(add.error ?? remove.error) && <Alert variant="error">{errorMessage(add.error ?? remove.error, t)}</Alert>}
      </CardContent>
    </Card>
  );
}

function ArchiveGroup({ workspaceId, group, version, onChanged }: { workspaceId: string; group: Group; version: number; onChanged: (changed: Group) => void }) {
  const { t } = useTranslation();
  const archive = useArchiveGroup(workspaceId, group.id);
  const current = useGroup(workspaceId, group.id).data ?? group;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{current.archived ? t('groups.restoreTitle') : t('groups.archiveTitle')}</CardTitle>
        <CardDescription>{current.archived ? t('groups.restoreLead') : t('groups.archiveLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {archive.error && (
          <Alert variant="error">{archive.error instanceof ApiError && archive.error.code === 'CONCURRENCY_CONFLICT' ? t('groups.changedElsewhere') : errorMessage(archive.error, t)}</Alert>
        )}
        <Button type="button" variant="outline" className="self-start" disabled={archive.isPending} onClick={() => archive.mutate({ archived: !current.archived, version }, { onSuccess: onChanged })}>
          {current.archived ? t('groups.restore') : t('groups.archive')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** On the student page: the groups the student is in. */
export function StudentGroups({ workspaceId, studentId }: { workspaceId: string; studentId: string }) {
  const { t } = useTranslation();
  const groups = useGroups(workspaceId, false, studentId);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('groups.ofStudent')}</CardTitle>
      </CardHeader>
      <CardContent>
        {groups.isError ? (
          <Alert variant="error">{errorMessage(groups.error, t)}</Alert>
        ) : !groups.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : groups.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('groups.noneForStudent')}</p>
        ) : (
          <ul aria-label={t('groups.ofStudent')} className="flex flex-wrap gap-2">
            {groups.data.map((g) => (
              <li key={g.id}>
                <Link className="rounded-full bg-muted px-3 py-1 text-sm hover:underline" to={`/app/w/${workspaceId}/groups/${g.id}`}>
                  {g.name}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
