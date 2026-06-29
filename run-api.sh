#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
LOCAL_DATA_DIR="$REPO_ROOT/.localdata"

if [ -f "$REPO_ROOT/.env.local" ]; then
  unset AI_PROVIDER
  unset AI_MODEL
  unset ANTHROPIC_API_KEY
  unset OPENROUTER_API_KEY
  set -a
  . "$REPO_ROOT/.env.local"
  set +a
fi

mkdir -p "$LOCAL_DATA_DIR/markdown"

export DB_PATH="${DB_PATH:-$LOCAL_DATA_DIR/dossier.db}"
export ADMIN_KEY="${ADMIN_KEY:-localtest}"
export PIPELINE_MIN_SECTIONS="${PIPELINE_MIN_SECTIONS:-5}"
export PIPELINE_MIN_ENTRIES="${PIPELINE_MIN_ENTRIES:-5}"
export CORS_ORIGIN="${CORS_ORIGIN:-http://localhost:4200}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://+:5000}"
export API_BASE_URL="${API_BASE_URL:-http://localhost:5000}"

case "$DB_PATH" in
  /*) ;;
  *) export DB_PATH="$REPO_ROOT/$DB_PATH" ;;
esac

if [ -z "$AI_PROVIDER" ]; then
  echo "ERROR: AI_PROVIDER is not set. Set it in .env.local or your environment."
  exit 1
fi

if [ -z "$AI_MODEL" ]; then
  echo "ERROR: AI_MODEL is not set. Set it in .env.local or your environment."
  exit 1
fi

if [ -n "$ANTHROPIC_API_KEY" ] && [ -n "$OPENROUTER_API_KEY" ]; then
  echo "ERROR: Set only one provider key. Do not set both ANTHROPIC_API_KEY and OPENROUTER_API_KEY."
  exit 1
fi

case "$AI_PROVIDER" in
  anthropic)
    if [ -z "$ANTHROPIC_API_KEY" ]; then
      echo "ERROR: AI_PROVIDER=anthropic requires ANTHROPIC_API_KEY."
      exit 1
    fi
    ;;
  openrouter)
    if [ -z "$OPENROUTER_API_KEY" ]; then
      echo "ERROR: AI_PROVIDER=openrouter requires OPENROUTER_API_KEY."
      exit 1
    fi
    ;;
  *)
    echo "ERROR: AI_PROVIDER must be 'anthropic' or 'openrouter'."
    exit 1
    ;;
esac

echo "Starting API..."
cd "$REPO_ROOT/api"
dotnet run
