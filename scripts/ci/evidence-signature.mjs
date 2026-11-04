// Signed Codex review evidence (docs/DEVELOPMENT.md "Independent Codex review and PR evidence").
//
// The evidence block in a PR body is only trusted when it carries an Ed25519 signature that verifies with the
// public key committed at scripts/ci/codex-evidence-public.pem. The private key exists only on the machine that
// runs `node scripts/codex-review.mjs review` (outside the repository), so a PR author who did not run the real
// review cannot produce valid evidence by typing it. The signature covers the reviewed and base commits, the
// Codex CLI version, the transcript hash, the exact raw Codex output and the declared counts.
//
// Used by both the generator (scripts/codex-review.mjs) and the gate (scripts/ci/pr-gate.sh via
// verify-evidence.mjs), so both build the signed payload in exactly the same way.
import { createHash, createPrivateKey, createPublicKey, sign, verify } from 'node:crypto';

export const SIGNATURE_PREFIX = 'Signature: ed25519:';
const RAW = /<!-- codex-raw-output:start -->\n```text\n([\s\S]*?)\n```\n<!-- codex-raw-output:end -->/;

const sha256 = (text) => createHash('sha256').update(text, 'utf8').digest('hex');

/** The exact raw Codex output text of an evidence block (as written by the generator). */
export function extractRaw(block) {
  const match = RAW.exec(block.replace(/\r/g, ''));
  if (!match) throw new Error('The raw Codex output block is missing or malformed.');
  return match[1];
}

/** Canonical, versioned payload that the signature covers. */
export function signingPayload({ commit, base, cli, transcriptSha256, raw, counts, rejected }) {
  return [
    'classhub-codex-evidence/v1',
    `reviewed: ${commit}`,
    `base: ${base}`,
    `cli: ${cli}`,
    `transcript-sha256: ${transcriptSha256}`,
    `raw-sha256: ${sha256(raw)}`,
    `findings: P0=${counts.P0} P1=${counts.P1} P2=${counts.P2} P3=${counts.P3}`,
    `rejected: ${rejected}`,
    '',
  ].join('\n');
}

export function signPayload(payload, privateKeyPem) {
  return sign(null, Buffer.from(payload, 'utf8'), createPrivateKey(privateKeyPem)).toString('base64');
}

/** SHA-256 fingerprint of a public key (DER), shown in the evidence and in docs. */
export function publicKeyFingerprint(publicKeyPem) {
  const der = createPublicKey(publicKeyPem).export({ type: 'spki', format: 'der' });
  return createHash('sha256').update(der).digest('hex');
}

const field = (body, name) => {
  const match = new RegExp(`^${name}: (.*)$`, 'm').exec(body);
  return match ? match[1].trim() : '';
};

/**
 * Verifies the signed evidence in a PR body. Returns the signed fields, or throws with the reason.
 * Every value the signature covers is read from the body and re-assembled into the payload, so changing
 * any of them (or the raw output) after signing invalidates the signature.
 */
export function verifyEvidence(body, publicKeyPem, expectedHead) {
  const text = body.replace(/\r/g, '');
  const start = text.indexOf('<!-- codex-review:start -->');
  const end = text.indexOf('<!-- codex-review:end -->');
  if (start < 0 || end < start) throw new Error('No generated Codex evidence block.');
  if (text.indexOf('<!-- codex-review:start -->', start + 1) >= 0 || text.indexOf('<!-- codex-review:end -->', end + 1) >= 0) {
    throw new Error('The body must contain exactly one Codex evidence block.');
  }
  const block = text.slice(start, end);

  const signatureLine = block.split('\n').find((line) => line.startsWith(SIGNATURE_PREFIX));
  if (!signatureLine) throw new Error('The evidence is not signed. Generate it with `node scripts/codex-review.mjs review` on the machine that holds the evidence key.');
  const signature = Buffer.from(signatureLine.slice(SIGNATURE_PREFIX.length).trim(), 'base64');

  const counts = /^P0=(\d+) P1=(\d+) P2=(\d+) P3=(\d+)$/.exec(field(block, 'Final round findings'));
  if (!counts) throw new Error("'Final round findings' is missing or malformed.");
  const signed = {
    commit: field(block, 'Reviewed commit'),
    base: field(block, 'Base commit'),
    cli: field(block, 'Codex CLI'),
    transcriptSha256: field(block, 'Transcript SHA-256'),
    raw: extractRaw(block),
    counts: { P0: Number(counts[1]), P1: Number(counts[2]), P2: Number(counts[3]), P3: Number(counts[4]) },
    rejected: Number(field(block, 'Rejected critical/high')),
  };
  const ok = verify(null, Buffer.from(signingPayload(signed), 'utf8'), createPublicKey(publicKeyPem), signature);
  if (!ok) throw new Error('The evidence signature is invalid: the evidence was not produced by the review script with the trusted key, or it was edited afterwards.');
  if (expectedHead !== undefined && signed.commit !== expectedHead) {
    throw new Error(`The signed evidence covers ${signed.commit}, but this check is for ${expectedHead}.`);
  }
  return signed;
}
