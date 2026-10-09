#!/bin/sh
# Runs once, when the database volume is first created (docker-entrypoint-initdb.d).
# POSTGRES_USER (xd_owner) owns the database and runs migrations; xd_app is the least-privilege runtime role.
set -eu

APP_PASSWORD=$(cat /run/secrets/Database__Password)

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
CREATE ROLE xd_app LOGIN PASSWORD '${APP_PASSWORD}';
GRANT CONNECT ON DATABASE "${POSTGRES_DB}" TO xd_app;

-- Every table/sequence created by the owner (via migrations) is usable by the runtime role. No DDL rights.
ALTER DEFAULT PRIVILEGES FOR ROLE "${POSTGRES_USER}" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app;
ALTER DEFAULT PRIVILEGES FOR ROLE "${POSTGRES_USER}" GRANT USAGE, SELECT ON SEQUENCES TO xd_app;

-- Keep the public schema closed: modules use their own schemas.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
