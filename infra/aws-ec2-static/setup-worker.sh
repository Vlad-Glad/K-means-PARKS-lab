#!/usr/bin/env bash
# =============================================================================
# Setup script for each Worker VM (#1..#8)
# Usage on each Worker Ubuntu 24.04 EC2 VM:
#   chmod +x setup-worker.sh
#   sudo MASTER_IP=10.0.1.10 WORKER_IP=$(hostname -I | awk '{print $1}') ./setup-worker.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MASTER_IP="${MASTER_IP:-10.0.1.10}"
WORKER_IP="${WORKER_IP:-$(hostname -I | awk '{print $1}')}"

if ! command -v docker >/dev/null 2>&1; then
  echo "[1/3] Installing Docker and NFS client..."
  apt-get update
  apt-get install -y ca-certificates curl git nfs-common
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

echo "[2/3] Mounting shared /var/lib/storage from Master ($MASTER_IP)..."
mkdir -p /var/lib/storage
if ! mountpoint -q /var/lib/storage; then
  mount -t nfs4 "${MASTER_IP}:/var/lib/storage" /var/lib/storage
fi

echo "[3/3] Building & starting PARCS Daemon on $WORKER_IP:1111 (1 PARCS Point)..."
cd "$SCRIPT_DIR"
export MASTER_IP WORKER_IP
docker compose -f docker-compose.worker.yml up -d --build

echo "Done! Worker daemon is listening on ${WORKER_IP}:1111."
