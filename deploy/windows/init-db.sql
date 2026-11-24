-- Runs only when the PostgreSQL container initializes an empty data volume.
-- Config database (iotgateway) is created by POSTGRES_DB.
CREATE DATABASE iotgateway_ts;

\connect iotgateway_ts
CREATE EXTENSION IF NOT EXISTS timescaledb;
