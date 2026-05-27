create table if not exists durable_stack_jobs (
    id uuid primary key,
    name text not null unique,
    job_type text not null,
    schedule_type text not null,
    cron_expression text null,
    time_zone text null,
    enabled boolean not null default true,
    payload_json jsonb null,
    max_attempts int not null default 3,
    retry_backoff_seconds int not null default 60,
    next_run_at_utc timestamptz null,
    last_run_at_utc timestamptz null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create table if not exists durable_stack_job_runs (
    id uuid primary key,
    job_id uuid null references durable_stack_jobs(id),
    job_name text not null,
    job_type text not null,
    status text not null,
    payload_json jsonb null,
    scheduled_for_utc timestamptz not null,
    started_at_utc timestamptz null,
    completed_at_utc timestamptz null,
    attempt int not null default 0,
    max_attempts int not null default 3,
    lease_owner text null,
    lease_until_utc timestamptz null,
    error_message text null,
    error_detail text null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create index if not exists ix_durable_stack_job_runs_due
on durable_stack_job_runs (status, scheduled_for_utc);

create index if not exists ix_durable_stack_job_runs_lease
on durable_stack_job_runs (lease_until_utc);

create index if not exists ix_durable_stack_job_runs_job_name
on durable_stack_job_runs (job_name);

create table if not exists durable_stack_job_locks (
    lock_key text primary key,
    owner text not null,
    lease_until_utc timestamptz not null,
    updated_at_utc timestamptz not null default now()
);
