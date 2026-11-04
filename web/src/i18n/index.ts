import { createInstance } from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import mk from './locales/mk.json';

export const SUPPORTED_LANGUAGES = ['mk', 'en'] as const;
export type Language = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: Language = 'mk';
const STORAGE_KEY = 'classhub.language';

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

/** The saved choice, else the browser language if supported, else Macedonian. */
export function detectLanguage(): Language {
  try {
    const saved = window.localStorage.getItem(STORAGE_KEY);
    if (isLanguage(saved)) return saved;
  } catch {
    // Storage can be unavailable (privacy mode); fall through.
  }
  const browser = typeof navigator !== 'undefined' ? navigator.language.slice(0, 2).toLowerCase() : '';
  return isLanguage(browser) ? browser : DEFAULT_LANGUAGE;
}

export function rememberLanguage(language: Language) {
  try {
    window.localStorage.setItem(STORAGE_KEY, language);
  } catch {
    // Not critical: the choice just isn't remembered.
  }
}

export const i18n = createInstance();

void i18n.use(initReactI18next).init({
  resources: { mk: { translation: mk }, en: { translation: en } },
  lng: typeof window === 'undefined' ? DEFAULT_LANGUAGE : detectLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: [...SUPPORTED_LANGUAGES],
  interpolation: { escapeValue: false }, // React escapes
  returnNull: false,
});

i18n.on('languageChanged', (language) => {
  if (typeof document !== 'undefined') document.documentElement.lang = language;
});
