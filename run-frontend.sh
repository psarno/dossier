#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"

export API_BASE_URL="${API_BASE_URL:-http://localhost:5000}"

echo "Starting frontend dev server on http://localhost:4200"
echo "API_BASE_URL=$API_BASE_URL"

cd "$REPO_ROOT/client"
npx ng serve
