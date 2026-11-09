import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDate, formatDateTime, formatMoney, parseMoney } from '../../../lib/format.ts';
import {
  PAYMENT_METHODS,
  useAdjustBalance,
  useAdjustLessons,
  useAssignPackage,
  useClosePackage,
  useFinanceOverview,
  useFinanceReport,
  useRecordPayment,
  useReverse,
  useStudentFinance,
  type FinanceEntry,
  type Package,
  type PaymentMethod,
  type StudentFinance,
} from '../../../teacher/finance.ts';
import { today } from '../../../teacher/lessons.ts';
import { num, useOffers } from '../../../teacher/offers.ts';
import { useWorkspace } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

/** The viewer's own time zone (history times). */
const viewerTimeZone = () => new Intl.DateTimeFormat().resolvedOptions().timeZone;

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const REASON_MAX = 500;

/** The currencies a form offers: the workspace's own first, then those the student already has. */
function useCurrencies(workspaceId: string, finance: StudentFinance | undefined) {
  const workspace = useWorkspace(workspaceId);
  const own = workspace.data?.currency ?? 'MKD';
  const used = [...(finance?.balances.map((b) => b.currency) ?? []), ...(finance?.packages.map((p) => p.currency) ?? [])];
  return { workspaceCurrency: own, currencies: [own, ...new Set(used.filter((c) => c !== own))], timeZone: workspace.data?.timeZone ?? 'Europe/Skopje' };
}

function Balances({ balances }: { balances: StudentFinance['balances'] }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  if (balances.length === 0) return <p className="text-sm text-muted-foreground">{t('finance.noRecords')}</p>;
  return (
    <ul aria-label={t('finance.balances')} className="grid gap-2 sm:grid-cols-2">
      {balances.map((b) => {
        const outstanding = num(b.outstandingMinor) ?? 0;
        return (
          <li key={b.currency} className="rounded-md border border-border p-3">
            <p className="text-xs text-muted-foreground">{b.currency}</p>
            <p className="text-lg font-semibold">
              {outstanding > 0
                ? t('finance.owes', { amount: formatMoney(outstanding, b.currency, language) })
                : outstanding < 0
                  ? t('finance.credit', { amount: formatMoney(-outstanding, b.currency, language) })
                  : t('finance.settled')}
            </p>
            <p className="text-xs text-muted-foreground">
              {t('finance.chargedPaid', { charged: formatMoney(num(b.chargedMinor) ?? 0, b.currency, language), paid: formatMoney(num(b.paidMinor) ?? 0, b.currency, language) })}
            </p>
          </li>
        );
      })}
    </ul>
  );
}

/** On the student page: what the student owes, their packages, payments and corrections, and the history (R2-P10). */
export function FinanceCard({ workspaceId, studentId, archived }: { workspaceId: string; studentId: string; archived: boolean }) {
  const { t } = useTranslation();
  const finance = useStudentFinance(workspaceId, studentId);
  const [action, setAction] = useState<'package' | 'payment' | 'adjustment' | null>(null);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('finance.title')}</CardTitle>
        <CardDescription>{t('finance.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {finance.isError ? (
          <Alert variant="error">{errorMessage(finance.error, t)}</Alert>
        ) : !finance.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : (
          <>
            <Balances balances={finance.data.balances} />
            {(num(finance.data.unpaidLessons) ?? 0) > 0 && <Alert variant="warning">{t('finance.unpaidLessons', { count: num(finance.data.unpaidLessons) ?? 0 })}</Alert>}
            <Packages workspaceId={workspaceId} studentId={studentId} packages={finance.data.packages} />
            <div className="flex flex-wrap gap-2">
              {!archived && (
                <Button type="button" variant={action === 'package' ? 'default' : 'outline'} onClick={() => setAction(action === 'package' ? null : 'package')}>
                  {t('finance.package.add')}
                </Button>
              )}
              <Button type="button" variant={action === 'payment' ? 'default' : 'outline'} onClick={() => setAction(action === 'payment' ? null : 'payment')}>
                {t('finance.payment.add')}
              </Button>
              <Button type="button" variant={action === 'adjustment' ? 'default' : 'outline'} onClick={() => setAction(action === 'adjustment' ? null : 'adjustment')}>
                {t('finance.adjustment.add')}
              </Button>
            </div>
            {action === 'package' && <AssignPackageForm workspaceId={workspaceId} studentId={studentId} finance={finance.data} onDone={() => setAction(null)} />}
            {action === 'payment' && <PaymentForm workspaceId={workspaceId} studentId={studentId} finance={finance.data} onDone={() => setAction(null)} />}
            {action === 'adjustment' && <AdjustmentForm workspaceId={workspaceId} studentId={studentId} finance={finance.data} onDone={() => setAction(null)} />}
            <History workspaceId={workspaceId} studentId={studentId} entries={finance.data.entries} />
          </>
        )}
      </CardContent>
    </Card>
  );
}

function Packages({ workspaceId, studentId, packages }: { workspaceId: string; studentId: string; packages: Package[] }) {
  const { t, i18n } = useTranslation();
  const close = useClosePackage(workspaceId, studentId);
  if (packages.length === 0) return <p className="text-sm text-muted-foreground">{t('finance.package.none')}</p>;
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('finance.package.title')}</h3>
      <ul aria-label={t('finance.package.title')} className="flex flex-col divide-y divide-border">
        {packages.map((p) => (
          <li key={p.id} className="flex flex-col gap-2 py-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="font-medium">
                {p.title}
                {p.closed && <span className="ml-2 text-xs text-muted-foreground">({t('finance.package.closed')})</span>}
              </span>
              <span className="text-sm">{t('finance.package.remaining', { remaining: num(p.remainingLessons) ?? 0, total: num(p.lessonCount) ?? 0 })}</span>
            </div>
            <p className="text-xs text-muted-foreground">{formatMoney(num(p.priceMinor) ?? 0, p.currency, i18n.resolvedLanguage ?? 'mk')}</p>
            {!p.closed && (
              <div className="flex flex-wrap gap-2">
                <LessonCorrection workspaceId={workspaceId} studentId={studentId} pkg={p} />
                <Button type="button" size="sm" variant="ghost" disabled={close.isPending} onClick={() => close.mutate({ packageId: p.id, version: num(p.version) ?? 0 })}>
                  {t('finance.package.close')}
                </Button>
              </div>
            )}
          </li>
        ))}
      </ul>
      {close.error && <Alert variant="error">{errorMessage(close.error, t)}</Alert>}
    </div>
  );
}

/** ± lessons with a reason (D-22: e.g. a no-show that counts). */
function LessonCorrection({ workspaceId, studentId, pkg }: { workspaceId: string; studentId: string; pkg: Package }) {
  const { t } = useTranslation();
  const adjust = useAdjustLessons(workspaceId, studentId);
  const [open, setOpen] = useState(false);
  const [quantity, setQuantity] = useState('1');
  const [reason, setReason] = useState('');
  if (!open)
    return (
      <Button type="button" size="sm" variant="outline" onClick={() => setOpen(true)}>
        {t('finance.lessons.correct')}
      </Button>
    );
  return (
    <form
      className="flex w-full flex-col gap-2 rounded-md border border-border p-3"
      onSubmit={(e) => {
        e.preventDefault();
        const value = Number(quantity);
        if (!Number.isInteger(value) || value === 0 || !reason.trim()) return;
        adjust.mutate({ packageId: pkg.id, quantity: value, reason: reason.trim() }, { onSuccess: () => setOpen(false) });
      }}
    >
      <FormField label={t('finance.lessons.quantity')} type="number" step={1} value={quantity} hint={t('finance.lessons.quantityHint')} onChange={(e) => setQuantity(e.target.value)} />
      <FormField label={t('finance.reason')} maxLength={REASON_MAX} value={reason} onChange={(e) => setReason(e.target.value)} />
      {adjust.error && <Alert variant="error">{errorMessage(adjust.error, t)}</Alert>}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={adjust.isPending || !reason.trim()}>
          {t('finance.saveCorrection')}
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => setOpen(false)}>
          {t('common.cancel')}
        </Button>
      </div>
    </form>
  );
}

function AssignPackageForm({ workspaceId, studentId, finance, onDone }: { workspaceId: string; studentId: string; finance: StudentFinance; onDone: () => void }) {
  const { t, i18n } = useTranslation();
  const offers = useOffers(workspaceId);
  const assign = useAssignPackage(workspaceId, studentId);
  const { workspaceCurrency, currencies } = useCurrencies(workspaceId, finance);
  const [offerId, setOfferId] = useState('');
  const [custom, setCustom] = useState({ title: '', lessons: '4', price: '', currency: '' });
  const [invalid, setInvalid] = useState(false);
  const currency = custom.currency || workspaceCurrency;
  const language = i18n.resolvedLanguage ?? 'mk';
  const active = (offers.data ?? []).filter((o) => o.isActive);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (offerId) {
      assign.mutate({ offerId }, { onSuccess: onDone });
      return;
    }
    const lessons = Number(custom.lessons);
    const price = parseMoney(custom.price, currency);
    if (!custom.title.trim() || !Number.isInteger(lessons) || lessons < 1 || lessons > 500 || price === null) {
      setInvalid(true);
      return;
    }
    assign.mutate({ title: custom.title.trim(), lessonCount: lessons, priceMinor: price, currency }, { onSuccess: onDone });
  };

  return (
    <form noValidate className="flex flex-col gap-3 rounded-md border border-border p-3" onSubmit={submit}>
      <div className="flex flex-col gap-2">
        <Label htmlFor="package-offer">{t('finance.package.fromOffer')}</Label>
        <select id="package-offer" className={field} value={offerId} onChange={(e) => setOfferId(e.target.value)}>
          <option value="">{t('finance.package.custom')}</option>
          {active.map((o) => (
            <option key={o.id} value={o.id}>
              {o.title} · {formatMoney(num(o.priceMinor) ?? 0, o.currency, language)}
              {o.kind === 'Package' ? ` · ${t('finance.package.lessons', { count: num(o.lessonCount) ?? 1 })}` : ''}
            </option>
          ))}
        </select>
        {offerId && <p className="text-xs text-muted-foreground">{t('finance.package.snapshot')}</p>}
      </div>
      {!offerId && (
        <div className="grid gap-3 sm:grid-cols-2">
          <FormField label={t('finance.package.name')} maxLength={160} value={custom.title} error={invalid && !custom.title.trim() ? t('finance.validation.required') : undefined} onChange={(e) => setCustom({ ...custom, title: e.target.value })} />
          <FormField label={t('finance.package.lessonCount')} type="number" min={1} max={500} value={custom.lessons} onChange={(e) => setCustom({ ...custom, lessons: e.target.value })} />
          <FormField
            label={t('finance.package.price')}
            inputMode="decimal"
            value={custom.price}
            error={invalid && parseMoney(custom.price, currency) === null ? t('finance.validation.amount') : undefined}
            onChange={(e) => setCustom({ ...custom, price: e.target.value })}
          />
          <CurrencySelect id="package-currency" currencies={currencies} value={currency} onChange={(c) => setCustom({ ...custom, currency: c })} />
        </div>
      )}
      {assign.error && <Alert variant="error">{errorMessage(assign.error, t)}</Alert>}
      <Button type="submit" className="self-start" disabled={assign.isPending}>
        {t('finance.package.assign')}
      </Button>
    </form>
  );
}

function CurrencySelect({ id, currencies, value, onChange }: { id: string; currencies: string[]; value: string; onChange: (currency: string) => void }) {
  const { t } = useTranslation();
  const [typed, setTyped] = useState(!currencies.includes(value));
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{t('finance.currency')}</Label>
      {typed ? (
        <input id={id} className={field} maxLength={3} value={value} onChange={(e) => onChange(e.target.value.toUpperCase())} />
      ) : (
        <select
          id={id}
          className={field}
          value={value}
          onChange={(e) => {
            if (e.target.value === '*') {
              setTyped(true);
              onChange('');
            } else onChange(e.target.value);
          }}
        >
          {currencies.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
          <option value="*">{t('finance.otherCurrency')}</option>
        </select>
      )}
    </div>
  );
}

function PaymentForm({ workspaceId, studentId, finance, onDone }: { workspaceId: string; studentId: string; finance: StudentFinance; onDone: () => void }) {
  const { t } = useTranslation();
  const pay = useRecordPayment(workspaceId, studentId);
  const { workspaceCurrency, currencies, timeZone } = useCurrencies(workspaceId, finance);
  const owed = finance.balances.find((b) => (num(b.outstandingMinor) ?? 0) > 0)?.currency;
  const [values, setValues] = useState({ amount: '', currency: owed ?? workspaceCurrency, paidOn: today(timeZone), method: 'Cash' as PaymentMethod, note: '', packageId: '' });
  const [invalid, setInvalid] = useState(false);
  const packages = finance.packages.filter((p) => p.currency === values.currency);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const amount = parseMoney(values.amount, values.currency);
    if (amount === null || amount <= 0 || !values.paidOn) {
      setInvalid(true);
      return;
    }
    pay.mutate(
      { amountMinor: amount, currency: values.currency, paidOn: values.paidOn, method: values.method, note: values.note.trim() || null, packageId: values.packageId || null },
      { onSuccess: onDone },
    );
  };

  return (
    <form noValidate className="grid gap-3 rounded-md border border-border p-3 sm:grid-cols-2" onSubmit={submit}>
      <p className="text-xs text-muted-foreground sm:col-span-2">{t('finance.payment.outside')}</p>
      <FormField
        label={t('finance.payment.amount')}
        inputMode="decimal"
        value={values.amount}
        error={invalid && !((parseMoney(values.amount, values.currency) ?? 0) > 0) ? t('finance.validation.amount') : undefined}
        onChange={(e) => setValues({ ...values, amount: e.target.value })}
      />
      <CurrencySelect id="payment-currency" currencies={currencies} value={values.currency} onChange={(currency) => setValues({ ...values, currency, packageId: '' })} />
      <FormField label={t('finance.payment.paidOn')} type="date" value={values.paidOn} onChange={(e) => setValues({ ...values, paidOn: e.target.value })} />
      <div className="flex flex-col gap-2">
        <Label htmlFor="payment-method">{t('finance.payment.method')}</Label>
        <select id="payment-method" className={field} value={values.method} onChange={(e) => setValues({ ...values, method: e.target.value as PaymentMethod })}>
          {PAYMENT_METHODS.map((m) => (
            <option key={m} value={m}>
              {t(`finance.methods.${m}`)}
            </option>
          ))}
        </select>
      </div>
      {packages.length > 0 && (
        <div className="flex flex-col gap-2">
          <Label htmlFor="payment-package">{t('finance.payment.forPackage')}</Label>
          <select id="payment-package" className={field} value={values.packageId} onChange={(e) => setValues({ ...values, packageId: e.target.value })}>
            <option value="">{t('finance.payment.noPackage')}</option>
            {packages.map((p) => (
              <option key={p.id} value={p.id}>
                {p.title}
              </option>
            ))}
          </select>
        </div>
      )}
      <FormField label={t('finance.payment.note')} maxLength={500} value={values.note} onChange={(e) => setValues({ ...values, note: e.target.value })} />
      {pay.error && (
        <Alert variant="error" className="sm:col-span-2">
          {errorMessage(pay.error, t)}
        </Alert>
      )}
      <Button type="submit" className="self-start" disabled={pay.isPending}>
        {t('finance.payment.record')}
      </Button>
    </form>
  );
}

function AdjustmentForm({ workspaceId, studentId, finance, onDone }: { workspaceId: string; studentId: string; finance: StudentFinance; onDone: () => void }) {
  const { t } = useTranslation();
  const adjust = useAdjustBalance(workspaceId, studentId);
  const { workspaceCurrency, currencies } = useCurrencies(workspaceId, finance);
  const [values, setValues] = useState({ amount: '', currency: finance.balances[0]?.currency ?? workspaceCurrency, direction: 'less' as 'less' | 'more', reason: '' });
  const [invalid, setInvalid] = useState(false);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const amount = parseMoney(values.amount, values.currency);
    if (amount === null || amount === 0 || !values.reason.trim()) {
      setInvalid(true);
      return;
    }
    adjust.mutate({ amountMinor: values.direction === 'less' ? -amount : amount, currency: values.currency, reason: values.reason.trim() }, { onSuccess: onDone });
  };

  return (
    <form noValidate className="grid gap-3 rounded-md border border-border p-3 sm:grid-cols-2" onSubmit={submit}>
      <div className="flex flex-col gap-2">
        <Label htmlFor="adjustment-direction">{t('finance.adjustment.direction')}</Label>
        <select id="adjustment-direction" className={field} value={values.direction} onChange={(e) => setValues({ ...values, direction: e.target.value as 'less' | 'more' })}>
          <option value="less">{t('finance.adjustment.less')}</option>
          <option value="more">{t('finance.adjustment.more')}</option>
        </select>
      </div>
      <FormField
        label={t('finance.adjustment.amount')}
        inputMode="decimal"
        value={values.amount}
        error={invalid && !((parseMoney(values.amount, values.currency) ?? 0) > 0) ? t('finance.validation.amount') : undefined}
        onChange={(e) => setValues({ ...values, amount: e.target.value })}
      />
      <CurrencySelect id="adjustment-currency" currencies={currencies} value={values.currency} onChange={(currency) => setValues({ ...values, currency })} />
      <FormField label={t('finance.reason')} maxLength={REASON_MAX} value={values.reason} error={invalid && !values.reason.trim() ? t('finance.validation.reason') : undefined} onChange={(e) => setValues({ ...values, reason: e.target.value })} />
      {adjust.error && (
        <Alert variant="error" className="sm:col-span-2">
          {errorMessage(adjust.error, t)}
        </Alert>
      )}
      <Button type="submit" className="self-start" disabled={adjust.isPending}>
        {t('finance.saveCorrection')}
      </Button>
    </form>
  );
}

function History({ workspaceId, studentId, entries }: { workspaceId: string; studentId: string; entries: FinanceEntry[] }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  const reverse = useReverse(workspaceId, studentId);
  const [reversing, setReversing] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  if (entries.length === 0) return null;

  const detail = (e: FinanceEntry) => {
    const amount = e.amountMinor !== null && e.amountMinor !== undefined && e.currency ? formatMoney(num(e.amountMinor) ?? 0, e.currency, language) : null;
    const lessons = num(e.lessons);
    return [amount, lessons !== null && e.kind !== 'PackageAssigned' ? t('finance.history.lessons', { count: lessons }) : null, e.packageTitle, e.lessonTitle, e.method ? t(`finance.methods.${e.method}`) : null, e.note]
      .filter(Boolean)
      .join(' · ');
  };

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('finance.history.title')}</h3>
      <ul aria-label={t('finance.history.title')} className="flex flex-col divide-y divide-border">
        {entries.map((e) => (
          <li key={e.id} className="flex flex-col gap-2 py-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex flex-col">
                <span className="text-sm font-medium">{t(`finance.history.kinds.${e.kind}`)}</span>
                <span className="text-xs text-muted-foreground">
                  {e.paidOn ? formatDate(`${e.paidOn}T12:00:00Z`, language, 'UTC') : formatDateTime(e.occurredAtUtc, language, viewerTimeZone())} · {detail(e)}
                </span>
              </div>
              {e.reversible && reversing !== e.id && (
                <Button type="button" size="sm" variant="ghost" onClick={() => { setReversing(e.id); setReason(''); }}>
                  {t('finance.history.reverse')}
                </Button>
              )}
            </div>
            {reversing === e.id && (
              <form
                className="flex flex-col gap-2 sm:flex-row sm:items-end"
                onSubmit={(event) => {
                  event.preventDefault();
                  if (reason.trim()) reverse.mutate({ entry: e, reason: reason.trim() }, { onSuccess: () => setReversing(null) });
                }}
              >
                <div className="flex-1">
                  <FormField label={t('finance.history.reverseReason')} maxLength={REASON_MAX} value={reason} onChange={(event) => setReason(event.target.value)} />
                </div>
                <Button type="submit" size="sm" variant="destructive" disabled={reverse.isPending || !reason.trim()}>
                  {t('finance.history.reverseConfirm')}
                </Button>
                <Button type="button" size="sm" variant="ghost" onClick={() => setReversing(null)}>
                  {t('common.cancel')}
                </Button>
              </form>
            )}
          </li>
        ))}
      </ul>
      {reverse.error && (
        <Alert variant="error">{reverse.error instanceof ApiError && reverse.error.code === 'CONCURRENCY_CONFLICT' ? t('finance.history.alreadyReversed') : errorMessage(reverse.error, t)}</Alert>
      )}
    </div>
  );
}

/** `/app/w/:workspaceId/finance`: who owes what (per currency), lessons left, and a period report. */
export function FinancePage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const language = i18n.resolvedLanguage ?? 'mk';
  const overview = useFinanceOverview(workspaceId);
  const workspace = useWorkspace(workspaceId);
  const zone = workspace.data?.timeZone ?? 'Europe/Skopje';
  const now = today(zone);
  const [period, setPeriod] = useState({ from: `${now.slice(0, 8)}01`, to: now });
  const [asked, setAsked] = useState(period);
  const report = useFinanceReport(workspaceId, asked.from, asked.to);

  if (overview.isError) return overview.error instanceof ApiError && overview.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(overview.error, t)}</Alert>;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('finance.pageTitle')}</h1>
        <p className="text-sm text-muted-foreground">{t('finance.pageLead')}</p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('finance.overview.title')}</CardTitle>
        </CardHeader>
        <CardContent>
          {!overview.data ? (
            <output className="block text-sm">{t('common.loading')}</output>
          ) : overview.data.students.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('finance.overview.empty')}</p>
          ) : (
            <ul aria-label={t('finance.overview.title')} className="flex flex-col divide-y divide-border">
              {overview.data.students.map((s) => (
                <li key={s.studentId} className="flex flex-wrap items-center justify-between gap-2 py-2">
                  <Link className="text-sm font-medium hover:underline" to={`/app/w/${workspaceId}/students/${s.studentId}`}>
                    {s.name}
                    {s.archived && <span className="ml-2 text-xs text-muted-foreground">({t('students.archived')})</span>}
                  </Link>
                  <span className="text-sm text-muted-foreground">
                    {[
                      ...s.balances
                        .filter((b) => (num(b.outstandingMinor) ?? 0) !== 0)
                        .map((b) => ((num(b.outstandingMinor) ?? 0) > 0 ? t('finance.owes', { amount: formatMoney(num(b.outstandingMinor) ?? 0, b.currency, language) }) : t('finance.credit', { amount: formatMoney(-(num(b.outstandingMinor) ?? 0), b.currency, language) }))),
                      t('finance.overview.remaining', { count: num(s.remainingLessons) ?? 0 }),
                      (num(s.unpaidLessons) ?? 0) > 0 ? t('finance.unpaidLessons', { count: num(s.unpaidLessons) ?? 0 }) : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('finance.report.title')}</CardTitle>
          <CardDescription>{t('finance.report.lead')}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <form
            className="flex flex-col gap-3 sm:flex-row sm:items-end"
            onSubmit={(e) => {
              e.preventDefault();
              setAsked(period);
            }}
          >
            <FormField label={t('finance.report.from')} type="date" value={period.from} onChange={(e) => setPeriod({ ...period, from: e.target.value })} />
            <FormField label={t('finance.report.to')} type="date" value={period.to} onChange={(e) => setPeriod({ ...period, to: e.target.value })} />
            <Button type="submit">{t('finance.report.show')}</Button>
          </form>
          {report.isError ? (
            <Alert variant="error">{errorMessage(report.error, t)}</Alert>
          ) : !report.data ? (
            <output className="block text-sm">{t('common.loading')}</output>
          ) : (
            <>
              {report.data.currencies.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t('finance.report.empty')}</p>
              ) : (
                <table className="w-full text-sm">
                  <caption className="sr-only">{t('finance.report.title')}</caption>
                  <thead>
                    <tr className="text-left text-muted-foreground">
                      <th className="py-1 font-medium">{t('finance.currency')}</th>
                      <th className="py-1 font-medium">{t('finance.report.packages')}</th>
                      <th className="py-1 font-medium">{t('finance.report.charged')}</th>
                      <th className="py-1 font-medium">{t('finance.report.received')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.data.currencies.map((c) => (
                      <tr key={c.currency} className="border-t border-border">
                        <td className="py-1">{c.currency}</td>
                        <td className="py-1">{num(c.packagesSold) ?? 0}</td>
                        <td className="py-1">{formatMoney(num(c.chargedMinor) ?? 0, c.currency, language)}</td>
                        <td className="py-1">{formatMoney(num(c.receivedMinor) ?? 0, c.currency, language)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              <p className="text-sm">{t('finance.report.lessons', { consumed: num(report.data.lessonsConsumed) ?? 0, unpaid: num(report.data.unpaidLessons) ?? 0 })}</p>
            </>
          )}
        </CardContent>
      </Card>
    </section>
  );
}
