import { ArrowLeft, Download, FileText } from 'lucide-react';
import { useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { useGroups } from '../../../teacher/groups.ts';
import {
  formatBytes,
  materialFileProblem,
  materialFileUrl,
  MATERIAL_EXTENSIONS,
  myMaterialFileUrl,
  useArchiveMaterial,
  useAssignMaterial,
  useDeleteMaterial,
  useMaterial,
  useMaterials,
  useMyMaterials,
  useUnassignMaterial,
  useUpdateMaterial,
  useUploadMaterial,
  type Material,
} from '../../../teacher/materials.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { StudentPicker } from '../../components/StudentPicker.tsx';
import { NotFoundContent } from '../Pages.tsx';

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const textarea = 'w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm';
const TITLE_MAX = 120;
const DESCRIPTION_MAX = 1000;

function SubjectSelect({ id, value, onChange, label, emptyLabel }: { id: string; value: string; onChange: (subjectId: string) => void; label?: string; emptyLabel?: string }) {
  const { t, i18n } = useTranslation();
  const catalog = useCatalog();
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label ?? t('materials.fields.subject')}</Label>
      <select id={id} className={field} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">{emptyLabel ?? t('materials.fields.noSubject')}</option>
        {/* A saved subject the ClassHub team deactivated is not in the catalog but stays valid on this material. */}
        {value && !catalog.data?.subjects.some((s) => s.id === value) && <option value={value}>{catalog.data ? t('groups.fields.retiredSubject') : t('common.loading')}</option>}
        {catalog.data?.subjects.map((s) => (
          <option key={s.id} value={s.id}>
            {i18n.resolvedLanguage === 'en' ? s.nameEn : s.nameMk}
          </option>
        ))}
      </select>
    </div>
  );
}

/** The teacher's materials (R2-P8): upload, search and the storage the workspace uses. */
export function MaterialsPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const archived = params.get('archived') === 'true';
  const [q, setQ] = useState('');
  const [subjectId, setSubjectId] = useState('');
  const [page, setPage] = useState(1);
  const materials = useMaterials(workspaceId, { q, subjectId, archived, page });
  const pages = Math.max(1, Math.ceil((num(materials.data?.total) ?? 0) / (num(materials.data?.pageSize) ?? 20)));

  if (materials.isError && !materials.data)
    return materials.error instanceof ApiError && materials.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(materials.error, t)}</Alert>;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('materials.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('materials.lead')}</p>
        {materials.data && (
          <p className="text-sm text-muted-foreground">{t('materials.storageUsed', { size: formatBytes(num(materials.data.storageUsedBytes) ?? 0, i18n.resolvedLanguage ?? 'mk') })}</p>
        )}
      </div>

      {!archived && <UploadCard workspaceId={workspaceId} />}

      <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end">
        <div className="flex flex-col gap-1 sm:w-72">
          <Label htmlFor="materials-search">{t('materials.search')}</Label>
          <input
            id="materials-search"
            type="search"
            className={field}
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <div className="sm:w-60">
          <SubjectSelect
            id="materials-subject-filter"
            label={t('materials.filterSubject')}
            emptyLabel={t('materials.allSubjects')}
            value={subjectId}
            onChange={(next) => {
              setSubjectId(next);
              setPage(1);
            }}
          />
        </div>
        <label className="flex items-center gap-2 text-sm sm:h-10">
          <input
            type="checkbox"
            className="h-4 w-4"
            checked={archived}
            onChange={(e) => {
              setParams(e.target.checked ? { archived: 'true' } : {}, { replace: true });
              setPage(1);
            }}
          />
          {t('materials.showArchived')}
        </label>
      </div>

      {!materials.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : materials.data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{q.trim() || subjectId ? t('materials.noMatches') : archived ? t('materials.noArchived') : t('materials.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('materials.title')} className="grid gap-3 sm:grid-cols-2">
          {materials.data.items.map((m) => (
            <li key={m.id}>
              <Link
                to={`/app/w/${workspaceId}/materials/${m.id}`}
                className="flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="font-semibold">{m.title}</span>
                <span className="text-sm text-muted-foreground">
                  {m.fileName} · {formatBytes(num(m.sizeBytes) ?? 0, i18n.resolvedLanguage ?? 'mk')} · {t('materials.assignedCount', { count: num(m.assignmentCount) ?? 0 })}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      {pages > 1 && (
        <nav aria-label={t('materials.pages')} className="flex items-center gap-2">
          <Button type="button" size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
          <Button type="button" size="sm" variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </section>
  );
}

function UploadCard({ workspaceId }: { workspaceId: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const upload = useUploadMaterial(workspaceId);
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [values, setValues] = useState({ title: '', description: '', subjectId: '' });
  const [autoTitle, setAutoTitle] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const fileProblem = file ? materialFileProblem(file) : 'missing';
  const titleMissing = !values.title.trim();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (!file || fileProblem || titleMissing) return;
    try {
      const created = await upload.mutateAsync({ file, title: values.title.trim(), description: values.description.trim(), subjectId: values.subjectId });
      void navigate(`/app/w/${workspaceId}/materials/${created.id}`);
    } catch {
      // shown below
    }
  };

  return (
    <form noValidate onSubmit={(e) => void submit(e)}>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('materials.upload.title')}</CardTitle>
          <CardDescription>{t('materials.upload.lead', { types: MATERIAL_EXTENSIONS.join(', ') })}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="material-file">{t('materials.upload.file')}</Label>
            <input
              id="material-file"
              ref={input}
              type="file"
              accept={MATERIAL_EXTENSIONS.join(',')}
              className="text-sm"
              aria-invalid={submitted && fileProblem !== null}
              aria-describedby={submitted && fileProblem ? 'material-file-error' : undefined}
              onChange={(e) => {
                const chosen = e.target.files?.[0] ?? null;
                setFile(chosen);
                // The file name is a good first title; it follows the chosen file until the teacher types their own.
                if (chosen && (!values.title.trim() || values.title === autoTitle)) {
                  const title = chosen.name.replace(/\.[^.]+$/, '').slice(0, TITLE_MAX);
                  setAutoTitle(title);
                  setValues({ ...values, title });
                }
              }}
            />
            {submitted && fileProblem && (
              <p id="material-file-error" className="text-sm text-destructive">
                {t(`materials.upload.${fileProblem}`)}
              </p>
            )}
          </div>
          <FormField
            label={t('materials.fields.title')}
            maxLength={TITLE_MAX}
            value={values.title}
            error={submitted && titleMissing ? t('materials.validation.title') : undefined}
            onChange={(e) => setValues({ ...values, title: e.target.value })}
          />
          <SubjectSelect id="material-subject" value={values.subjectId} onChange={(subjectId) => setValues({ ...values, subjectId })} />
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="material-description">{t('materials.fields.description')}</Label>
            <textarea id="material-description" rows={2} maxLength={DESCRIPTION_MAX} className={textarea} value={values.description} onChange={(e) => setValues({ ...values, description: e.target.value })} />
          </div>
          {upload.error && (
            <Alert variant="error" className="sm:col-span-2">
              {errorMessage(upload.error, t)}
            </Alert>
          )}
          <Button type="submit" className="self-start" disabled={upload.isPending}>
            {upload.isPending ? t('materials.upload.uploading') : t('materials.upload.submit')}
          </Button>
        </CardContent>
      </Card>
    </form>
  );
}

/** Keyed by the route's id, like the other editors: another material is a fresh mount that waits for its own refetch. */
export function MaterialPage() {
  const { materialId } = useParams();
  return <MaterialLoader key={materialId} />;
}

function MaterialLoader() {
  const { t } = useTranslation();
  const { workspaceId = '', materialId = '' } = useParams();
  const material = useMaterial(workspaceId, materialId);
  if (material.isError) return material.error instanceof ApiError && material.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(material.error, t)}</Alert>;
  if (!material.data || !material.isFetchedAfterMount) return <output className="block">{t('common.loading')}</output>;
  return <MaterialEditor workspaceId={workspaceId} material={material.data} />;
}

function MaterialEditor({ workspaceId, material }: { workspaceId: string; material: Material }) {
  const { t, i18n } = useTranslation();
  const save = useUpdateMaterial(workspaceId, material.id);
  const [values, setValues] = useState({ title: material.title, description: material.description ?? '', subjectId: material.subjectId ?? '' });
  const [version, setVersion] = useState(num(material.version) ?? 0);
  const [missing, setMissing] = useState(false);
  const current = useMaterial(workspaceId, material.id).data ?? material;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!values.title.trim()) {
      setMissing(true);
      return;
    }
    try {
      const saved = await save.mutateAsync({ title: values.title.trim(), description: values.description.trim() || null, subjectId: values.subjectId || null, version });
      setVersion(num(saved.version) ?? 0);
    } catch {
      // shown below
    }
  };
  const saveError = save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('materials.changedElsewhere') : save.error ? errorMessage(save.error, t) : null;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/materials`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('materials.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{current.title}</h1>
        {current.archived && <p className="text-sm text-muted-foreground">{t('materials.archivedLead')}</p>}
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('materials.file')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center justify-between gap-3">
          <span className="inline-flex items-center gap-2 text-sm">
            <FileText className="h-4 w-4" aria-hidden />
            {current.fileName} · {formatBytes(num(current.sizeBytes) ?? 0, i18n.resolvedLanguage ?? 'mk')}
          </span>
          <a className={buttonVariants({ variant: 'outline' })} href={materialFileUrl(workspaceId, current.id)} download={current.fileName}>
            <Download className="h-4 w-4" aria-hidden />
            {t('materials.download')}
          </a>
        </CardContent>
      </Card>

      <form noValidate onSubmit={(e) => void submit(e)}>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('materials.details')}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField
              label={t('materials.fields.title')}
              maxLength={TITLE_MAX}
              value={values.title}
              error={missing && !values.title.trim() ? t('materials.validation.title') : undefined}
              onChange={(e) => setValues({ ...values, title: e.target.value })}
            />
            <SubjectSelect id="material-edit-subject" value={values.subjectId} onChange={(subjectId) => setValues({ ...values, subjectId })} />
            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label htmlFor="material-edit-description">{t('materials.fields.description')}</Label>
              <textarea id="material-edit-description" rows={3} maxLength={DESCRIPTION_MAX} className={textarea} value={values.description} onChange={(e) => setValues({ ...values, description: e.target.value })} />
            </div>
            {saveError && (
              <Alert variant="error" className="sm:col-span-2">
                {saveError}
              </Alert>
            )}
            {save.isSuccess && (
              <Alert variant="success" className="sm:col-span-2">
                {t('materials.saved')}
              </Alert>
            )}
            <Button type="submit" className="self-start" disabled={save.isPending}>
              {save.isPending ? t('common.loading') : t('materials.save')}
            </Button>
          </CardContent>
        </Card>
      </form>

      <Assignments workspaceId={workspaceId} material={current} />
      <ArchiveMaterial workspaceId={workspaceId} material={current} version={version} onChanged={(changed) => setVersion(num(changed.version) ?? 0)} />
      <DeleteMaterial workspaceId={workspaceId} material={current} version={version} />
    </section>
  );
}

/** Assignments follow the cache (each is its own operation; they do not change the material's version). */
function Assignments({ workspaceId, material }: { workspaceId: string; material: Material }) {
  const { t } = useTranslation();
  const assign = useAssignMaterial(workspaceId, material.id);
  const unassign = useUnassignMaterial(workspaceId, material.id);
  const groups = useGroups(workspaceId, false);
  const [student, setStudent] = useState('');
  const [group, setGroup] = useState('');
  const assignedStudents = material.assignments.flatMap((a) => (a.studentId ? [a.studentId] : []));
  const freeGroups = (groups.data ?? []).filter((g) => !material.assignments.some((a) => a.groupId === g.id));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('materials.assignments.title')}</CardTitle>
        <CardDescription>{t('materials.assignments.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {material.assignments.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('materials.assignments.none')}</p>
        ) : (
          <ul aria-label={t('materials.assignments.title')} className="flex flex-col divide-y divide-border">
            {material.assignments.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-3 py-2">
                <Link className="text-sm font-medium hover:underline" to={a.studentId ? `/app/w/${workspaceId}/students/${a.studentId}` : `/app/w/${workspaceId}/groups/${a.groupId}`}>
                  {a.name}
                  <span className="ml-2 text-xs text-muted-foreground">({a.studentId ? t('materials.assignments.student') : t('materials.assignments.group')})</span>
                </Link>
                <Button type="button" size="sm" variant="ghost" disabled={unassign.isPending} aria-label={t('materials.assignments.remove', { name: a.name })} onClick={() => unassign.mutate(a.id)}>
                  {t('materials.assignments.removeShort')}
                </Button>
              </li>
            ))}
          </ul>
        )}
        {material.archived ? (
          <p className="text-sm text-muted-foreground">{t('materials.assignments.archived')}</p>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            <form
              className="flex flex-col gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (student) assign.mutate({ studentId: student }, { onSuccess: () => setStudent('') });
              }}
            >
              <StudentPicker workspaceId={workspaceId} id="material-assign-student" label={t('materials.assignments.toStudent')} value={student} onChange={setStudent} exclude={assignedStudents} />
              <Button type="submit" variant="outline" className="self-start" disabled={assign.isPending || !student}>
                {t('materials.assignments.assign')}
              </Button>
            </form>
            <form
              className="flex flex-col gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (group) assign.mutate({ groupId: group }, { onSuccess: () => setGroup('') });
              }}
            >
              <Label htmlFor="material-assign-group">{t('materials.assignments.toGroup')}</Label>
              <select id="material-assign-group" className={field} value={group} onChange={(e) => setGroup(e.target.value)}>
                <option value="">{t('materials.assignments.chooseGroup')}</option>
                {freeGroups.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </select>
              <Button type="submit" variant="outline" className="self-start" disabled={assign.isPending || !group}>
                {t('materials.assignments.assign')}
              </Button>
            </form>
          </div>
        )}
        {(assign.error ?? unassign.error) && <Alert variant="error">{errorMessage(assign.error ?? unassign.error, t)}</Alert>}
      </CardContent>
    </Card>
  );
}

function ArchiveMaterial({ workspaceId, material, version, onChanged }: { workspaceId: string; material: Material; version: number; onChanged: (changed: Material) => void }) {
  const { t } = useTranslation();
  const archive = useArchiveMaterial(workspaceId, material.id);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{material.archived ? t('materials.restoreTitle') : t('materials.archiveTitle')}</CardTitle>
        <CardDescription>{material.archived ? t('materials.restoreLead') : t('materials.archiveLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {archive.error && (
          <Alert variant="error">{archive.error instanceof ApiError && archive.error.code === 'CONCURRENCY_CONFLICT' ? t('materials.changedElsewhere') : errorMessage(archive.error, t)}</Alert>
        )}
        <Button type="button" variant="outline" className="self-start" disabled={archive.isPending} onClick={() => archive.mutate({ archived: !material.archived, version }, { onSuccess: onChanged })}>
          {material.archived ? t('materials.restore') : t('materials.archive')}
        </Button>
      </CardContent>
    </Card>
  );
}

function DeleteMaterial({ workspaceId, material, version }: { workspaceId: string; material: Material; version: number }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const remove = useDeleteMaterial(workspaceId, material.id);
  const [confirming, setConfirming] = useState(false);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('materials.deleteTitle')}</CardTitle>
        <CardDescription>{t('materials.deleteLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {remove.error && (
          <Alert variant="error">{remove.error instanceof ApiError && remove.error.code === 'CONCURRENCY_CONFLICT' ? t('materials.changedElsewhere') : errorMessage(remove.error, t)}</Alert>
        )}
        {confirming ? (
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="destructive"
              disabled={remove.isPending}
              onClick={() => remove.mutate(version, { onSuccess: () => void navigate(`/app/w/${workspaceId}/materials`, { replace: true }) })}
            >
              {t('materials.deleteConfirm')}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setConfirming(false)}>
              {t('common.cancel')}
            </Button>
          </div>
        ) : (
          <Button type="button" variant="outline" className="self-start" onClick={() => setConfirming(true)}>
            {t('materials.delete')}
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

/** In the student portal: the materials the teacher gave the student or one of their groups. */
export function MyMaterials({ workspaceId }: { workspaceId: string }) {
  const { t, i18n } = useTranslation();
  const materials = useMyMaterials(workspaceId);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('studentPortal.materials.title')}</CardTitle>
      </CardHeader>
      <CardContent>
        {materials.isError ? (
          <Alert variant="error">{errorMessage(materials.error, t)}</Alert>
        ) : !materials.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : materials.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('studentPortal.materials.none')}</p>
        ) : (
          <ul aria-label={t('studentPortal.materials.title')} className="flex flex-col divide-y divide-border">
            {materials.data.map((m) => (
              <li key={m.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                <div className="flex min-w-0 flex-col">
                  <span className="font-medium">{m.title}</span>
                  {m.description && <span className="text-sm text-muted-foreground">{m.description}</span>}
                  <span className="text-xs text-muted-foreground">
                    {m.fileName} · {formatBytes(num(m.sizeBytes) ?? 0, i18n.resolvedLanguage ?? 'mk')}
                  </span>
                </div>
                <a className={buttonVariants({ variant: 'outline', size: 'sm' })} href={myMaterialFileUrl(workspaceId, m.id)} download={m.fileName} aria-label={t('studentPortal.materials.download', { title: m.title })}>
                  <Download className="h-4 w-4" aria-hidden />
                  {t('materials.download')}
                </a>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
