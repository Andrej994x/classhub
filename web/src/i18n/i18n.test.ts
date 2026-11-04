import { afterEach, describe, expect, it, vi } from 'vitest';

describe('i18n initialization', () => {
  afterEach(() => {
    vi.resetModules();
    window.localStorage.clear();
    document.documentElement.lang = '';
  });

  it('sets the document language from the remembered choice at startup', async () => {
    window.localStorage.setItem('classhub.language', 'en');
    document.documentElement.lang = 'mk'; // as in index.html

    const { i18n } = await import('./index.ts');

    await vi.waitFor(() => expect(i18n.resolvedLanguage).toBe('en'));
    expect(document.documentElement.lang).toBe('en');
  });

  it('defaults to Macedonian', async () => {
    Object.defineProperty(window.navigator, 'language', { value: 'de-DE', configurable: true });
    const { i18n } = await import('./index.ts');

    await vi.waitFor(() => expect(i18n.resolvedLanguage).toBe('mk'));
    expect(document.documentElement.lang).toBe('mk');
  });
});
