import { ArrowDown, ArrowUp, Pencil, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { formatMoney } from '../../../lib/format.ts';
import { num, useArchiveOffer, useOffers, useReorderOffers, type Offer } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

export function OffersPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const offers = useOffers(workspaceId);
  const catalog = useCatalog();
  const reorder = useReorderOffers(workspaceId);
  const archive = useArchiveOffer(workspaceId);
  const [confirming, setConfirming] = useState<string | null>(null);
  const language = i18n.resolvedLanguage === 'en' ? 'en' : 'mk';

  if (offers.isPending) return <output className="block">{t('common.loading')}</output>;
  if (offers.isError) return offers.error instanceof ApiError && offers.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(offers.error, t)}</Alert>;

  const list = offers.data;
  const subjectName = (offer: Offer) => {
    const subject = catalog.data?.subjects.find((s) => s.id === offer.subjectId);
    return subject ? (language === 'en' ? subject.nameEn : subject.nameMk) : '';
  };
  const move = (index: number, delta: -1 | 1) => {
    const ids = list.map((o) => o.id);
    const [moved] = ids.splice(index, 1);
    ids.splice(index + delta, 0, moved!);
    reorder.mutate(ids);
  };
  const error = reorder.error ?? archive.error;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('offers.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('offers.lead')}</p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/offers/new`}>
          <Plus className="h-4 w-4" aria-hidden />
          {t('offers.create')}
        </Link>
      </div>
      {error && <Alert variant="error">{errorMessage(error, t)}</Alert>}
      {list.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('offers.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ol aria-label={t('offers.title')} className="flex flex-col gap-3">
          {list.map((offer, index) => {
            const lessons = num(offer.lessonCount);
            const seats = num(offer.seats);
            return (
              <li key={offer.id}>
                <Card>
                  <CardHeader className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                    <div className="flex flex-col gap-1">
                      <CardTitle className="text-lg">{offer.title}</CardTitle>
                      <CardDescription>
                        {[
                          t(`offers.kinds.${offer.kind}`),
                          subjectName(offer),
                          t('offers.minutes', { count: num(offer.durationMinutes) ?? 0 }),
                          lessons !== null ? t('offers.lessons', { count: lessons }) : null,
                          seats !== null ? t('offers.seatsCount', { count: seats }) : null,
                        ]
                          .filter(Boolean)
                          .join(' · ')}
                      </CardDescription>
                    </div>
                    <p className="text-lg font-semibold" data-testid="offer-price">
                      {formatMoney(num(offer.priceMinor) ?? 0, offer.currency, language)}
                    </p>
                  </CardHeader>
                  <CardContent className="flex flex-col gap-3">
                    <div className="flex flex-wrap gap-2 text-xs">
                      <span className="rounded-full bg-muted px-2 py-1">{offer.isActive ? t('offers.active') : t('offers.inactive')}</span>
                      <span className="rounded-full bg-muted px-2 py-1">{offer.showOnPublicSite ? t('offers.onSite') : t('offers.hiddenFromSite')}</span>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <Link className={buttonVariants({ variant: 'outline', size: 'sm' })} to={`/app/w/${workspaceId}/offers/${offer.id}`}>
                        <Pencil className="h-4 w-4" aria-hidden />
                        {t('offers.edit', { title: offer.title })}
                      </Link>
                      <Button variant="ghost" size="sm" disabled={index === 0 || reorder.isPending} aria-label={t('offers.moveUp', { title: offer.title })} onClick={() => move(index, -1)}>
                        <ArrowUp className="h-4 w-4" aria-hidden />
                      </Button>
                      <Button variant="ghost" size="sm" disabled={index === list.length - 1 || reorder.isPending} aria-label={t('offers.moveDown', { title: offer.title })} onClick={() => move(index, 1)}>
                        <ArrowDown className="h-4 w-4" aria-hidden />
                      </Button>
                      {confirming === offer.id ? (
                        <span className="flex flex-wrap items-center gap-2 text-sm">
                          {t('offers.archiveConfirm')}
                          <Button variant="outline" size="sm" disabled={archive.isPending} onClick={() => archive.mutate(offer.id, { onSettled: () => setConfirming(null) })}>
                            {t('offers.archiveYes')}
                          </Button>
                          <Button variant="ghost" size="sm" onClick={() => setConfirming(null)}>
                            {t('offers.archiveNo')}
                          </Button>
                        </span>
                      ) : (
                        <Button variant="ghost" size="sm" onClick={() => setConfirming(offer.id)}>
                          {t('offers.archive', { title: offer.title })}
                        </Button>
                      )}
                    </div>
                  </CardContent>
                </Card>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}
