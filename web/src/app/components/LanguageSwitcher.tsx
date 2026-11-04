import { useTranslation } from 'react-i18next';
import { isLanguage, rememberLanguage, SUPPORTED_LANGUAGES } from '../../i18n/index.ts';

/** Switches the UI language (mk/en). The teaching content language is independent of this choice. */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const current = isLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'mk';

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="sr-only">{t('language.label')}</span>
      <select
        className="h-9 rounded-md border border-input bg-background px-2 text-sm"
        value={current}
        aria-label={t('language.label')}
        onChange={(event) => {
          const next = event.target.value;
          if (isLanguage(next)) {
            rememberLanguage(next);
            void i18n.changeLanguage(next);
          }
        }}
      >
        {SUPPORTED_LANGUAGES.map((language) => (
          <option key={language} value={language}>
            {t(`language.${language}`)}
          </option>
        ))}
      </select>
    </label>
  );
}
