#!/usr/bin/env node
// Verifies the Ed25519 signature of the Codex evidence in a PR body (called by scripts/ci/pr-gate.sh).
// Usage: node scripts/ci/verify-evidence.mjs <body-file> <public-key-pem>
import { readFileSync } from 'node:fs';
import { publicKeyFingerprint, verifyEvidence } from './evidence-signature.mjs';

const [bodyFile, publicKeyFile] = process.argv.slice(2);
if (!bodyFile || !publicKeyFile) {
  console.error('Usage: node scripts/ci/verify-evidence.mjs <body-file> <public-key-pem>');
  process.exit(64);
}

try {
  const publicKey = readFileSync(publicKeyFile, 'utf8');
  const signed = verifyEvidence(readFileSync(bodyFile, 'utf8'), publicKey);
  console.log(`Evidence signature valid for ${signed.commit.slice(0, 7)} (key ${publicKeyFingerprint(publicKey).slice(0, 16)}).`);
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
