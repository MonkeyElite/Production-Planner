#!/usr/bin/env bash
set -e

# Where we'll copy the SSL files to (inside the container)
SSL_DIR="/var/lib/postgresql/ssl"
SECRETS_DIR="/run/postgres-secrets"

mkdir -p "$SSL_DIR"

# Copy files from bind-mounted secrets into SSL_DIR with correct perms
if [ -f "$SECRETS_DIR/server.key" ]; then
  install -o postgres -g postgres -m 600 "$SECRETS_DIR/server.key" "$SSL_DIR/server.key"
fi

if [ -f "$SECRETS_DIR/server.crt" ]; then
  install -o postgres -g postgres -m 644 "$SECRETS_DIR/server.crt" "$SSL_DIR/server.crt"
fi

if [ -f "$SECRETS_DIR/root.crt" ]; then
  install -o postgres -g postgres -m 644 "$SECRETS_DIR/root.crt" "$SSL_DIR/root.crt"
fi

if [ -f "$SECRETS_DIR/pg_hba.conf" ]; then
  install -o postgres -g postgres -m 644 "$SECRETS_DIR/pg_hba.conf" "/var/lib/postgresql/pg_hba.conf"
fi

# Hand off to the original entrypoint with SSL options
exec docker-entrypoint.sh "$@" \
  -c ssl=on \
  -c ssl_cert_file="$SSL_DIR/server.crt" \
  -c ssl_key_file="$SSL_DIR/server.key" \
  -c ssl_ca_file="$SSL_DIR/root.crt" \
  -c hba_file=/var/lib/postgresql/pg_hba.conf
