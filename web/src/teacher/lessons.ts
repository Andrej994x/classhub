import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client.ts';
import type { components } from '../api/schema';

export type Lesson = components['schemas']['LessonResponse'];

export interface LessonInput {
  kind: 'Individual' | 'Group';
  studentId: string | null;
  groupId: string | null;
  subjectId: string | null;
  title: string;
  topic: string | null;
  /** Wall-clock start in the workspace time zone, `YYYY-MM-DDTHH:mm:00`. */
  startLocal: string;
  durationMinutes: number;
  mode: 'Online' | 'InPerson';
  meetingUrl: string | null;
  location: string | null;
}

const keys = {
  all: (workspaceId: string) => ['w', workspaceId, 'lessons'] as const,
  range: (workspaceId: string, from: string, to: string) => ['w', workspaceId, 'lessons', 'range', from, to] as const,
  one: (workspaceId: string, lessonId: string) => ['w', workspaceId, 'lessons', lessonId] as const,
};

/** Lessons starting on the local days from..to (inclusive, `YYYY-MM-DD`, workspace time zone). */
export function useLessons(workspaceId: string, from: string, to: string) {
  return useQuery({
    queryKey: keys.range(workspaceId, from, to),
    placeholderData: keepPreviousData,
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/lessons', { params: { path: { workspaceId }, query: { from, to } } })).data!,
  });
}

export function useLesson(workspaceId: string, lessonId: string | undefined) {
  return useQuery({
    queryKey: keys.one(workspaceId, lessonId ?? 'new'),
    enabled: lessonId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/lessons/{lessonId}', { params: { path: { workspaceId, lessonId: lessonId! } } })).data!,
  });
}

function useLessonMutation<TInput>(workspaceId: string, run: (input: TInput) => Promise<Lesson>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (lesson) => {
      queryClient.setQueryData(keys.one(workspaceId, lesson.id), lesson);
      await queryClient.invalidateQueries({ queryKey: [...keys.all(workspaceId), 'range'] });
    },
  });
}

export function useSaveLesson(workspaceId: string, lessonId?: string) {
  return useLessonMutation(workspaceId, async ({ input, version }: { input: LessonInput; version: number | null }) =>
    lessonId
      ? (await api.client.PUT('/api/w/{workspaceId}/lessons/{lessonId}', { params: { path: { workspaceId, lessonId } }, body: { ...input, version } })).data!
      : (await api.client.POST('/api/w/{workspaceId}/lessons', { params: { path: { workspaceId } }, body: { ...input, version: null } })).data!,
  );
}

export function useCompleteLesson(workspaceId: string, lessonId: string) {
  return useLessonMutation(workspaceId, async (version: number) =>
    (await api.client.POST('/api/w/{workspaceId}/lessons/{lessonId}/complete', { params: { path: { workspaceId, lessonId } }, body: { version } })).data!,
  );
}

export function useCancelLesson(workspaceId: string, lessonId: string) {
  return useLessonMutation(workspaceId, async ({ version, reason }: { version: number; reason: string }) =>
    (await api.client.POST('/api/w/{workspaceId}/lessons/{lessonId}/cancel', { params: { path: { workspaceId, lessonId } }, body: { version, reason } })).data!,
  );
}

// --- Plain local dates (`YYYY-MM-DD`): calendar arithmetic without time zones (UTC noon avoids any DST edge). ---

const parse = (date: string) => new Date(`${date}T12:00:00Z`);
const format = (d: Date) => d.toISOString().slice(0, 10);

export const isDate = (value: string | null | undefined): value is string => !!value && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(parse(value).getTime());

export function addDays(date: string, days: number): string {
  const d = parse(date);
  d.setUTCDate(d.getUTCDate() + days);
  return format(d);
}

export function addMonths(date: string, months: number): string {
  const d = parse(date);
  const day = d.getUTCDate();
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() + months);
  const last = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0, 12)).getUTCDate();
  d.setUTCDate(Math.min(day, last));
  return format(d);
}

/** Monday of the week (ISO weeks, as in North Macedonia). */
export function startOfWeek(date: string): string {
  const weekday = (parse(date).getUTCDay() + 6) % 7;
  return addDays(date, -weekday);
}

export const startOfMonth = (date: string) => `${date.slice(0, 8)}01`;

/** The 6 × 7 grid of a month view, Monday first. */
export function monthGrid(date: string): string[] {
  const first = startOfWeek(startOfMonth(date));
  return Array.from({ length: 42 }, (_, i) => addDays(first, i));
}

/** Today in a time zone (the workspace's), as `YYYY-MM-DD`. */
export function today(timeZone: string, now = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  const get = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${get('year')}-${get('month')}-${get('day')}`;
}

/** The local day and time of a server wall-clock value (`2030-11-12T17:00:00` → `2030-11-12`, `17:00`). */
export const localDay = (value: string) => value.slice(0, 10);
export const localTime = (value: string) => value.slice(11, 16);

// --- Recurring lessons (R2-P5) ---

export type Series = components['schemas']['SeriesResponse'];
export type SeriesResult = components['schemas']['SeriesResultResponse'];

export interface SeriesInput {
  kind: 'Individual' | 'Group';
  studentId: string | null;
  groupId: string | null;
  subjectId: string | null;
  title: string;
  topic: string | null;
  /** `YYYY-MM-DD` and `HH:mm:00`, workspace time zone. */
  startDate: string;
  startTime: string;
  durationMinutes: number;
  intervalWeeks: 1 | 2;
  endDate: string | null;
  count: number | null;
  mode: 'Online' | 'InPerson';
  meetingUrl: string | null;
  location: string | null;
}

const seriesKey = (workspaceId: string, seriesId: string) => ['w', workspaceId, 'lesson-series', seriesId] as const;

export function useSeries(workspaceId: string, seriesId: string | undefined) {
  return useQuery({
    queryKey: seriesKey(workspaceId, seriesId ?? 'none'),
    enabled: seriesId !== undefined,
    refetchOnMount: 'always',
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}/lesson-series/{seriesId}', { params: { path: { workspaceId, seriesId: seriesId! } } })).data!,
  });
}

function useSeriesMutation<TInput, TResult>(workspaceId: string, run: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    // A series changes many lessons at once: every lesson view reloads.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['w', workspaceId] }),
  });
}

export function useCreateSeries(workspaceId: string) {
  return useSeriesMutation(workspaceId, async (input: SeriesInput) =>
    (await api.client.POST('/api/w/{workspaceId}/lesson-series', { params: { path: { workspaceId } }, body: input })).data!,
  );
}

export function useUpdateSeries(workspaceId: string, seriesId: string) {
  return useSeriesMutation(
    workspaceId,
    async ({ scope, fromLessonId, series, version, timeZone }: { scope: 'ThisAndFollowing' | 'AllFuture'; fromLessonId: string | null; series: SeriesInput; version: number; timeZone: string }) =>
      (await api.client.PUT('/api/w/{workspaceId}/lesson-series/{seriesId}', { params: { path: { workspaceId, seriesId } }, body: { scope, fromLessonId, series, version, timeZone } })).data!,
  );
}

export function useEndSeries(workspaceId: string, seriesId: string) {
  return useSeriesMutation(workspaceId, async ({ fromDate, version }: { fromDate: string; version: number }) =>
    (await api.client.POST('/api/w/{workspaceId}/lesson-series/{seriesId}/end', { params: { path: { workspaceId, seriesId } }, body: { fromDate, version } })).data!,
  );
}

/**
 * The one end condition of a series: a series set to a count that was later ended on a date has both, and the earlier
 * boundary wins (an edit sends exactly one of them).
 */
export function seriesEnd(series: Series): { endDate: string | null; count: number | null } {
  const count = series.count === null ? null : Number(series.count);
  if (count === null || series.endDate === null) return { endDate: series.endDate, count };
  const lastByCount = addDays(series.startDate, (count - 1) * 7 * Number(series.intervalWeeks));
  return series.endDate < lastByCount ? { endDate: series.endDate, count: null } : { endDate: null, count };
}

/** The current wall-clock time in a time zone, `HH:mm`. */
export function nowTime(timeZone: string, now = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(now);
  const get = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${get('hour')}:${get('minute')}`;
}
