#!/usr/bin/env bash
# Registers, uploads a document, waits for it to be indexed, searches it and asks a question, against a running stack.
# Usage: tests/smoke/ask.sh [api-address]   (prints the answer)
set -euo pipefail
api=${1:-http://localhost:8080}
email="smoke-$(date +%s%N)@example.com"
token=$(curl -sf "$api/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"displayName\":\"Smoke\",\"password\":\"smoke-test-password\"}" | jq -r .accessToken)
auth=(-H "Authorization: Bearer $token")
workspace=$(curl -sf "$api/api/me" "${auth[@]}" | jq -r '.workspaces[0].id')

file=$(mktemp --suffix .md)
cat > "$file" <<'MD'
# Returns policy

Customers can send back any product within 14 days of delivery.

## Refunds

Refunds are paid back to the original card within 3 working days of the parcel reaching our warehouse.

## Exchanges

A grinder or kettle can be exchanged for another model once per order, free of charge.
MD
document=$(curl -sf "$api/api/workspaces/$workspace/documents" "${auth[@]}" -F "file=@$file;filename=returns.md" | jq -r .id)

for i in $(seq 1 120); do
  status=$(curl -sf "$api/api/workspaces/$workspace/documents/$document" "${auth[@]}" | jq -r .status)
  [ "$status" = "Ready" ] && break
  [ "$status" = "Failed" ] && { echo "indexing failed"; exit 1; }
  sleep 1
done
[ "$status" = "Ready" ] || { echo "not indexed after 120 s"; exit 1; }

curl -sf "$api/api/workspaces/$workspace/search?q=how%20long%20do%20refunds%20take" "${auth[@]}" | jq -e '.hits[0].heading == "Refunds"' > /dev/null

conversation=$(curl -sf "$api/api/workspaces/$workspace/conversations" "${auth[@]}" -H 'Content-Type: application/json' -d '{}' | jq -r .id)
answer=$(curl -sf "$api/api/workspaces/$workspace/conversations/$conversation/messages" "${auth[@]}" -H 'Content-Type: application/json' \
  -d '{"question":"When are refunds paid back to the card?"}')
echo "$answer" | jq -r '.answer | "\(.model) · \(.outcome) · \(.durationMs) ms\n\(.content)\ncited: \([.citations[] | .heading] | join(", "))"'
echo "$answer" | jq -e '.answer.outcome == "Answered" and (.answer.citations | map(.heading) | index("Refunds")) != null' > /dev/null

outside=$(curl -sf "$api/api/workspaces/$workspace/conversations/$conversation/messages" "${auth[@]}" -H 'Content-Type: application/json' \
  -d '{"question":"Who painted the Mona Lisa?"}')
echo "$outside" | jq -e '.answer.outcome == "NotFound"' > /dev/null
echo "Indexed, found, answered with a citation, and a question outside the documents was not answered."
