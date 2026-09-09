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
useradd -r -s /sbin/nologin teamtools
mkdir -p /opt/teamtools/app
mkdir -p /opt/teamtools/data
chown -R teamtools:teamtools /opt/teamtools

# ── Environment file ──────────────────────────────────────────────────────────
# Values are injected by Terraform's templatefile() function at apply time.
cat > /etc/teamtools.env << ENVFILE
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
DOTNET_ROOT=/usr/local/dotnet
Database__Provider=${database_provider}
ConnectionStrings__Default=Data Source=/opt/teamtools/data/teamtools.db
Integrations__Jira__Enabled=${jira_enabled}
Integrations__Ado__Enabled=${ado_enabled}
Integrations__Jira__OAuth__ClientId=${jira_client_id}
Integrations__Jira__OAuth__ClientSecret=${jira_client_secret}
ENVFILE
chmod 600 /etc/teamtools.env

# ── systemd unit ─────────────────────────────────────────────────────────────
cat > /etc/systemd/system/teamtools.service << 'UNIT'
[Unit]
Description=TeamTools API
After=network.target

[Service]
Type=simple
User=teamtools
WorkingDirectory=/opt/teamtools/app
ExecStart=/usr/local/dotnet/dotnet /opt/teamtools/app/TeamTools.Api.dll
Restart=always
RestartSec=10
EnvironmentFile=/etc/teamtools.env
StandardOutput=journal
StandardError=journal
SyslogIdentifier=teamtools

# Prevent the service crashing the host if the SQLite file is missing on first boot.
StartLimitIntervalSec=60
StartLimitBurst=5

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
# Enable but do not start — the app binary is deployed separately via deploy-backend.sh.
systemctl enable teamtools

echo "EC2 bootstrap complete. Deploy the app with deploy-backend.sh."
