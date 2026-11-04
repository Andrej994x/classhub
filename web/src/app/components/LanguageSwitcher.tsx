import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { meQueryKey, saveAccountLanguage, type Me } from '../../auth/session.ts';
import { isLanguage, rememberLanguage, SUPPORTED_LANGUAGES } from '../../i18n/index.ts';

/**
 * Switches the UI language (mk/en) and, when signed in, saves it on the account (emails use it too).
 * The teaching content language is independent of this choice.
 */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
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
            const account = queryClient.getQueryData<Me | null>(meQueryKey);
            if (account) {
              // Best effort: the UI already switched; the account keeps its old value if this fails.
              saveAccountLanguage(next)
                .then((me) => {
                  // Apply only while the same account is still signed in: a late answer must not bring back a
                  // session that ended (sign-out) or replace another account that signed in meanwhile.
                  const signedIn = queryClient.getQueryData<Me | null>(meQueryKey);
                  if (me && signedIn && signedIn.id === account.id && me.id === account.id) queryClient.setQueryData(meQueryKey, me);
                })
                .catch(() => undefined);
            }
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
