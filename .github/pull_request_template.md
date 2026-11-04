## Phase

<!-- e.g. R1-P2 — Offers & Programs · branch feature/r1-p2-offers -->

## Scope / Out of scope

## Business rules covered

## Tests run

| Check | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | |
| `dotnet test --solution ClassHub.slnx` (unit / architecture / integration) | |
| `web`: lint / i18n / vitest / build | |
| Playwright | |

## Migrations

## Codex review

### Review history

<!-- One row per round and finding: commit, severity, finding, resolution (fixed in <sha> / rejected). -->

| # | Round / commit | Severity | Finding | Resolution |
|---|---|---|---|---|

<!--
The evidence block below is GENERATED, never typed by hand. After the final commit:
  node scripts/codex-review.mjs review                 # runs codex review --base origin/main on HEAD
  node scripts/codex-review.mjs apply --body <this file> --evidence .codex-review/<head-sha>.evidence.md
or, for an open PR:
  node scripts/codex-review.mjs apply --pr <number> --evidence .codex-review/<head-sha>.evidence.md
If a final-round P0/P1 finding is rejected, pass --rejected <n> to `review`/`evidence` and add a
"### Rejected findings" section with the technical reasoning. The PR gate rejects anything else.
-->
<!-- codex-review:start -->
(generated Codex evidence goes here)
<!-- codex-review:end -->

## Security considerations / Remaining risks
