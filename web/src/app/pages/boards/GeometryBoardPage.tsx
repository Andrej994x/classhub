import { ArrowLeft, Undo2 } from 'lucide-react';
import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import {
  addObject,
  addPoint,
  emptyConstruction,
  fromDocument,
  GEOMETRY_SCHEMA_VERSION,
  movePoints,
  POINTS_NEEDED,
  type Construction,
  type GeometryTool,
  type View,
} from '../../../boards/geometry/construction.ts';
import type { BoardTarget } from '../../../boards/geometry/GeometryCanvas.tsx';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { useActivity, useSaveActivity } from '../../../teacher/activities.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

// JSXGraph loads only with this page.
const GeometryCanvas = lazy(() => import('../../../boards/geometry/GeometryCanvas.tsx'));

const field = 'h-10 w-full rounded-md border border-input bg-background px-3 text-sm';
const TOOLS: GeometryTool[] = ['move', 'point', 'segment', 'line', 'circle', 'triangle', 'angle'];
const NO_SELECTION: ReadonlySet<string> = new Set();

/**
 * `/app/w/:workspaceId/toolbox/geometry-board[?activity=…]` (R3-P4): constructions with points, segments, lines,
 * circles, triangles and angles on a coordinate system, with undo; saved into the Activity Library in ClassHub's own
 * format (D-33) and opened again from there. Saving works like the drawing board (versions, no reload after the first
 * save, the latest version when reopening).
 */
export function GeometryBoardPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [search] = useSearchParams();
  const activityId = search.get('activity');
  const [createdHere, setCreatedHere] = useState<string | null>(null);
  const justCreated = activityId !== null && activityId === createdHere;
  const activity = useActivity(workspaceId, justCreated ? null : activityId);

  if (justCreated) return <Board key="new" workspaceId={workspaceId} loaded={null} onCreated={setCreatedHere} />;
  if (activityId && activity.isError) {
    return activity.error instanceof ApiError && activity.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(activity.error, t)}</Alert>;
  }
  if (activityId && !activity.isFetchedAfterMount) return <output className="block">{t('common.loading')}</output>;
  return <Board key={activityId ?? 'new'} workspaceId={workspaceId} loaded={activity.data ?? null} onCreated={setCreatedHere} />;
}

type Loaded = NonNullable<ReturnType<typeof useActivity>['data']>;

function Board({ workspaceId, loaded, onCreated }: { workspaceId: string; loaded: Loaded | null; onCreated: (id: string) => void }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const catalog = useCatalog();
  const save = useSaveActivity(workspaceId);
  const [construction, setConstruction] = useState<Construction>(() => (loaded ? fromDocument(loaded.document) : emptyConstruction()));
  const [history, setHistory] = useState<Construction[]>([]);
  const [tool, setTool] = useState<GeometryTool>('point');
  const [picked, setPicked] = useState<string[]>([]);
  const view = useRef<View['boundingBox']>(construction.view.boundingBox);
  const [title, setTitle] = useState(loaded?.title ?? '');
  const [subjectId, setSubjectId] = useState(loaded?.subjectId ?? '');
  const [id, setId] = useState<string | null>(loaded?.id ?? null);
  const [version, setVersion] = useState<number | null>(loaded ? Number(loaded.version) : null);
  const [savedAs, setSavedAs] = useState<string | null>(null);
  const en = i18n.resolvedLanguage === 'en';
  const current = JSON.stringify([construction.objects, construction.view.axis, construction.view.grid, title.trim(), subjectId]);
  const selected = useMemo(() => (picked.length ? new Set(picked) : NO_SELECTION), [picked]);

  /** A change of the construction: the one before it is kept for undo. */
  const change = (next: Construction) => {
    setHistory((h) => [...h.slice(-99), construction]);
    setConstruction(next);
  };

  const undo = useCallback(() => {
    setPicked([]);
    if (history.length === 0) return;
    setConstruction(history[history.length - 1]!);
    setHistory(history.slice(0, -1));
  }, [history]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z' && !(event.target instanceof HTMLInputElement)) {
        event.preventDefault();
        undo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [undo]);

  const onTarget = (target: BoardTarget) => {
    if (tool === 'move') return;
    let next = construction;
    let pointId: string;
    if ('pointId' in target) {
      pointId = target.pointId;
    } else {
      [next, pointId] = addPoint(construction, target.x, target.y);
    }
    if (tool === 'point') {
      if (next !== construction) change(next);
      return;
    }
    const chosen = picked.includes(pointId) ? picked : [...picked, pointId];
    if (chosen.length < POINTS_NEEDED[tool]) {
      if (next !== construction) change(next);
      setPicked(chosen);
      return;
    }
    const built = addObject(next, tool, chosen);
    setPicked([]);
    if (built) change(built);
    else if (next !== construction) change(next);
  };

  const chooseTool = (next: GeometryTool) => {
    setTool(next);
    setPicked([]);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const sending = current;
    const document: Construction = { ...construction, view: { ...construction.view, boundingBox: view.current } };
    save.mutate(
      { id, body: { title: title.trim(), subjectId: subjectId || null, toolType: 'geometry-board', schemaVersion: GEOMETRY_SCHEMA_VERSION, document, version } },
      {
        onSuccess: (result) => {
          setVersion(Number(result.version));
          setSavedAs(sending);
          if (!id) {
            setId(result.id);
            onCreated(result.id);
            navigate(`/app/w/${workspaceId}/toolbox/geometry-board?activity=${result.id}`, { replace: true });
          }
        },
      },
    );
  };

  const needed = tool === 'move' ? 0 : POINTS_NEEDED[tool];
  return (
    <section className="flex flex-col gap-4">
      <Link to={`/app/w/${workspaceId}/activities`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('activities.title')}
      </Link>
      <h1 className="text-2xl font-semibold tracking-tight">{t('activities.boards.geometry-board')}</h1>
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
      {savedAs === current && !save.isPending && <Alert variant="success">{t('boards.saved')}</Alert>}

      <div role="toolbar" aria-label={t('boards.geometry.tools')} className="flex flex-wrap items-center gap-2">
        {TOOLS.map((name) => (
          <Button key={name} type="button" size="sm" variant={tool === name ? 'default' : 'outline'} aria-pressed={tool === name} onClick={() => chooseTool(name)}>
            {t(`boards.geometry.tool.${name}`)}
          </Button>
        ))}
        <Button type="button" size="sm" variant="outline" disabled={history.length === 0} onClick={undo}>
          <Undo2 className="h-4 w-4" aria-hidden />
          {t('boards.geometry.undo')}
        </Button>
        <label className="inline-flex items-center gap-1.5 text-sm">
          <input type="checkbox" checked={construction.view.axis} onChange={(e) => change({ ...construction, view: { ...construction.view, axis: e.target.checked } })} />
          {t('boards.geometry.axis')}
        </label>
        <label className="inline-flex items-center gap-1.5 text-sm">
          <input type="checkbox" checked={construction.view.grid} onChange={(e) => change({ ...construction, view: { ...construction.view, grid: e.target.checked } })} />
          {t('boards.geometry.grid')}
        </label>
      </div>
      <p className="text-sm text-muted-foreground" aria-live="polite">
        {tool === 'move' ? t('boards.geometry.hint.move') : t('boards.geometry.hint.pick', { count: needed, picked: picked.length })}
      </p>
      <div className="h-[65vh] min-h-[380px] overflow-hidden rounded-lg border border-border bg-white">
        <Suspense fallback={<output className="block p-4">{t('common.loading')}</output>}>
          <GeometryCanvas
            construction={construction}
            tool={tool}
            selected={selected}
            initialView={construction.view.boundingBox}
            label={t('boards.geometry.board', { count: construction.objects.length })}
            onTarget={onTarget}
            onPointsMoved={(moves) => {
              // One drag is one step, whatever it moved (a point, or a shape and its points); no movement, no change.
              const moved = movePoints(construction, moves);
              if (JSON.stringify(moved.objects) !== JSON.stringify(construction.objects)) change(moved);
            }}
            onView={(box) => (view.current = box)}
          />
        </Suspense>
      </div>
    </section>
  );
}
