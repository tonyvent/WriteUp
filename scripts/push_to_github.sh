#!/usr/bin/env bash
# Create a GitHub repo and push this project to it.
# Usage:  ./scripts/push_to_github.sh <repo-name> [--private]
set -euo pipefail

REPO="${1:-process-scribe}"
VIS="public"
[[ "${2:-}" == "--private" ]] && VIS="private"

if command -v gh >/dev/null 2>&1; then
  # Easiest path: GitHub CLI handles repo creation + remote + push.
  gh repo create "$REPO" --source=. --"$VIS" --push
else
  echo "GitHub CLI (gh) not found."
  echo "1) Create an empty repo named '$REPO' at https://github.com/new"
  echo "2) Then run:"
  echo "     git remote add origin https://github.com/<you>/$REPO.git"
  echo "     git branch -M main"
  echo "     git push -u origin main"
fi
