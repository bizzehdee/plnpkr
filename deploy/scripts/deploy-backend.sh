#!/usr/bin/env bash
# Deploy the ASP.NET Core backend to EC2.
# Usage: EC2_KEY=~/.ssh/my-key.pem bash deploy/scripts/deploy-backend.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TF_DIR="$SCRIPT_DIR/../terraform"
PUBLISH_DIR="$REPO_ROOT/publish-backend"

EC2_KEY="${EC2_KEY:-$HOME/.ssh/id_rsa}"
EC2_USER="${EC2_USER:-ec2-user}"

# Pull connection details from Terraform state so they stay in sync.
EC2_IP="$(cd "$TF_DIR" && terraform output -raw ec2_public_ip)"

echo "==> Building .NET backend (Release, no SPA)"
cd "$REPO_ROOT"
dotnet publish backend/src/PlanningPoker.Api \
  -c Release \
  -o "$PUBLISH_DIR" \
  -p:BuildSpa=false \
  --nologo

echo "==> Connecting to $EC2_USER@$EC2_IP"

# Grace-stop the running service (ignore error if it isn't running yet).
ssh -i "$EC2_KEY" \
    -o StrictHostKeyChecking=no \
    -o ConnectTimeout=30 \
    "$EC2_USER@$EC2_IP" \
    "sudo systemctl stop planningpoker 2>/dev/null || true"

echo "==> Copying files (rsync)"
rsync -az --delete \
  -e "ssh -i $EC2_KEY -o StrictHostKeyChecking=no" \
  "$PUBLISH_DIR/" \
  "$EC2_USER@$EC2_IP:/opt/planningpoker/app/"

echo "==> Fixing ownership and starting service"
ssh -i "$EC2_KEY" \
    -o StrictHostKeyChecking=no \
    "$EC2_USER@$EC2_IP" \
    "sudo chown -R planningpoker:planningpoker /opt/planningpoker/app \
     && sudo systemctl start planningpoker \
     && sleep 2 \
     && sudo systemctl status planningpoker --no-pager"

rm -rf "$PUBLISH_DIR"
echo "==> Backend deployed. Health: http://$EC2_IP:8080/health/live"
