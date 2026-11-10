import type { ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types';
import { ArrowLeft } from 'lucide-react';
import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { selfHostExcalidrawAssets } from '../../../boards/drawing/assets.ts';
import { DRAWING_SCHEMA_VERSION, fromDocument, toDocument, type DrawingDocument } from '../../../boards/drawing/scene.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { LargeScreenNote } from '../../components/LargeScreenNote.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { useActivity, useSaveActivity } from '../../../teacher/activities.ts';
import { countActivityOpened } from '../../../teacher/usage.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

// Excalidraw is large: it loads only with this page. Its fonts are registered while its module loads, so the
// self-hosted asset path is set first.
const DrawingCanvas = lazy(() => {
  selfHostExcalidrawAssets();
  return import('../../../boards/drawing/DrawingCanvas.tsx');
});

const field = 'h-10 w-full rounded-md border border-input bg-background px-3 text-sm';
const EMPTY: DrawingDocument = { elements: [], appState: { viewBackgroundColor: '#ffffff', gridModeEnabled: false } };

/**
 * `/app/w/:workspaceId/toolbox/drawing-board[?activity=…]` (R3-P3): draw, then save into the Activity Library; an
 * activity opens again from the library. Saving a new drawing creates the activity; later saves change it with the
 * version that was loaded (a change elsewhere is a conflict, never overwritten).
 */
export function DrawingBoardPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [search] = useSearchParams();
  const activityId = search.get('activity');
  // The activity this mounted page has just created: its board already holds it (no reload, the same board stays).
  // Page state, never navigation state: after a refresh or Back the page loads the activity like any other.
  const [createdHere, setCreatedHere] = useState<string | null>(null);
  const justCreated = activityId !== null && activityId === createdHere;
  const activity = useActivity(workspaceId, justCreated ? null : activityId);

  if (justCreated) return <Board key="new" workspaceId={workspaceId} loaded={null} onCreated={setCreatedHere} />;
  if (activityId && activity.isError) {
    return activity.error instanceof ApiError && activity.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(activity.error, t)}</Alert>;
  }
  // Opened from the library: wait for this opening's fetch (never a cached, possibly older version), so the board
  // starts from the latest title, scene and version; later background updates do not reset the board.
  if (activityId && !activity.isFetchedAfterMount) return <output className="block">{t('common.loading')}</output>;
  // A new key when another activity opens: the board starts from that activity's scene.
  return <Board key={activityId ?? 'new'} workspaceId={workspaceId} loaded={activity.data ?? null} onCreated={setCreatedHere} />;
}

type Loaded = NonNullable<ReturnType<typeof useActivity>['data']>;

function Board({ workspaceId, loaded, onCreated }: { workspaceId: string; loaded: Loaded | null; onCreated: (id: string) => void }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const catalog = useCatalog();
  const save = useSaveActivity(workspaceId);
  // A saved activity opened again on its board is a reuse (counted once per opening; usage analytics, R3-P6).
  const counted = useRef(false);
  useEffect(() => {
    if (loaded && !counted.current) {
      counted.current = true;
      countActivityOpened(workspaceId, loaded.id);
    }
  }, [loaded, workspaceId]);
  const api = useRef<ExcalidrawImperativeAPI | null>(null);
  const [initial] = useState(() => (loaded ? fromDocument(loaded.document) : EMPTY));
  const [title, setTitle] = useState(loaded?.title ?? '');
  const [subjectId, setSubjectId] = useState(loaded?.subjectId ?? '');
  const [id, setId] = useState<string | null>(loaded?.id ?? null);
  const [version, setVersion] = useState<number | null>(loaded ? Number(loaded.version) : null);
  // What was last saved (scene, title, subject): the confirmation shows only while the board still matches it, so an
  // edit after saving (or during the save) never looks saved.
  const [scene, setScene] = useState('');
  const [savedAs, setSavedAs] = useState<string | null>(null);
  const [blocked, setBlocked] = useState(false);
  const en = i18n.resolvedLanguage === 'en';

  const current = JSON.stringify([scene, title.trim(), subjectId]);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!api.current) return;
    const sending = current;
    const document = toDocument(api.current.getSceneElements() as never, api.current.getAppState() as never);
    save.mutate(
      { id, body: { title: title.trim(), subjectId: subjectId || null, toolType: 'drawing-board', schemaVersion: DRAWING_SCHEMA_VERSION, document, version } },
      {
        onSuccess: (result) => {
          setVersion(Number(result.version));
          setSavedAs(sending);
          if (!id) {
            // A new activity: from now on this board is that activity (later saves change it; the link reopens it).
            setId(result.id);
            onCreated(result.id);
            navigate(`/app/w/${workspaceId}/toolbox/drawing-board?activity=${result.id}`, { replace: true });
          }
        },
      },
    );
  };

  return (
    <section className="flex flex-col gap-4">
      <Link to={`/app/w/${workspaceId}/activities`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('activities.title')}
      </Link>
      <h1 className="text-2xl font-semibold tracking-tight">{t('activities.boards.drawing-board')}</h1>
      <LargeScreenNote />
      <form onSubmit={submit} aria-label={t('boards.saveForm')} className="grid gap-3 sm:grid-cols-[2fr_1fr_auto] sm:items-end">
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('activities.titleField')}
          <input className={field} value={title} required maxLength={150} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('toolbox.subject')}
          <select className={field} value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('activities.noSubject')}</option>
            {catalog.data?.subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {en ? s.nameEn : s.nameMk}
              </option>
            ))}
          </select>
        </label>
        <Button type="submit" disabled={save.isPending || !title.trim()}>
          {t('boards.save')}
        </Button>
      </form>
      {save.isError && <Alert variant="error">{errorMessage(save.error, t)}</Alert>}
      {blocked && <Alert variant="warning">{t('boards.blocked')}</Alert>}
      {savedAs === current && !save.isPending && <Alert variant="success">{t('boards.saved')}</Alert>}
      <p className="text-xs text-muted-foreground">{t('boards.drawingNote')}</p>
      <div className="h-[70vh] min-h-[420px] overflow-hidden rounded-lg border border-border">
        <Suspense fallback={<output className="block p-4">{t('common.loading')}</output>}>
          <DrawingCanvas initial={initial} onReady={(ready) => (api.current = ready)} onBlocked={() => setBlocked(true)} onSceneChange={setScene} />
        </Suspense>
      </div>
    </section>
  );
}
