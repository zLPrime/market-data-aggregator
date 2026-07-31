-- Canonical schema for the aggregator's tick store.
-- Applied by ops when provisioning the database; the integration test loads this same
-- file so the test table can never drift from production.

CREATE TABLE IF NOT EXISTS ticks (
    source  text        NOT NULL,
    ticker  text        NOT NULL,
    price   numeric     NOT NULL,
    volume  numeric     NOT NULL,
    ts      timestamptz NOT NULL
);
