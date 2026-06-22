#!/usr/bin/env bash
# Build the Angular SPA and sync it to S3, then invalidate CloudFront.
# Usage: bash deploy/scripts/deploy-frontend.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TF_DIR="$SCRIPT_DIR/../terraform"
FRONTEND_DIR="$REPO_ROOT/frontend"
DIST_DIR="$FRONTEND_DIR/dist/frontend/browser"

# Pull resource names from Terraform state.
S3_BUCKET="$(cd "$TF_DIR" && terraform output -raw s3_bucket_name)"
CF_ID="$(cd "$TF_DIR" && terraform output -raw cloudfront_distribution_id)"

echo "==> Building Angular frontend (production)"
cd "$FRONTEND_DIR"
npm ci --prefer-offline
npm run build -- --configuration production

# Verify the build produced the expected output directory.
if [[ ! -f "$DIST_DIR/index.html" ]]; then
  echo "ERROR: Expected dist at $DIST_DIR — check angular.json outputPath."
  exit 1
fi

echo "==> Uploading hashed assets (immutable, 1-year cache)"
# Hashed filenames (e.g. main.abc123.js) never change for the same content,
# so they can be cached aggressively.
aws s3 sync "$DIST_DIR/" "s3://$S3_BUCKET/" \
  --delete \
  --cache-control "public, max-age=31536000, immutable" \
  --exclude "index.html" \
  --exclude "config.js"

echo "==> Uploading index.html and config.js (no-cache)"
# These files are the SPA entry points — browsers must always re-fetch them
# so they pick up the latest hashed asset references.
aws s3 cp "$DIST_DIR/index.html" "s3://$S3_BUCKET/index.html" \
  --cache-control "no-cache, no-store, must-revalidate" \
  --content-type "text/html"

# config.js may not exist if the public/ copy was skipped; create a no-op one.
if [[ -f "$DIST_DIR/config.js" ]]; then
  aws s3 cp "$DIST_DIR/config.js" "s3://$S3_BUCKET/config.js" \
    --cache-control "no-cache, no-store, must-revalidate" \
    --content-type "application/javascript"
fi

echo "==> Invalidating CloudFront edge cache"
aws cloudfront create-invalidation \
  --distribution-id "$CF_ID" \
  --paths "/index.html" "/config.js" \
  --query 'Invalidation.Id' \
  --output text

echo "==> Frontend deployed to s3://$S3_BUCKET"
