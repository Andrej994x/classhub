import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '../../components/ui/button.tsx';
import { Label } from '../../components/ui/label.tsx';
import { num } from '../../teacher/offers.ts';
import { useStudents } from '../../teacher/students.ts';

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';

/**
 * Chooses one active student: search, then pick from a paged list. Contact details tell apart students with the same
 * name. A value that is not on the current page (an existing choice) keeps its own option via `currentLabel`.
 */
export function StudentPicker({
  workspaceId,
  id,
  label,
  value,
  onChange,
  currentLabel,
  exclude = [],
  error,
}: {
  workspaceId: string;
  id: string;
  label: string;
  value: string;
  onChange: (studentId: string) => void;
  currentLabel?: string;
  exclude?: readonly string[];
  error?: string;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const students = useStudents(workspaceId, { q: search, archived: false, page });
  const pages = Math.max(1, Math.ceil((num(students.data?.total) ?? 0) / (num(students.data?.pageSize) ?? 20)));
  const candidates = (students.data?.items ?? []).filter((s) => !exclude.includes(s.id));
  const listed = candidates.some((s) => s.id === value);
  // The label of every student chosen here (or given for the initial value), so a choice that a later search or page
  // no longer lists still shows its own name.
  const [labels, setLabels] = useState<Record<string, string>>(() => (value && currentLabel ? { [value]: currentLabel } : {}));
  const optionLabel = (s: (typeof candidates)[number]) => [[s.firstName, s.lastName].filter(Boolean).join(' '), s.email ?? s.phone].filter(Boolean).join(' · ');

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={`${id}-search`}>{t('students.picker.search')}</Label>
      <input
        id={`${id}-search`}
        type="search"
        className={field}
        value={search}
        onChange={(e) => {
          setSearch(e.target.value);
          setPage(1);
        }}
      />
      <Label htmlFor={id}>{label}</Label>
      <select
        id={id}
        className={field}
        value={value}
        aria-invalid={error ? true : undefined}
        onChange={(e) => {
          const picked = candidates.find((s) => s.id === e.target.value);
          if (picked) setLabels({ ...labels, [picked.id]: optionLabel(picked) });
          onChange(e.target.value);
        }}
      >
        <option value="">{t('students.picker.choose')}</option>
        {value && !listed && labels[value] && <option value={value}>{labels[value]}</option>}
        {candidates.map((s) => (
          <option key={s.id} value={s.id}>
            {optionLabel(s)}
          </option>
        ))}
      </select>
      {error && <p className="text-sm text-destructive">{error}</p>}
      {pages > 1 && (
        <nav aria-label={t('students.picker.pages')} className="flex items-center gap-2">
          <Button type="button" size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
          <Button type="button" size="sm" variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </div>
  );
}
