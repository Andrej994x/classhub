// Checks that every locale file has exactly the same translation keys and the same
// {{placeholders}} as the reference locale (mk). Uses only Node built-ins.
// Usage: node scripts/check-i18n.mjs [localesDir]
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const REFERENCE = 'mk';
/** Every supported UI language must exist; a missing file fails the check. */
const REQUIRED_LOCALES = ['mk', 'en'];
const localesDir = resolve(process.argv[2] ?? join(import.meta.dirname, '..', 'src', 'i18n', 'locales'));

/** Flattens nested JSON into { "a.b.c": "value" }. */
function flatten(value, prefix = '', out = {}) {
  if (typeof value === 'string') {
    out[prefix] = value;
  } else if (value && typeof value === 'object' && !Array.isArray(value)) {
    for (const [key, child] of Object.entries(value)) {
      flatten(child, prefix ? `${prefix}.${key}` : key, out);
    }
  } else {
    out[prefix] = Symbol.for('invalid');
  }
  return out;
}

const placeholders = (text) => [...text.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)].map((m) => m[1]).toSorted();

const files = readdirSync(localesDir).filter((f) => f.endsWith('.json'));
const locales = Object.fromEntries(
  files.map((f) => [f.replace(/\.json$/, ''), flatten(JSON.parse(readFileSync(join(localesDir, f), 'utf8')))]),
);

const errors = [];
for (const locale of REQUIRED_LOCALES) {
  if (!(locale in locales)) errors.push(`Required locale "${locale}.json" not found in ${localesDir}`);
}
const reference = locales[REFERENCE];
if (!reference) {
  errors.push(`Reference locale "${REFERENCE}.json" not found in ${localesDir}`);
} else {
  for (const [locale, entries] of Object.entries(locales)) {
    for (const [key, text] of Object.entries(entries)) {
      if (typeof text !== 'string') errors.push(`[${locale}] "${key}" must be a string`);
      else if (text.trim() === '') errors.push(`[${locale}] "${key}" is empty`);
    }
    if (locale === REFERENCE) continue;
    for (const key of Object.keys(reference)) {
      if (!(key in entries)) errors.push(`[${locale}] missing key "${key}"`);
    }
    for (const key of Object.keys(entries)) {
      if (!(key in reference)) errors.push(`[${locale}] extra key "${key}" (not in ${REFERENCE})`);
      else if (typeof entries[key] === 'string' && typeof reference[key] === 'string') {
        const a = placeholders(reference[key]).join(',');
        const b = placeholders(entries[key]).join(',');
        if (a !== b) errors.push(`[${locale}] "${key}" placeholders {${b}} differ from ${REFERENCE} {${a}}`);
      }
    }
  }
}

// Every API error code (ClassHub.Core SharedKernel ErrorCodes) needs a translation under errors.codes.
const errorCodesFile = resolve(import.meta.dirname, '..', '..', 'src', 'ClassHub.Core', 'SharedKernel', 'Errors', 'ErrorCodes.cs');
if (reference && process.argv[2] === undefined) {
  try {
    const source = readFileSync(errorCodesFile, 'utf8');
    for (const [, code] of source.matchAll(/public const string \w+ = "([A-Z_]+)";/g)) {
      if (!(`errors.codes.${code}` in reference)) errors.push(`[${REFERENCE}] missing translation for API error code "${code}" (errors.codes.${code})`);
    }
  } catch {
    errors.push(`Cannot read ${errorCodesFile} to check API error code translations`);
  }
}

if (errors.length > 0) {
  console.error(`i18n check failed (${errors.length}):\n- ${errors.join('\n- ')}`);
  process.exit(1);
}
console.log(`i18n check passed: ${files.length} locales (${Object.keys(locales).join(', ')}), ${Object.keys(reference).length} keys each.`);
