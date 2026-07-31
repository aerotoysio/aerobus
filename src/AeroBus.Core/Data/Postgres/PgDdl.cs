namespace AeroBus.Core.Data.Postgres
{
    /// <summary>
    /// Idempotent per-schema DDL (docs/storage-architecture.md): promoted,
    /// indexed columns for everything queried, plus a <c>doc jsonb</c> holding
    /// the aggregate exactly as the wire serialises it. Inventory and counters
    /// are fully relational — they exist to be updated atomically.
    /// Additive changes only; every statement must stay IF NOT EXISTS-safe.
    /// </summary>
    public static class PgDdl
    {
        public const string Tables = """
            CREATE TABLE IF NOT EXISTS customers (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                customer_number text,
                first_name    text,
                last_name     text,
                email         text,
                phone         text,
                loyalty_program text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_customers_email ON customers (lower(email));
            CREATE INDEX IF NOT EXISTS ix_customers_last_name ON customers (lower(last_name));
            CREATE INDEX IF NOT EXISTS ix_customers_number ON customers (customer_number);

            CREATE TABLE IF NOT EXISTS orders (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                order_id      text,
                status        text,
                profile_id    uuid,
                channel       text,
                currency      text,
                total_amount  numeric,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_orders_order_id ON orders (order_id);
            CREATE INDEX IF NOT EXISTS ix_orders_created ON orders (created DESC);
            CREATE INDEX IF NOT EXISTS ix_orders_profile ON orders (profile_id);
            CREATE INDEX IF NOT EXISTS ix_orders_status ON orders (status);

            CREATE TABLE IF NOT EXISTS flight_inventory (
                flight_id     uuid NOT NULL,
                bucket        text NOT NULL,
                available     integer NOT NULL,
                sold          integer NOT NULL DEFAULT 0,
                updated       timestamptz,
                PRIMARY KEY (flight_id, bucket)
            );

            CREATE TABLE IF NOT EXISTS order_counters (
                key           text PRIMARY KEY,
                value         bigint NOT NULL
            );

            CREATE TABLE IF NOT EXISTS airports (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                code          text,
                name          text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_airports_code ON airports (code);

            CREATE TABLE IF NOT EXISTS schedules (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                flight_number text,
                departure_station text,
                arrival_station   text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );

            CREATE TABLE IF NOT EXISTS flights (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                schedule_id   uuid,
                flight_number text,
                departure_station text,
                arrival_station   text,
                departure_dt_local timestamptz,
                departure_dt  timestamptz,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_flights_departure ON flights (departure_dt_local);
            CREATE INDEX IF NOT EXISTS ix_flights_route ON flights (departure_station, arrival_station);
            CREATE INDEX IF NOT EXISTS ix_flights_schedule ON flights (schedule_id);

            CREATE TABLE IF NOT EXISTS market_zones (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                name          text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );

            CREATE TABLE IF NOT EXISTS products (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                code          text,
                name          text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_products_code ON products (code);

            CREATE TABLE IF NOT EXISTS bundles (
                id            uuid PRIMARY KEY,
                company_id    uuid,
                type          text,
                name          text,
                status        text,
                created       timestamptz,
                updated       timestamptz,
                doc           jsonb NOT NULL
            );
            """;
    }
}
