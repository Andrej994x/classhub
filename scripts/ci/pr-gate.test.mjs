// Tests for the Codex evidence generator (scripts/codex-review.mjs) and the PR gate (scripts/ci/pr-gate.sh).
// Run: node --test scripts/ci/pr-gate.test.mjs   (needs bash; CI runs it on ubuntu)
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync } from 'node:crypto';
import { applyEvidence, buildEvidence, countFindings, evidenceFromRecord, extractFinalMessage } from '../codex-review.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const HEAD = 'a'.repeat(40);
const BASE = 'b'.repeat(40);
const HASH = 'c'.repeat(64);

// A test evidence key; the gate is pointed at its public key (EVIDENCE_PUBLIC_KEY).
const pem = (pair) => ({
  privateKey: pair.privateKey.export({ type: 'pkcs8', format: 'pem' }),
  publicKey: pair.publicKey.export({ type: 'spki', format: 'pem' }),
});
const KEY = pem(generateKeyPairSync('ed25519'));
const OTHER_KEY = pem(generateKeyPairSync('ed25519'));
const keyDir = mkdtempSync(join(tmpdir(), 'pr-gate-key-'));
const PUBLIC_KEY_FILE = join(keyDir, 'public.pem');
writeFileSync(PUBLIC_KEY_FILE, KEY.publicKey);

const transcript = (finalMessage) =>
  `OpenAI Codex v0.164.0\n--------\nexec\ngit diff origin/main...HEAD\n codex\nnot the end\ncodex\n${finalMessage}\n${finalMessage}\n`;

const cleanMessage = 'No actionable defects were identified in the changes against the supplied merge base.';
const p1Message = [
  'The outbox claim uses incompatible SQL Server locking hints.',
  '',
  'Review comment:',
  '',
  '- [P1] Remove conflicting granularity hints - EmailOutboxDispatcher.cs:71',
  '  SQL Server rejects this statement.',
  '- [P2] Validate tokens first - AccountService.cs:203',
].join('\n');

function evidence(message, overrides = {}) {
  return buildEvidence({ commit: HEAD, base: BASE, cli: 'codex-cli 0.164.0', transcriptSha256: HASH, message, privateKey: KEY.privateKey, ...overrides });
}

function runGate(body, { headSha = HEAD, headRef = 'feature/r0-p5-workspace-tenancy' } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'pr-gate-'));
  const bodyFile = join(dir, 'body.md');
  writeFileSync(bodyFile, body);
  const gh = join(dir, 'gh');
  writeFileSync(gh, '#!/usr/bin/env bash\ncat "$GH_STUB_BODY_FILE"\n');
  chmodSync(gh, 0o755);
  const result = spawnSync('bash', [join(ROOT, 'scripts', 'ci', 'pr-gate.sh')], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${dir}${process.platform === 'win32' ? ';' : ':'}${process.env.PATH}`,
      GH_STUB_BODY_FILE: bodyFile,
      GH_TOKEN: 'x', REPO: 'o/r', PR_NUMBER: '7', HEAD_REF: headRef, HEAD_SHA: headSha, PR_BODY: '',
      EVIDENCE_PUBLIC_KEY: PUBLIC_KEY_FILE,
    },
  });
  return { ok: result.status === 0, out: `${result.stdout}${result.stderr}` };
}

test('final message is extracted once (the CLI prints it twice)', () => {
  assert.equal(extractFinalMessage(transcript(cleanMessage)), cleanMessage);
});

test('a transcript without a completed review is rejected', () => {
  assert.throws(() => extractFinalMessage('exec\nsomething\n'), /no final "codex" message/);
});

test('inability-to-review answers and verdict-less messages produce no evidence', () => {
  assert.throws(
    () => extractFinalMessage(transcript('I could not review the changes because the repository could not be accessed.')),
    /could not \(fully\) complete the review/);
  assert.throws(() => extractFinalMessage(transcript('Here is a summary of the repository layout and its modules.')), /neither findings nor/);
  // An inability statement wins over clean-looking wording or an empty structured result.
  for (const failed of [
    'I could not access the repository, so no findings can be reported.',
    '{"findings": [], "overall_explanation": "Unable to read the diff against origin/main."}',
    'The review could not be completed: failed to load the changed files. No issues found.',
  ]) {
    assert.throws(() => extractFinalMessage(transcript(failed)), /could not \(fully\) complete the review/);
  }
  // Real verdicts recorded so far stay accepted.
  for (const ok of [
    'No actionable defects were identified in the changes against the supplied merge base.',
    'The changes align with the R0-P3 platform scope. Inspection found no concrete, actionable regressions in the pipeline.',
    'No actionable defects were identified; build and test execution was not verified in this read-only review.',
    'I did not find any actionable issues in the diff against origin/main.',
    'I could not find any regressions introduced by these changes.',
    '{"findings": [], "overall_correctness": "patch is correct", "overall_explanation": "fine"}',
  ]) {
    assert.equal(extractFinalMessage(transcript(ok)), ok);
  }
});

test('severity counts ignore duplicates', () => {
  assert.deepEqual(countFindings(`${p1Message}\n${p1Message}`), { P0: 0, P1: 1, P2: 1, P3: 0 });
});

test('evidence is only produced from an intact review record (no re-binding)', () => {
  const bytes = Buffer.from(transcript(cleanMessage));
  const record = { commit: HEAD, base: BASE, cli: 'codex-cli 0.164.0', transcriptSha256: createHash('sha256').update(bytes).digest('hex') };
  assert.match(evidenceFromRecord(record, bytes), new RegExp(`Reviewed commit: ${HEAD}`));
  assert.throws(() => evidenceFromRecord(record, Buffer.from(transcript('Another review for another commit.'))), /does not match the review record/);
});

test('evidence requires full SHAs and a valid rejection count', () => {
  assert.throws(() => evidence(cleanMessage, { commit: 'abc1234' }), /full SHA/);
  assert.throws(() => evidence(cleanMessage, { rejected: 1 }), /invalid/);
});

test('generated evidence for a clean review passes the gate', () => {
  const r = runGate(applyEvidence('## Phase\n\nX\n', evidence(cleanMessage)));
  assert.ok(r.ok, r.out);
  assert.match(r.out, /PR gate passed/);
});

test('short explicit clean verdicts are accepted end to end', () => {
  for (const verdict of ['No issues found.', 'No actionable findings.']) {
    const message = extractFinalMessage(transcript(verdict));
    assert.equal(message, verdict);
    const r = runGate(applyEvidence('', evidence(message)));
    assert.ok(r.ok, `${verdict}: ${r.out}`);
  }
});

test('the gate fails when the evidence is for another commit', () => {
  const r = runGate(applyEvidence('', evidence(cleanMessage)), { headSha: 'd'.repeat(40) });
  assert.ok(!r.ok);
  assert.match(r.out, /covers a{40}, but this check is for d{40}/);
});

test('unresolved P1 findings fail; documented rejections pass', () => {
  assert.ok(!runGate(applyEvidence('', evidence(p1Message))).ok);
  const withoutReasoning = runGate(applyEvidence('', evidence(p1Message, { rejected: 1 })));
  assert.ok(!withoutReasoning.ok);
  assert.match(withoutReasoning.out, /Rejected findings/);
  const body = applyEvidence(
    '## Codex review\n\n### Rejected findings\n\n- [P1] granularity hints: rejected because the statement runs in the integration suite on SQL Server 2022 with RCSI.\n\n',
    evidence(p1Message, { rejected: 1 }));
  const r = runGate(body);
  assert.ok(r.ok, r.out);
});

test('tampered counts or a hand-edited unresolved line fail', () => {
  const tamperedCounts = evidence(p1Message, { rejected: 0 }).replace('P1=1', 'P1=0').replace('Unresolved critical/high: 1', 'Unresolved critical/high: 0');
  assert.match(runGate(tamperedCounts).out, /differ from the raw Codex output/);
  const tamperedUnresolved = evidence(p1Message).replace('Unresolved critical/high: 1', 'Unresolved critical/high: 0');
  assert.match(runGate(tamperedUnresolved).out, /must be P0\+P1-rejected/);
});

test('missing or legacy evidence fails with a hint', () => {
  assert.match(runGate('## Phase\n').out, /No generated Codex evidence block/);
  const legacy = '<!-- codex-review -->\nCodex review: completed\nReviewed commit: abc1234\nUnresolved critical/high: 0\n';
  assert.match(runGate(legacy).out, /No generated Codex evidence block/);
});

test('branch naming is enforced', () => {
  assert.match(runGate(applyEvidence('', evidence(cleanMessage)), { headRef: 'Feature/X' }).out, /must match/);
});

test('applying evidence keeps the human review history of a legacy body', () => {
  const legacy = [
    '## Codex review', '', '<!-- codex-review -->', 'Codex review: completed', 'Reviewed commit: 4e3f0a1', '',
    '| # | Round | Finding |', '|---|---|---|', '| 1 | 1 | fixed |', '', 'Unresolved critical/high: 0', '',
    '<details><summary>Raw</summary>', '<!-- codex-raw-output:start -->', 'x', '<!-- codex-raw-output:end -->', '</details>',
    '', '## Security considerations', 'none',
  ].join('\n');
  const updated = applyEvidence(legacy, evidence(cleanMessage));
  assert.match(updated, /### Review history\n\n\| # \| Round \| Finding \|/);
  assert.match(updated, /All review rounds \(raw Codex output[\s\S]*```text\nx\n```/);
  assert.equal(updated.split('<!-- codex-raw-output:start -->').length, 2); // only the generated block is marked
  assert.ok(runGate(updated).ok, 'a converted legacy body passes the gate');
  assert.match(updated, /<!-- codex-review:start -->[\s\S]*<!-- codex-review:end -->/);
  assert.match(updated, /## Security considerations\nnone/);
  assert.doesNotMatch(updated, /Reviewed commit: 4e3f0a1/);
  // Re-applying replaces the generated block instead of adding another one.
  const again = applyEvidence(updated, evidence(cleanMessage));
  assert.equal(again.split('<!-- codex-review:start -->').length, 2);
});

test('unsigned evidence fails: typing evidence by hand is not enough', () => {
  const r = runGate(applyEvidence('', evidence(cleanMessage, { privateKey: undefined })));
  assert.ok(!r.ok);
  assert.match(r.out, /not signed/);
});

test('evidence signed with another key fails', () => {
  const r = runGate(applyEvidence('', evidence(cleanMessage, { privateKey: OTHER_KEY.privateKey })));
  assert.ok(!r.ok);
  assert.match(r.out, /signature is invalid/);
});

test('editing signed evidence afterwards fails (raw output, commit, transcript hash)', () => {
  const signed = applyEvidence('', evidence(p1Message, { rejected: 1 }));
  const reasoning = '\n### Rejected findings\n\n- [P1] granularity hints: rejected because the exact statement runs in the integration suite on SQL Server 2022 with RCSI.\n';
  assert.ok(runGate(signed + reasoning).ok, 'the untouched evidence passes');

  // Same counts, different wording: a softened finding must not keep a valid signature.
  const softened = signed.replace('SQL Server rejects this statement.', 'SQL Server may warn about this statement.');
  assert.match(runGate(softened + reasoning).out, /signature is invalid/);

  // Re-pointing old evidence at a new head commit.
  const newHead = 'e'.repeat(40);
  const repointed = signed.replace(`Reviewed commit: ${HEAD}`, `Reviewed commit: ${newHead}`);
  assert.match(runGate(repointed + reasoning, { headSha: newHead }).out, /signature is invalid/);

  const otherTranscript = signed.replace(`Transcript SHA-256: ${HASH}`, `Transcript SHA-256: ${'f'.repeat(64)}`);
  assert.match(runGate(otherTranscript + reasoning).out, /signature is invalid/);

  // Raising the rejection count after signing (to "resolve" a P1).
  const unsignedRejection = applyEvidence('', evidence(p1Message)).replace('Rejected critical/high: 0', 'Rejected critical/high: 1').replace('Unresolved critical/high: 1', 'Unresolved critical/high: 0');
  assert.match(runGate(unsignedRejection + reasoning).out, /signature is invalid/);
});

test('fields outside the evidence block cannot change what is certified (reused old evidence)', () => {
  // Valid signed evidence for an older commit, plus a forged "Reviewed commit" line for the new head above it.
  const newHead = 'e'.repeat(40);
  const reused = `Reviewed commit: ${newHead}\nFinal round findings: P0=0 P1=0 P2=0 P3=0\n\n${applyEvidence('', evidence(cleanMessage))}`;
  const r = runGate(reused, { headSha: newHead });
  assert.ok(!r.ok, r.out);
  assert.match(r.out, /covers a{40}, but this check is for e{40}/);
});

test('a second evidence block fails', () => {
  const one = applyEvidence('', evidence(cleanMessage));
  const r = runGate(`${one}\n\n${evidence(cleanMessage)}`);
  assert.ok(!r.ok);
  assert.match(r.out, /exactly one Codex evidence block/);
});

test('the gate fails closed without the trusted public key', () => {
  const dir = mkdtempSync(join(tmpdir(), 'pr-gate-nokey-'));
  const bodyFile = join(dir, 'body.md');
  writeFileSync(bodyFile, applyEvidence('', evidence(cleanMessage)));
  const gh = join(dir, 'gh');
  writeFileSync(gh, '#!/usr/bin/env bash\ncat "$GH_STUB_BODY_FILE"\n');
  chmodSync(gh, 0o755);
  const result = spawnSync('bash', [join(ROOT, 'scripts', 'ci', 'pr-gate.sh')], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${dir}${process.platform === 'win32' ? ';' : ':'}${process.env.PATH}`,
      GH_STUB_BODY_FILE: bodyFile,
      GH_TOKEN: 'x', REPO: 'o/r', PR_NUMBER: '7', HEAD_REF: 'feature/x', HEAD_SHA: HEAD, PR_BODY: '',
      EVIDENCE_PUBLIC_KEY: join(dir, 'missing.pem'),
    },
  });
  assert.notEqual(result.status, 0);
  assert.match(`${result.stdout}${result.stderr}`, /public key is missing/);
});
