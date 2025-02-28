CREATE TABLE IF NOT EXISTS public.trend_test
(
    id integer NOT NULL GENERATED ALWAYS AS IDENTITY ( INCREMENT 1 START 1 MINVALUE 1 MAXVALUE 2147483647 CACHE 1 ),
    "timestamp" timestamp with time zone DEFAULT now(),
    setpoint real,
    actual real,
    act_range real[],
    is_valid boolean
)