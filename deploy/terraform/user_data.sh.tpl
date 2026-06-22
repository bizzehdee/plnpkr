#!/bin/bash
set -euo pipefail
exec > >(tee /var/log/user-data.log) 2>&1

# ── .NET 10 runtime ──────────────────────────────────────────────────────────
# Microsoft's install script handles all distro detection automatically.
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --runtime aspnet --install-dir /usr/local/dotnet

# Make dotnet available system-wide (needed by the systemd service).
cat > /etc/profile.d/dotnet.sh << 'DOTNETPROFILE'
export DOTNET_ROOT=/usr/local/dotnet
export PATH=$PATH:/usr/local/dotnet
DOTNETPROFILE
chmod +x /etc/profile.d/dotnet.sh

# ── App user and directories ──────────────────────────────────────────────────
useradd -r -s /sbin/nologin planningpoker
mkdir -p /opt/planningpoker/app
mkdir -p /opt/planningpoker/data
chown -R planningpoker:planningpoker /opt/planningpoker

# ── Environment file ──────────────────────────────────────────────────────────
# Values are injected by Terraform's templatefile() function at apply time.
cat > /etc/planningpoker.env << ENVFILE
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
DOTNET_ROOT=/usr/local/dotnet
Database__Provider=${database_provider}
ConnectionStrings__Default=Data Source=/opt/planningpoker/data/planningpoker.db
Integrations__Jira__Enabled=${jira_enabled}
Integrations__Ado__Enabled=${ado_enabled}
Integrations__Jira__OAuth__ClientId=${jira_client_id}
Integrations__Jira__OAuth__ClientSecret=${jira_client_secret}
ENVFILE
chmod 600 /etc/planningpoker.env

# ── systemd unit ─────────────────────────────────────────────────────────────
cat > /etc/systemd/system/planningpoker.service << 'UNIT'
[Unit]
Description=Planning Poker API
After=network.target

[Service]
Type=simple
User=planningpoker
WorkingDirectory=/opt/planningpoker/app
ExecStart=/usr/local/dotnet/dotnet /opt/planningpoker/app/PlanningPoker.Api.dll
Restart=always
RestartSec=10
EnvironmentFile=/etc/planningpoker.env
StandardOutput=journal
StandardError=journal
SyslogIdentifier=planningpoker

# Prevent the service crashing the host if the SQLite file is missing on first boot.
StartLimitIntervalSec=60
StartLimitBurst=5

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
# Enable but do not start — the app binary is deployed separately via deploy-backend.sh.
systemctl enable planningpoker

echo "EC2 bootstrap complete. Deploy the app with deploy-backend.sh."
