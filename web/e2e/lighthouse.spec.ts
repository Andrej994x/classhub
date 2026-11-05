import { chromium, expect, test } from '@playwright/test';
import { execFile } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { promisify } from 'node:util';
import { teacherViaApi, uniqueEmail, xsrf } from './support.ts';

/** Pinned (not the newest release): the audit runs `npx lighthouse@<version>` with Playwright's Chromium. */
const LIGHTHOUSE = 'lighthouse@13.5.0';

/**
 * R1-P12: Lighthouse SEO audit of a sample published page. Opt-in (E2E_LIGHTHOUSE=1): it downloads Lighthouse with npx
 * and is run before a release, not on every CI run. The score must be at least 0.9.
 */
test('a published sample page scores at least 90 in the Lighthouse SEO audit', async ({ request, baseURL }, testInfo) => {
  test.skip(!process.env.E2E_LIGHTHOUSE, 'Set E2E_LIGHTHOUSE=1 to run the Lighthouse audit.');
  test.skip(testInfo.project.name !== 'desktop', 'One audit is enough (Lighthouse emulates a phone itself).');
  test.setTimeout(240_000);

  const { workspaceId } = await teacherViaApi(request, uniqueEmail('lighthouse'));
  const slug = `lh-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  const draft = (await (await request.get(`/api/w/${workspaceId}/site`)).json()) as { draft: unknown };
  const saved = (await (await request.put(`/api/w/${workspaceId}/site`, {
    data: { slug, template: 'classic', palette: 'ocean', language: 'mk', draft: draft.draft, version: null },
    headers: { 'X-XSRF-TOKEN': await xsrf(request) },
  })).json()) as { version: number };
  expect((await request.post(`/api/w/${workspaceId}/site/publish`, { data: { version: saved.version }, headers: { 'X-XSRF-TOKEN': await xsrf(request) } })).ok()).toBe(true);

  const dir = mkdtempSync(join(tmpdir(), 'classhub-lighthouse-'));
  const report = join(dir, 'report.json');
  try {
    await promisify(execFile)(
      process.platform === 'win32' ? 'npx.cmd' : 'npx',
      ['--yes', LIGHTHOUSE, `${baseURL}/${slug}`, '--only-categories=seo', '--output=json', `--output-path=${report}`, '--quiet', '--chrome-flags=--headless=new --no-sandbox'],
      { env: { ...process.env, CHROME_PATH: chromium.executablePath() }, shell: process.platform === 'win32', timeout: 200_000, maxBuffer: 20 * 1024 * 1024 },
    );
    const result = JSON.parse(readFileSync(report, 'utf8')) as {
      categories: { seo: { score: number } };
      audits: Record<string, { score: number | null; title: string }>;
    };
    const failed = Object.entries(result.audits).filter(([, audit]) => audit.score === 0).map(([id, audit]) => `${id}: ${audit.title}`);
    testInfo.annotations.push({ type: 'lighthouse-seo', description: `${Math.round(result.categories.seo.score * 100)} (failed audits: ${failed.join('; ') || 'none'})` });
    process.stdout.write(`Lighthouse SEO score: ${Math.round(result.categories.seo.score * 100)}; failed audits: ${failed.join('; ') || 'none'}\n`);
    expect(result.categories.seo.score).toBeGreaterThanOrEqual(0.9);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
