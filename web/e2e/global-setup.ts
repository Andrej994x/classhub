import { execFile, spawn, type ChildProcess } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

const run = promisify(execFile);
const here = dirname(fileURLToPath(import.meta.url));
const webDir = resolve(here, '..');
const repoDir = resolve(webDir, '..');
const apiProject = join(repoDir, 'src', 'ClassHub.Api');

/** Same image as the integration tests and CI (override with CLASSHUB_TEST_SQL_IMAGE). */
const SQL_IMAGE = process.env.CLASSHUB_TEST_SQL_IMAGE ?? 'mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04';
export const E2E_PORT = Number(process.env.E2E_PORT ?? 5180);
export const E2E_BASE_URL = `http://127.0.0.1:${E2E_PORT}`;

const sleep = (ms: number) => new Promise((done) => setTimeout(done, ms));

/** Polls until `attempt` succeeds; stops early (with the last error) when `failed` reports a dead process. */
async function waitFor(what: string, attempt: () => Promise<boolean>, timeoutMs: number, failed: () => boolean = () => false) {
  const deadline = Date.now() + timeoutMs;
  let last: unknown;
  while (Date.now() < deadline) {
    try {
      if (await attempt()) return;
    } catch (error) {
      last = error;
      if (failed()) break;
    }
    await sleep(1_000);
  }
  throw new Error(`E2E setup: ${what} did not become ready (${last instanceof Error ? last.message : 'timeout'}).`);
}

/**
 * Starts the whole E2E stack, isolated from the local ClassHubDb:
 * 1. a disposable SQL Server 2022 container on a random loopback port with a generated sa password,
 * 2. the production SPA build (served by the API from wwwroot/dist),
 * 3. the API in the E2E environment (it migrates the empty ClassHubE2E_* database and refuses any other).
 * Returns the teardown (Playwright runs it after all tests, also after failures).
 */
export default async function globalSetup() {
  const resultsDir = join(webDir, 'test-results');
  mkdirSync(resultsDir, { recursive: true });
  const container = `classhub-e2e-${randomBytes(4).toString('hex')}`;
  const password = `E2e!${randomBytes(16).toString('hex')}`;
  let api: ChildProcess | undefined;

  const teardown = async () => {
    if (api && api.exitCode === null) {
      api.kill();
      await Promise.race([new Promise((done) => api!.once('exit', done)), sleep(10_000)]);
    }
    await run('docker', ['rm', '--force', '--volumes', container]).catch(() => undefined);
  };

  try {
    await run('docker', ['run', '--detach', '--name', container, '--publish', '127.0.0.1::1433', '--env', 'ACCEPT_EULA=Y', '--env', `MSSQL_SA_PASSWORD=${password}`, SQL_IMAGE]);
    const { stdout } = await run('docker', ['port', container, '1433/tcp']);
    const port = /:(\d+)\s*$/m.exec(stdout.trim())?.[1];
    if (!port) throw new Error(`E2E setup: no published SQL port (${stdout}).`);

    if (!process.env.E2E_SKIP_BUILD) {
      await run(process.platform === 'win32' ? 'npm.cmd' : 'npm', ['run', 'build'], { cwd: webDir, shell: process.platform === 'win32' });
      await run('dotnet', ['build', apiProject, '--configuration', 'Release', '--nologo', '--verbosity', 'quiet']);
    }

    await waitFor(
      'SQL Server',
      async () => {
        await run('docker', ['exec', container, '/opt/mssql-tools18/bin/sqlcmd', '-C', '-S', 'localhost', '-U', 'sa', '-P', password, '-Q', 'SELECT 1']);
        return true;
      },
      120_000,
    );

    const database = `ClassHubE2E_${randomBytes(6).toString('hex')}`;
    const connectionString = `Server=127.0.0.1,${port};Database=${database};User Id=sa;Password=${password};Encrypt=True;TrustServerCertificate=True`;
    // For tests that run the API's command-line tool against the same disposable database (grant-platform-owner).
    process.env.CLASSHUB_E2E_CONNECTION = connectionString;
    const log = createWriteStream(join(resultsDir, 'api.log'));
    api = spawn('dotnet', [join(apiProject, 'bin', 'Release', 'net10.0', 'ClassHub.Api.dll')], {
      cwd: apiProject, // content root: appsettings*.json and wwwroot/dist
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'E2E',
        ASPNETCORE_URLS: E2E_BASE_URL,
        ConnectionStrings__ClassHub: connectionString,
        App__PublicBaseUrl: E2E_BASE_URL,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    api.stdout?.pipe(log);
    api.stderr?.pipe(log);

    const started = api;
    await waitFor(
      'the API',
      async () => {
        if (started.exitCode !== null) throw new Error(`the API exited with code ${started.exitCode}; see test-results/api.log`);
        return (await fetch(`${E2E_BASE_URL}/health/ready`)).ok;
      },
      120_000,
      () => started.exitCode !== null,
    );
  } catch (error) {
    await teardown();
    throw error;
  }

  return teardown;
}
