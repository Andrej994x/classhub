import { MonitorSmartphone } from 'lucide-react';
import { useTranslation } from 'react-i18next';

/** On phone-sized screens (below Tailwind's sm breakpoint): the boards work, but are best on a tablet or computer (R3-P7). */
export function LargeScreenNote() {
  const { t } = useTranslation();
  return (
    <p className="flex items-start gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm sm:hidden" data-testid="large-screen-note">
      <MonitorSmartphone className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      {t('pwa.largeScreen')}
    </p>
  );
}
