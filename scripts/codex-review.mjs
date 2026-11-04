#!/usr/bin/env node
// Independent Codex review → machine-generated, commit-bound PR evidence (docs/DEVELOPMENT.md).
//
// Commands
//   node scripts/codex-review.mjs review [--rejected <n>]
//       Requires a clean working tree. Runs `codex review --base origin/main` on the current HEAD, saves the
//       full transcript to .codex-review/<head-sha>.transcript.md and writes the evidence block to
//       .codex-review/<head-sha>.evidence.md.
//   node scripts/codex-review.mjs evidence --review <.codex-review/<sha>.review.json> [--rejected <n>]
//       Regenerates the evidence block of a completed review (for example to record a rejection). The
//       reviewed commit, base and transcript come only from the review record written by `review`; the
//       transcript hash is re-verified. Evidence can never be re-bound to another commit.
//   node scripts/codex-review.mjs apply --evidence <file> (--pr <number> | --body <file>)
//       Replaces the Codex evidence block in a PR body (via gh) or in a local body file.
//   node scripts/codex-review.mjs keygen [--force]
//       Creates the Ed25519 evidence key: the private key goes OUTSIDE the repository (default
//       ~/.classhub/codex-evidence-ed25519.pem, override with CLASSHUB_EVIDENCE_KEY), the public key to
//       scripts/ci/codex-evidence-public.pem (commit it; the PR gate verifies signatures with it).
//
// Evidence is signed with the private key (scripts/ci/evidence-signature.mjs). The PR gate rejects unsigned
// evidence and evidence edited after signing, so typing evidence by hand cannot pass it.
//
// The evidence is derived only from the real transcript and git: reviewed commit (full SHA), base commit,
// Codex CLI version, transcript SHA-256 and the severity counts of the final review message. It is never
// typed by hand. --rejected records how many final-round P0/P1 findings were rejected with documented
// reasoning (a "### Rejected findings" section in the PR body is then required by the PR gate).
import { createHash, generateKeyPairSync } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { closeSync, existsSync, fchmodSync, mkdirSync, openSync, readFileSync, writeFileSync, writeSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SIGNATURE_PREFIX, publicKeyFingerprint, signPayload, signingPayload } from './ci/evidence-signature.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT_DIR = join(ROOT, '.codex-review');
export const START = '<!-- codex-review:start -->';
export const END = '<!-- codex-review:end -->';
export const PUBLIC_KEY_PATH = join(ROOT, 'scripts', 'ci', 'codex-evidence-public.pem');
export const privateKeyPath = () => process.env.CLASSHUB_EVIDENCE_KEY || join(homedir(), '.classhub', 'codex-evidence-ed25519.pem');

const git = (...args) => execFileSync('git', args, { cwd: ROOT, encoding: 'utf8' }).trim();

/** The final review message: everything after the last line that is exactly "codex", de-duplicated
 *  (the CLI prints the final message once in the transcript and once more on stdout). */
export function extractFinalMessage(transcript) {
  const lines = transcript.replace(/\r/g, '').split('\n');
  let last = -1;
  for (let i = 0; i < lines.length; i++) if (lines[i].trim() === 'codex') last = i;
  if (last < 0) throw new Error('Not a completed Codex review transcript: no final "codex" message found.');
  let message = lines.slice(last + 1).join('\n').trim();
  // Drop the repeated copy: the text is "X X" when the message was printed twice. Whitespace is ignored in
  // the comparison because a console may have hard-wrapped one copy; the later (unwrapped) copy is kept.
  const compact = (s) => s.replace(/\s+/g, '');
  for (let i = message.indexOf('\n'); i > 0; i = message.indexOf('\n', i + 1)) {
    if (compact(message.slice(0, i)) === compact(message.slice(i))) {
      message = message.slice(i).trim();
      break;
    }
  }
  if (message.trim() === '') throw new Error('The final Codex message is empty.');
  // Completion is decided by content (findings or an explicit clean verdict), never by length.
  assertCompletedReview(message);
  return message;
}

// Statements that the review itself could not be done. "find/identify/spot/see" are deliberately absent:
// "I could not find any issues" is a verdict, not a failure.
const INABILITY = /\b(could not|couldn't|unable to|cannot|can't|failed to|was not able to|wasn't able to|not possible to)\b[^.\n]{0,60}\b(review|access|read|open|load|inspect|complete|retrieve|clone|fetch|analy[sz]e|examine|evaluate)\b/i;
// Clean verdicts: negative phrasing ("no actionable defects", "did not find any issues", "found no
// regressions"), or Codex's structured output ("findings": [] / "overall_correctness": "patch is correct").
const CLEAN = [
  /\b(no|not|none|without|never)\b[^.\n]{0,60}\b(defects?|issues?|regressions?|problems?|findings?|bugs?|concerns?|errors?)\b/i,
  /\b(did not|didn't|could not|couldn't)\s+(find|identify|spot|see)\b[^.\n]{0,60}\b(defects?|issues?|regressions?|problems?|findings?|bugs?|concerns?)\b/i,
  /"findings"\s*:\s*\[\s*\]/i,
  /"overall_correctness"\s*:\s*"patch is correct"/i,
  /\bpatch is correct\b/i,
];

/** Fails closed unless the message is a real review result: findings, or an explicit clean verdict
 *  without any statement that the review could not be done. */
export function assertCompletedReview(message) {
  const hasFindings = /\[P[0-3]\]/.test(message);
  const isClean = CLEAN.some((pattern) => pattern.test(message));
  // Without findings, an explicit inability statement always wins: "could not access the repository, so no
  // findings" is a failed review, not a clean one. (Findings prove the review ran; their text may quote
  // such phrases.) When in doubt, fail closed and run the review again.
  if (!hasFindings && INABILITY.test(message)) {
    throw new Error('Codex reports that it could not (fully) complete the review; no evidence is produced. Check the transcript and run it again.');
  }
  if (!hasFindings && !isClean) {
    throw new Error('The final Codex message has neither findings nor a recognizable clean verdict; no evidence is produced. Check the transcript and run the review again.');
  }
}

/** Counts unique findings by severity in a final review message ("[P0]".."[P3]"). */
export function countFindings(message) {
  const seen = new Set();
  const counts = { P0: 0, P1: 0, P2: 0, P3: 0 };
  for (const line of message.split('\n')) {
    const m = line.match(/\[(P[0-3])\]\s*(.*)$/);
    if (!m) continue;
    const key = `${m[1]}|${m[2].replace(/\s+/g, ' ').trim().slice(0, 120)}`;
    if (seen.has(key)) continue;
    seen.add(key);
    counts[m[1]]++;
  }
  return counts;
}

/** Builds the evidence block. With `privateKey` (PEM) it is signed; the PR gate only accepts signed evidence. */
export function buildEvidence({ commit, base, cli, transcriptSha256, message, rejected = 0, privateKey }) {
  if (!/^[0-9a-f]{40}$/.test(commit)) throw new Error(`Reviewed commit must be a full SHA, got '${commit}'.`);
  if (!/^[0-9a-f]{40}$/.test(base)) throw new Error(`Base commit must be a full SHA, got '${base}'.`);
  const counts = countFindings(message);
  const criticalHigh = counts.P0 + counts.P1;
  if (rejected < 0 || rejected > criticalHigh) {
    throw new Error(`--rejected ${rejected} is invalid: the final round has ${criticalHigh} P0/P1 finding(s).`);
  }
  const unresolved = criticalHigh - rejected;
  const raw = message.replace(/```/g, "'''");
  const signature = privateKey
    ? [`${SIGNATURE_PREFIX}${signPayload(signingPayload({ commit, base, cli, transcriptSha256, raw, counts, rejected }), privateKey)}`]
    : [];
  return [
    START,
    '<!-- codex-review -->',
    'Codex review: completed',
    `Reviewed commit: ${commit}`,
    `Base commit: ${base}`,
    `Codex CLI: ${cli}`,
    `Transcript SHA-256: ${transcriptSha256}`,
    `Final round findings: P0=${counts.P0} P1=${counts.P1} P2=${counts.P2} P3=${counts.P3}`,
    `Rejected critical/high: ${rejected}`,
    `Unresolved critical/high: ${unresolved}`,
    ...signature,
    '',
    '<details><summary>Raw Codex output (final review message of the reviewed commit)</summary>',
    '',
    '<!-- codex-raw-output:start -->',
    '```text',
    raw,
    '```',
    '<!-- codex-raw-output:end -->',
    '',
    '</details>',
    END,
  ].join('\n');
}

/** Replaces the evidence block in a PR body. Older bodies (without start/end markers) have their legacy
 *  block replaced; a human-written review history table is preserved under "### Review history". */
export function applyEvidence(body, evidence) {
  const text = (body ?? '').replace(/\r/g, '');
  const s = text.indexOf(START);
  const e = text.indexOf(END);
  if (s >= 0 && e > s) return text.slice(0, s) + evidence + text.slice(e + END.length);

  const legacy = text.indexOf('<!-- codex-review -->');
  if (legacy >= 0) {
    const rawEnd = text.indexOf('<!-- codex-raw-output:end -->', legacy);
    const details = rawEnd >= 0 ? text.indexOf('</details>', rawEnd) : -1;
    const stop = details >= 0 ? details + '</details>'.length : text.indexOf('\n## ', legacy);
    const old = text.slice(legacy, stop < 0 ? text.length : stop);
    const history = old.split('\n').filter((l) => l.startsWith('|')).join('\n');
    // Earlier rounds' raw output stays for the audit trail, without raw-output markers (the gate only
    // recounts the generated block of the reviewed commit).
    const oldRaw = old.match(/<!-- codex-raw-output:start -->\n?([\s\S]*?)<!-- codex-raw-output:end -->/)?.[1]
      ?.replace(/^```text\n?/, '').replace(/\n?```\s*$/, '').trim();
    let keep = history ? `### Review history\n\n${history}\n\n` : '';
    if (oldRaw) {
      keep += `<details><summary>All review rounds (raw Codex output, recorded before generated evidence)</summary>\n\n\`\`\`text\n${oldRaw.replace(/```/g, "'''")}\n\`\`\`\n\n</details>\n\n`;
    }
    return text.slice(0, legacy) + keep + evidence + (stop < 0 ? '' : text.slice(stop));
  }

  return `${text.trimEnd()}\n\n## Codex review\n\n${evidence}\n`;
}

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : fallback;
}

function codexVersion() {
  return execFileSync('codex', ['--version'], { encoding: 'utf8', shell: process.platform === 'win32' }).trim();
}

const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');

/** Builds evidence from a review record; the transcript must still have the recorded hash. */
export function evidenceFromRecord(record, transcriptBytes, rejected = 0, privateKey = undefined) {
  const actual = sha256(transcriptBytes);
  if (actual !== record.transcriptSha256) {
    throw new Error(`Transcript hash ${actual} does not match the review record (${record.transcriptSha256}).`);
  }
  return buildEvidence({
    commit: record.commit,
    base: record.base,
    cli: record.cli,
    transcriptSha256: record.transcriptSha256,
    message: extractFinalMessage(transcriptBytes.toString('utf8')),
    rejected,
    privateKey,
  });
}

/** The evidence key; signing is mandatory for the review and evidence commands. */
function loadPrivateKey() {
  const path = privateKeyPath();
  if (!existsSync(path)) {
    throw new Error(`No evidence key at ${path}. Create it once with 'node scripts/codex-review.mjs keygen' (see docs/DEVELOPMENT.md).`);
  }
  return readFileSync(path, 'utf8');
}

function keygen() {
  const path = privateKeyPath();
  if (resolve(path).startsWith(ROOT)) throw new Error('The private evidence key must be stored outside the repository.');
  const { privateKey, publicKey } = generateKeyPairSync('ed25519');
  mkdirSync(dirname(path), { recursive: true });
  // One open with exclusive create ('wx'): no window between "does it exist?" and writing (no clobbering a key
  // that appeared meanwhile). --force replaces an existing key deliberately.
  let fd;
  try {
    fd = openSync(path, process.argv.includes('--force') ? 'w' : 'wx', 0o600);
  } catch (error) {
    if (error.code === 'EEXIST') {
      throw new Error(`${path} already exists. Pass --force to replace it (then the public key must be replaced in a PR, see docs/RECOVERY.md).`);
    }
    throw error;
  }
  try {
    writeSync(fd, privateKey.export({ type: 'pkcs8', format: 'pem' }));
    try {
      fchmodSync(fd, 0o600);
    } catch {
      // Windows: the file is in the user profile, readable only by the user by default.
    }
  } finally {
    closeSync(fd);
  }
  const publicPem = publicKey.export({ type: 'spki', format: 'pem' });
  writeFileSync(PUBLIC_KEY_PATH, publicPem);
  console.log(`Private key: ${path} (keep it secret; back it up outside the repository)`);
  console.log(`Public key:  ${PUBLIC_KEY_PATH} (commit it)`);
  console.log(`Fingerprint: ${publicKeyFingerprint(publicPem)}`);
}

/** Tracked changes and untracked (not ignored) files: Codex could review them as part of the working tree. */
const workingTreeChanges = () => git('status', '--porcelain', '--untracked-files=normal');

async function review() {
  loadPrivateKey(); // fail before the (long) review when the key is missing
  if (workingTreeChanges()) {
    throw new Error('The working tree has uncommitted or untracked changes. Commit them first: the review covers HEAD only.');
  }
  git('fetch', 'origin', '--quiet');
  const commit = git('rev-parse', 'HEAD');
  const base = git('rev-parse', 'origin/main');
  mkdirSync(OUT_DIR, { recursive: true });
  const transcriptPath = join(OUT_DIR, `${commit}.transcript.md`);
  console.log(`Running codex review --base origin/main for ${commit.slice(0, 7)} (this takes several minutes)...`);
  const output = [];
  const code = await new Promise((done) => {
    const child = spawn('codex', ['review', '--base', 'origin/main'], { cwd: ROOT, shell: process.platform === 'win32' });
    child.stdout.on('data', (d) => output.push(d));
    child.stderr.on('data', (d) => output.push(d));
    child.on('close', done);
  });
  const transcriptBytes = Buffer.concat(output);
  writeFileSync(transcriptPath, transcriptBytes);
  if (code !== 0) throw new Error(`codex review exited with ${code}; see ${transcriptPath}. No evidence was written.`);
  if (git('rev-parse', 'HEAD') !== commit) throw new Error('HEAD changed during the review. Run it again.');
  if (workingTreeChanges()) {
    throw new Error('Files changed while the review ran, so it may not match HEAD. No evidence was written; run it again.');
  }
  extractFinalMessage(transcriptBytes.toString('utf8')); // the review must have completed
  const record = {
    commit,
    base,
    cli: codexVersion(),
    transcript: `${commit}.transcript.md`,
    transcriptSha256: sha256(transcriptBytes),
    completedAtUtc: new Date().toISOString(),
  };
  writeFileSync(join(OUT_DIR, `${commit}.review.json`), `${JSON.stringify(record, null, 2)}\n`);
  const evidence = evidenceFromRecord(record, transcriptBytes, Number(arg('rejected', '0')), loadPrivateKey());
  const evidencePath = join(OUT_DIR, `${commit}.evidence.md`);
  writeFileSync(evidencePath, `${evidence}\n`);
  console.log(`Transcript: ${transcriptPath}\nEvidence:   ${evidencePath}`);
  console.log(evidence.split('\n').filter((l) => /^(Reviewed commit|Final round findings|Unresolved)/.test(l)).join('\n'));
}

function apply() {
  const evidencePath = arg('evidence');
  if (!evidencePath || !existsSync(evidencePath)) throw new Error('--evidence <file> is required.');
  const evidence = readFileSync(evidencePath, 'utf8').trim();
  const pr = arg('pr');
  const bodyFile = arg('body');
  if (pr) {
    const body = execFileSync('gh', ['pr', 'view', pr, '--json', 'body', '--jq', '.body'], { encoding: 'utf8' });
    const tmp = join(OUT_DIR, `pr-${pr}.body.md`);
    writeFileSync(tmp, applyEvidence(body, evidence));
    execFileSync('gh', ['pr', 'edit', pr, '--body-file', tmp], { stdio: 'inherit' });
  } else if (bodyFile) {
    writeFileSync(bodyFile, applyEvidence(readFileSync(bodyFile, 'utf8'), evidence));
    console.log(`Updated ${bodyFile}`);
  } else {
    throw new Error('Pass --pr <number> or --body <file>.');
  }
}

async function main() {
  const command = process.argv[2];
  if (command === 'review') return review();
  if (command === 'evidence') {
    const recordPath = arg('review');
    if (!recordPath || !existsSync(recordPath)) throw new Error('--review <.codex-review/<sha>.review.json> is required.');
    const record = JSON.parse(readFileSync(recordPath, 'utf8'));
    const transcriptBytes = readFileSync(join(dirname(resolve(recordPath)), record.transcript));
    const evidence = evidenceFromRecord(record, transcriptBytes, Number(arg('rejected', '0')), loadPrivateKey());
    const out = join(OUT_DIR, `${record.commit}.evidence.md`);
    writeFileSync(out, `${evidence}\n`);
    console.log(`Evidence: ${out}`);
    return undefined;
  }
  if (command === 'apply') return apply();
  if (command === 'keygen') return keygen();
  console.error('Usage: node scripts/codex-review.mjs review|evidence|apply|keygen [options]');
  process.exitCode = 64;
  return undefined;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
