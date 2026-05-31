#!/usr/bin/env bash
set -e

psql -v ON_ERROR_STOP=1 \
     -v db_name="$POSTGRES_DB" \
     -v db_username="$POSTGRES_DB_USERNAME" \
     -v db_password="$POSTGRES_DB_PASSWORD" \
     -U postgres -d "$POSTGRES_DB" <<-'SQL'
CREATE USER :"db_username" WITH PASSWORD :'db_password';
GRANT ALL PRIVILEGES ON DATABASE :"db_name" TO :"db_username";
GRANT USAGE, CREATE ON SCHEMA public TO :"db_username";
SQL
