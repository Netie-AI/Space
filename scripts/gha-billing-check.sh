#!/usr/bin/env bash
# Latest GitHub Actions CI annotation. Exit 1 if hosted runners never started.
set -euo pipefail
cd "$(dirname "$0")/.."

repo="${GITHUB_REPOSITORY:-Netie-AI/Space}"
run_id="$(gh run list --repo "$repo" --workflow=ci.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
if [[ -z "$run_id" || "$run_id" == "null" ]]; then
  echo "gha-billing-check: no CI runs"
  exit 1
fi

job_id="$(gh api "repos/${repo}/actions/runs/${run_id}/jobs" --jq '.jobs[0].id')"
ann="$(gh api "repos/${repo}/check-runs/${job_id}/annotations" --jq '.[0].message // empty')"
echo "run=${run_id} job=${job_id}"
echo "$ann"

if [[ "$ann" == *"spending limit needs to be increased"* ]] \
  || [[ "$ann" == *"insufficient minutes"* ]] \
  || [[ "$ann" == *"job was not started"* ]]; then
  echo "GHA hosted runners are billing-locked. Do not merge. docs/HUMAN_GHA_BILLING.md"
  exit 1
fi
