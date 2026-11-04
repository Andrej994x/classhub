#!/usr/bin/env bash
# PR gate for ClassHub (see docs/ROADMAP.md section I.4 and .github/pull_request_template.md).
# Inputs (environment): GH_TOKEN, REPO, PR_NUMBER, HEAD_REF, PR_BODY.
set -euo pipefail

fail() {
  echo "::error::$1"
  exit 1
}

body="${PR_BODY:-}"
body="${body//$'\r'/}"

# 1. Branch naming: feature/*, fix/*, chore/* (Dependabot branches are allowed but still need a review).
if [[ ! "$HEAD_REF" =~ ^(feature|fix|chore)/[a-z0-9][a-z0-9._-]*$ && ! "$HEAD_REF" =~ ^dependabot/ ]]; then
  fail "Branch '$HEAD_REF' must match feature/*, fix/* or chore/* (lowercase)."
fi

# 2. Codex review section.
grep -q '<!-- codex-review -->' <<<"$body" || fail "PR body has no '<!-- codex-review -->' section."
grep -Eq '^Codex review: completed[[:space:]]*$' <<<"$body" || fail "PR body must state 'Codex review: completed'."

unresolved="$( (grep -Eo '^Unresolved critical/high: [0-9]+' <<<"$body" || true) | head -n1 | awk '{print $3}')"
[[ -n "$unresolved" ]] || fail "PR body must state 'Unresolved critical/high: <n>'."
[[ "$unresolved" == "0" ]] || fail "There are $unresolved unresolved critical/high Codex findings."

# 3. Raw Codex output must be attached (between the markers), not only a summary.
raw="$(awk '/<!-- codex-raw-output:start -->/{f=1;next}/<!-- codex-raw-output:end -->/{f=0}f' <<<"$body")"
raw_chars="$(tr -d '[:space:]`' <<<"$raw" | wc -c)"
(( raw_chars >= 40 )) || fail "Raw Codex output is missing between the codex-raw-output markers."
if grep -q '(paste the full' <<<"$raw"; then
  fail "Raw Codex output still contains the template placeholder."
fi

# 4. The reviewed commit must be a commit of this pull request.
reviewed="$( (grep -Eo '^Reviewed commit: [0-9a-f]{7,40}' <<<"$body" || true) | head -n1 | awk '{print $3}')"
[[ -n "$reviewed" ]] || fail "PR body must state 'Reviewed commit: <sha>'."
commits="$(gh api --paginate "repos/$REPO/pulls/$PR_NUMBER/commits" --jq '.[].sha')"
grep -q "^$reviewed" <<<"$commits" || fail "Reviewed commit $reviewed is not part of this pull request."

echo "PR gate passed: branch '$HEAD_REF', Codex review of $reviewed recorded, 0 unresolved critical/high findings."
