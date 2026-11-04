import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { i18n } from '../i18n/index.ts';
import { renderRoute } from '../test/render.tsx';

describe('routes and layouts', () => {
  it('renders the home page in Macedonian by default', async () => {
    await renderRoute('/');
    expect(screen.getByRole('heading', { level: 1, name: 'Добредојдовте во ClassHub' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Регистрација' })).toHaveAttribute('href', '/register');
  });

  it('renders the home page in English', async () => {
    await renderRoute('/', 'en');
    expect(screen.getByRole('heading', { level: 1, name: 'Welcome to ClassHub' })).toBeInTheDocument();
  });

  it.each([
    ['/app', 'Почетна', 'Наставнички простор'],
    ['/student', 'Мои часови', 'Ученички простор'],
    ['/admin', 'Администрација', 'Администрација на платформата'],
  ])('renders the %s area shell with its navigation', async (path, title, area) => {
    await renderRoute(path);
    expect(screen.getByRole('heading', { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getAllByText(new RegExp(area)).length).toBeGreaterThan(0);
    expect(screen.getByRole('navigation', { name: 'Главна навигација' })).toBeInTheDocument();
  });

  it('marks the active navigation item', async () => {
    await renderRoute('/app');
    const nav = screen.getByRole('navigation', { name: 'Главна навигација' });
    expect(within(nav).getByRole('link', { name: 'Почетна' })).toHaveClass('text-primary');
  });

  it('opens the mobile navigation drawer and closes it after navigating', async () => {
    const user = userEvent.setup();
    await renderRoute('/app');

    await user.click(screen.getByRole('button', { name: 'Отвори мени' }));
    const drawer = await screen.findByRole('dialog', { name: 'Наставнички простор' });
    await user.click(within(drawer).getByRole('link', { name: 'Поставки' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('switches the UI language and remembers it', async () => {
    const user = userEvent.setup();
    await renderRoute('/');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');

    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome to ClassHub' })).toBeInTheDocument();
    expect(window.localStorage.getItem('classhub.language')).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    await i18n.changeLanguage('mk');
  });

  it('renders the auth shell at /login and switches its language', async () => {
    const user = userEvent.setup();
    await renderRoute('/login');

    expect(screen.getByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');
    await i18n.changeLanguage('mk');
  });

  it.each(['/register', '/verify-email', '/forgot-password', '/reset-password'])('renders %s inside the auth shell', async (path) => {
    await renderRoute(path);
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Јазик' })).toBeInTheDocument();
    expect(screen.queryByText(/Страницата не е пронајдена/)).not.toBeInTheDocument();
  });

  it('renders a localized not-found page for unknown paths', async () => {
    await renderRoute('/does-not-exist');
    expect(screen.getByRole('heading', { name: /Страницата не е пронајдена/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Назад кон почетната страница' })).toHaveAttribute('href', '/');
  });
});
