import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { PAYMENT_CURRENCIES, PAYMENT_METHODS, useActivatePro, useWorkspaceSubscription } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDate, formatDateTime, formatMoney, parseMoney } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const today = () => new Date().toISOString().slice(0, 10);

/** A workspace's plan, its history with the recorded payments, and Pro activation or extension (D-30, D-39). */
export function SubscriptionCard({ workspaceId }: { workspaceId: string }) {
  const { t, i18n } = useTranslation();
  const subscription = useWorkspaceSubscription(workspaceId);
  const language = i18n.resolvedLanguage ?? 'mk';

  if (subscription.isError) return <Alert variant="error">{errorMessage(subscription.error, t)}</Alert>;
  if (!subscription.data) return <output className="block">{t('common.loading')}</output>;
  const data = subscription.data;
  const pro = data.planCode === 'pro';

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{t('admin.subscription.title')}</CardTitle>
        <CardDescription>
          {pro && data.paidUntilUtc
            ? t('admin.subscription.proUntil', { date: formatDate(data.paidUntilUtc, language, browserTimeZone) })
            : t('admin.subscription.onFree')}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <ActivateForm workspaceId={workspaceId} extend={pro} />
        <div className="flex flex-col gap-2">
          <h3 className="text-sm font-semibold">{t('admin.subscription.history')}</h3>
          {data.history.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('admin.subscription.noHistory')}</p>
          ) : (
            <ol aria-label={t('admin.subscription.history')} className="flex flex-col gap-2 text-sm">
              {data.history.map((event) => (
                <li key={event.id} className="flex flex-col gap-0.5 rounded-md border border-border p-3">
                  <span className="font-medium">
                    {t(`admin.subscription.kinds.${event.kind}`)} · {formatDateTime(event.occurredAtUtc, language, browserTimeZone)}
                  </span>
                  {event.periodEndUtc && (
                    <span className="text-muted-foreground">{t('admin.subscription.until', { date: formatDate(event.periodEndUtc, language, browserTimeZone) })}</span>
                  )}
                  {event.amountMinor !== null && event.amountMinor !== undefined && event.currency && (
                    <span className="text-muted-foreground">
                      {formatMoney(num(event.amountMinor) ?? 0, event.currency, language)} · {t(`admin.subscription.methods.${event.paymentMethod}`)}
                      {event.paymentReference ? ` · ${event.paymentReference}` : ''}
                    </span>
                  )}
                  {event.note && <span className="text-muted-foreground">{event.note}</span>}
                </li>
              ))}
            </ol>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function ActivateForm({ workspaceId, extend }: { workspaceId: string; extend: boolean }) {
  const { t } = useTranslation();
  const activate = useActivatePro(workspaceId);
  const [months, setMonths] = useState('1');
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('MKD');
  const [method, setMethod] = useState<string>('bank-transfer');
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState(today());
  const [note, setNote] = useState('');
  const [invalid, setInvalid] = useState(false);

  const complimentary = method === 'complimentary';
  const amountMinor = complimentary ? 0 : parseMoney(amount, currency);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (amountMinor === null || (!complimentary && amountMinor <= 0) || !paidOn) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    activate.mutate(
      { months: Number(months), amountMinor, currency, paymentMethod: method, paymentReference: reference.trim() || null, paidOn, note: note.trim() || null },
      {
        onSuccess: () => {
          setAmount('');
          setReference('');
          setNote('');
        },
      },
    );
  };

  return (
    <form noValidate aria-label={extend ? t('admin.subscription.extend') : t('admin.subscription.activate')} onSubmit={submit} className="grid gap-3 sm:grid-cols-2">
      <div className="flex flex-col gap-1">
        <Label htmlFor="pro-months">{t('admin.subscription.months')}</Label>
        <select id="pro-months" className={field} value={months} onChange={(e) => setMonths(e.target.value)}>
          {[1, 3, 6, 12, 24].map((m) => (
            <option key={m} value={m}>
              {t('admin.subscription.monthCount', { count: m })}
            </option>
          ))}
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor="pro-method">{t('admin.subscription.method')}</Label>
        <select id="pro-method" className={field} value={method} onChange={(e) => setMethod(e.target.value)}>
          {PAYMENT_METHODS.map((m) => (
            <option key={m} value={m}>
              {t(`admin.subscription.methods.${m}`)}
            </option>
          ))}
        </select>
      </div>
      {!complimentary && (
        <>
          <div className="flex flex-col gap-1">
            <Label htmlFor="pro-amount">{t('admin.subscription.amount')}</Label>
            <input
              id="pro-amount"
              inputMode="decimal"
              className={field}
              value={amount}
              aria-invalid={invalid && (amountMinor === null || amountMinor <= 0) ? true : undefined}
              onChange={(e) => setAmount(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="pro-currency">{t('admin.subscription.currency')}</Label>
            <select id="pro-currency" className={field} value={currency} onChange={(e) => setCurrency(e.target.value)}>
              {PAYMENT_CURRENCIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
        </>
      )}
      <div className="flex flex-col gap-1">
        <Label htmlFor="pro-paid-on">{t('admin.subscription.paidOn')}</Label>
        <input id="pro-paid-on" type="date" className={field} value={paidOn} max={today()} onChange={(e) => setPaidOn(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor="pro-reference">{t('admin.subscription.reference')}</Label>
        <input id="pro-reference" className={field} maxLength={100} value={reference} onChange={(e) => setReference(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1 sm:col-span-2">
        <Label htmlFor="pro-note">{t('admin.subscription.note')}</Label>
        <input id="pro-note" className={field} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
      {invalid && (
        <Alert variant="error" className="sm:col-span-2">
          {t('admin.subscription.amountInvalid')}
        </Alert>
      )}
      {activate.error && (
        <Alert variant="error" className="sm:col-span-2">
          {errorMessage(activate.error, t)}
        </Alert>
      )}
      {activate.isSuccess && (
        <Alert variant="success" className="sm:col-span-2">
          {t('admin.subscription.saved')}
        </Alert>
      )}
      <Button type="submit" className="self-start sm:col-span-2 sm:justify-self-start" disabled={activate.isPending}>
        {activate.isPending ? t('common.loading') : extend ? t('admin.subscription.extend') : t('admin.subscription.activate')}
      </Button>
    </form>
  );
}
