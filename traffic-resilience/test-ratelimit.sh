#!/usr/bin/env bash
set -euo pipefail

URL="${1:-http://localhost:8080/api/v1/users}"
REQUEST_COUNT="${2:-20}"
AUTH_URL="${3:-http://localhost:8081/login}"

echo "Fetching a token from $AUTH_URL ..."
TOKEN=$(curl -s -X POST "$AUTH_URL" -H "Content-Type: application/json" -d '{"username":"alice","password":"password123"}' | grep -o '"token":"[^"]*"' | cut -d'"' -f4)

if [ -z "$TOKEN" ]; then
  echo "Failed to obtain a token from $AUTH_URL - is auth-service running?"
  exit 1
fi

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

echo "Firing $REQUEST_COUNT requests at $URL CONCURRENTLY ..."
echo ""

# Launch every request as a background job nearly simultaneously. Each job
# runs in its own subshell/process, so it can't write back to this script's
# variables directly - it writes its status code to its own file instead,
# and we read all the files back after `wait` returns.
for i in $(seq 1 "$REQUEST_COUNT"); do
  (
    sent=$(date +"%H:%M:%S.%3N")
    status=$(curl -s -o /dev/null -w "%{http_code}" "$URL" -H "Authorization: Bearer $TOKEN")
    received=$(date +"%H:%M:%S.%3N")
    echo "$status $sent $received" > "$TMP_DIR/$i"
  ) &
done

wait

declare -A status_counts
passed=0
failed=0

for i in $(seq 1 "$REQUEST_COUNT"); do
  read -r status sent received < "$TMP_DIR/$i"
  status_counts[$status]=$(( ${status_counts[$status]:-0} + 1 ))

  if [ "$status" = "429" ]; then
    failed=$((failed + 1))
    echo "  req #$i -> $status (rate limited) | sent=$sent received=$received"
  else
    passed=$((passed + 1))
    echo "  req #$i -> $status | sent=$sent received=$received"
  fi
done

echo ""
echo "=== Rate Limit Test Report ==="
echo "Target              : $URL"
echo "Total requests sent : $REQUEST_COUNT"
echo "Passed (not 429)    : $passed"
echo "Failed (429)        : $failed"
echo ""
echo "Breakdown by status code:"
for code in "${!status_counts[@]}"; do
  echo "  $code : ${status_counts[$code]}"
done
