/// <reference lib="dom" />
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { crc32, deflateSync } from 'node:zlib';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export type Language = 'mk' | 'en';
type Messages = Record<string, unknown>;

const localesDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'i18n', 'locales');
const messages: Record<Language, Messages> = {
  mk: JSON.parse(readFileSync(join(localesDir, 'mk.json'), 'utf8')) as Messages,
  en: JSON.parse(readFileSync(join(localesDir, 'en.json'), 'utf8')) as Messages,
};

/** The UI text for a key, from the same locale files the app uses (so tests follow wording changes). */
export function text(language: Language, key: string, values: Record<string, string | number> = {}): string {
  const value = key.split('.').reduce<unknown>((node, part) => (node as Messages | undefined)?.[part], messages[language]);
  if (typeof value !== 'string') throw new Error(`Missing ${language} text: ${key}`);
  return value.replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(values[name] ?? ''));
}

export const PASSWORD = 'correct horse battery staple';
export const NEW_PASSWORD = 'violet lantern harbor 42';

export const uniqueEmail = (label: string) => `e2e-${label}-${randomBytes(5).toString('hex')}@classhub.test`;

/** Starts the SPA in a language (as a returning visitor who chose it). */
export async function useLanguage(page: Page, language: Language) {
  await page.addInitScript((value) => window.localStorage.setItem('classhub.language', value), language);
}

/** Opens the newest link to `path` that the dev mailbox received for `email`. */
export async function openEmailLink(page: Page, language: Language, email: string, path: '/verify-email' | '/reset-password') {
  await page.goto(`/dev/mailbox?to=${encodeURIComponent(email)}`);
  await expect(page.getByRole('heading', { level: 1, name: text(language, 'devMailbox.title') })).toBeVisible();
  const link = page.getByRole('list', { name: text(language, 'devMailbox.messages') }).getByRole('link', { name: new RegExp(`${path}#`) }).first();
  await expect(link).toBeVisible();
  await link.click();
}

export async function register(page: Page, language: Language, email: string, password = PASSWORD) {
  await page.goto('/register');
  await page.getByLabel(text(language, 'auth.fields.email')).fill(email);
  await page.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(password);
  await page.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(password);
  await page.getByRole('button', { name: text(language, 'auth.register.submit') }).click();
  await expect(page.getByRole('heading', { level: 1, name: text(language, 'auth.register.sentTitle') })).toBeVisible();
}

export async function signIn(page: Page, language: Language, email: string, password = PASSWORD) {
  await page.getByLabel(text(language, 'auth.fields.email')).fill(email);
  await page.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(password);
  await page.getByRole('button', { name: text(language, 'auth.login.submit') }).click();
}

/** Registers and signs in through the API (setup for flows that are not about registration). */
export async function teacherViaApi(request: APIRequestContext, email: string) {
  const token = async () => ((await (await request.get('/api/auth/antiforgery')).json()) as { requestToken: string }).requestToken;
  expect((await request.post('/api/auth/register', { data: { email, password: PASSWORD, locale: 'mk' }, headers: { 'X-XSRF-TOKEN': await token() } })).status()).toBe(202);
  expect((await request.post('/api/auth/login', { data: { email, password: PASSWORD, rememberMe: false }, headers: { 'X-XSRF-TOKEN': await token() } })).ok()).toBe(true);
  const workspaces = (await (await request.get('/api/me/workspaces')).json()) as { id: string }[];
  return { workspaceId: workspaces[0]!.id };
}

function pngChunk(type: string, data: Buffer) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

/** A real, solid-colour PNG (RGB, 8-bit) built in memory: no binary fixtures in the repository. */
export function pngImage(width: number, height: number, rgb: [number, number, number] = [70, 130, 180]): Buffer {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header.set([8, 2, 0, 0, 0], 8); // 8-bit, RGB, deflate, no filter, no interlace
  const row = Buffer.concat([Buffer.from([0]), Buffer.from(Array.from({ length: width }, () => rgb).flat())]);
  const pixels = Buffer.concat(Array.from({ length: height }, () => row));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(pixels)),
    pngChunk('IEND', Buffer.alloc(0)),
  ]);
}

/** Registers and signs in through the API within the page's browser context (the page then has the session). */
export async function signedInTeacher(page: Page, email: string) {
  return teacherViaApi(page.request, email);
}
