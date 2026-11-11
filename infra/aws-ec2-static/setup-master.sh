#!/usr/bin/env bash
# =============================================================================
# Setup script for the Master VM (Control Plane VM #0)
# Usage on Master Ubuntu 24.04 EC2 VM:
#   chmod +x setup-master.sh
#   sudo MASTER_IP=10.0.1.10 \
#        WORKER_1_IP=10.0.1.11 WORKER_2_IP=10.0.1.12 WORKER_3_IP=10.0.1.13 WORKER_4_IP=10.0.1.14 \
#        WORKER_5_IP=10.0.1.15 WORKER_6_IP=10.0.1.16 WORKER_7_IP=10.0.1.17 WORKER_8_IP=10.0.1.18 \
#        ./setup-master.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v docker >/dev/null 2>&1; then
  echo "[1/4] Installing Docker and NFS Server..."
  apt-get update
  apt-get install -y ca-certificates curl git nfs-kernel-server
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

echo "[2/4] Preparing shared /var/lib/storage directory..."
mkdir -p /var/lib/storage
chmod 777 /var/lib/storage

if ! mountpoint -q /var/lib/storage; then
  # Export /var/lib/storage via NFSv4 if EFS is not already mounted there
  apt-get install -y nfs-kernel-server
  if ! grep -q "/var/lib/storage" /etc/exports 2>/dev/null; then
    echo "/var/lib/storage *(rw,sync,no_subtree_check,no_root_squash)" >> /etc/exports
  fi
  exportfs -ra
  systemctl enable --now nfs-kernel-server
fi

echo "[3/4] Building & starting PARCS Master stack (Postgres + Host API + Portal)..."
cd "$SCRIPT_DIR"
docker compose -f docker-compose.master.yml up -d --build

echo "[4/4] Done! PARCS Portal is available on port 80 and Host API Swagger on port 5000."
