import { Download, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '../../components/ui/button.tsx';
import { useInstallPrompt } from '../../pwa/pwa.ts';

/** "Install the app", shown only when the browser offers it; can be dismissed for good (R3-P7). */
export function InstallApp() {
  const { t } = useTranslation();
  const prompt = useInstallPrompt();
  if (!prompt.available) return null;
  return (
    <span className="flex items-center gap-1">
      <Button type="button" size="sm" variant="outline" onClick={() => void prompt.install()}>
        <Download className="h-4 w-4" aria-hidden />
        <span className="sr-only sm:not-sr-only">{t('pwa.install')}</span>
      </Button>
      <Button type="button" size="icon" variant="ghost" aria-label={t('pwa.dismiss')} onClick={prompt.dismiss}>
        <X className="h-4 w-4" aria-hidden />
      </Button>
    </span>
  );
}
