using Npgsql;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres;

/// <summary>
/// Applies small numbered PostgreSQL schema migrations, never changing an existing one.
/// </summary>
public static class PostgresSchema
{
    public const int KnowledgeMigrationVersion = 6;

    public static IReadOnlySet<int> OptionalVersions { get; } = new HashSet<int> { KnowledgeMigrationVersion };

    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, @"
            create table history (
                id uuid primary key default gen_random_uuid(),
                owner uuid not null,
                question text not null,
                entity text not null,
                fetch_xml text not null,
                created_at timestamptz not null default now(),
                title text null,
                starred boolean not null default false
            );
            create index history_owner_created on history (owner, created_at desc);
            create index history_owner_starred on history (owner, created_at desc) where starred;"),
        (2, @"
            create table ai_calls (
                id bigserial primary key,
                request_id uuid not null,
                at timestamptz not null,
                purpose text not null,
                provider text not null,
                model text not null,
                duration_ms bigint not null,
                input_tokens int null,
                output_tokens int null,
                outcome text not null,
                error_type text null
            );
            create index ai_calls_at on ai_calls (at);
            create table requests (
                id uuid primary key,
                at timestamptz not null,
                owner uuid null,
                duration_ms bigint not null,
                outcome text not null,
                error_type text null
            );
            create index requests_at on requests (at);"),
        (3, @"
            create table admin_users (
                account text primary key,
                display_name text not null,
                active boolean not null,
                created_at timestamptz not null,
                created_by text not null
            );
            create table admin_roles (
                id uuid primary key,
                name text not null,
                builtin boolean not null default false,
                created_at timestamptz not null default now()
            );
            create unique index admin_roles_name_lower on admin_roles (lower(name));
            create table admin_role_permissions (
                role_id uuid not null references admin_roles (id) on delete cascade,
                permission text not null,
                primary key (role_id, permission)
            );
            create table admin_user_roles (
                account text not null references admin_users (account) on delete cascade,
                role_id uuid not null references admin_roles (id) on delete cascade,
                primary key (account, role_id)
            );
            create table admin_audit (
                id bigserial primary key,
                at timestamptz not null,
                actor text not null,
                action text not null,
                target text not null,
                detail text not null
            );
            create index admin_audit_at on admin_audit (at desc);
            create index admin_audit_actor on admin_audit (actor);
            insert into admin_roles (id, name, builtin) values ('00000000-0000-0000-0000-0000000000a1', 'Administrator', true);"),
        (4, @"
            create table app_logs (
                id bigserial primary key,
                at timestamptz not null,
                level int not null,
                message text not null,
                exception text null,
                request_id text null
            );
            create index app_logs_at on app_logs (at desc);"),
        (5, @"
            create table admin_groups (
                group_key text primary key,
                display_name text not null
            );
            create table admin_group_roles (
                group_key text not null references admin_groups (group_key) on delete cascade,
                role_id uuid not null references admin_roles (id) on delete cascade,
                primary key (group_key, role_id)
            );"),
        (6, @"
            create extension if not exists vector;
            create table knowledge_documents (
                id uuid primary key,
                title text not null,
                body text not null,
                enabled boolean not null default true,
                updated_by text not null,
                updated_at timestamptz not null
            );
            create table knowledge_chunks (
                document_id uuid not null references knowledge_documents (id) on delete cascade,
                chunk_index int not null,
                text text not null,
                embedding vector(1024) not null,
                tsv tsvector generated always as (to_tsvector('simple', text) || to_tsvector('turkish', text)) stored,
                primary key (document_id, chunk_index)
            );
            create index knowledge_chunks_tsv on knowledge_chunks using gin (tsv);
            create index knowledge_chunks_embedding on knowledge_chunks using hnsw (embedding vector_cosine_ops);"),
        (7, @"
            create table runtime_settings (
                key text primary key,
                value text not null,
                updated_by text not null,
                updated_at timestamptz not null
            );"),
        (8, @"
            alter table requests add column knowledge_pieces int null;"),
        (9, @"
            alter table history add column parent_id uuid null;
            alter table requests add column follow_up boolean null;"),
        (10, @"
            create table conversations (
                id uuid primary key default gen_random_uuid(),
                owner uuid not null,
                title text not null,
                pinned boolean not null default false,
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now()
            );
            create index conversations_owner_order on conversations (owner, pinned desc, updated_at desc, id desc);
            alter table history add column conversation_id uuid null references conversations (id) on delete cascade;
            create index history_conversation on history (conversation_id, created_at, id);

            create temp table chain_members on commit drop as
            with recursive chain as (
                select h.id as entry_id, h.id as root_id from history h
                where h.parent_id is null or not exists (select 1 from history p where p.id = h.parent_id)
                union all
                select c.id, ch.root_id from history c join chain ch on c.parent_id = ch.entry_id
            )
            select entry_id, root_id from chain;

            create temp table chain_roots on commit drop as
            select root_id, gen_random_uuid() as conv_id from (select distinct root_id from chain_members) r;

            insert into conversations (id, owner, title, pinned, created_at, updated_at)
            select cr.conv_id, rh.owner,
                   case when nullif(btrim(coalesce(rh.title, '')), '') is not null then left(btrim(rh.title), 60)
                        else left(regexp_replace(btrim(rh.question), '[[:space:]]+', ' ', 'g'), 40) end,
                   s.any_starred, s.first_at, s.last_at
            from chain_roots cr
            join history rh on rh.id = cr.root_id
            join (select cm.root_id, bool_or(m.starred) as any_starred, min(m.created_at) as first_at, max(m.created_at) as last_at
                  from chain_members cm join history m on m.id = cm.entry_id group by cm.root_id) s on s.root_id = cr.root_id;

            update history h set conversation_id = cr.conv_id
            from chain_members cm join chain_roots cr on cr.root_id = cm.root_id
            where h.id = cm.entry_id;"),
        (11, @"
            create table user_preferences (
                owner uuid primary key,
                display_name text null,
                updated_at timestamptz not null default now()
            );"),
        (12, @"
            create table data_access_log (
                id uuid primary key,
                at timestamptz not null,
                user_id uuid not null,
                source text not null,
                tables text not null,
                rows int not null,
                outcome text not null,
                request_id uuid not null
            );
            create index data_access_log_at on data_access_log (at desc);
            create index data_access_log_user on data_access_log (user_id, at desc);
            create index data_access_log_request on data_access_log (request_id);
            create function data_access_log_guard() returns trigger as $$
            begin
                if tg_op = 'UPDATE' then
                    raise exception 'data_access_log is append-only';
                end if;
                if tg_op = 'DELETE' and coalesce(current_setting('nlquery.purge', true), '') <> 'on' then
                    raise exception 'data_access_log rows can only be deleted by the retention purge';
                end if;
                if tg_op = 'TRUNCATE' then
                    raise exception 'data_access_log cannot be truncated';
                end if;
                if tg_op = 'DELETE' then
                    return old;
                end if;
                return new;
            end;
            $$ language plpgsql;
            create trigger data_access_log_rows before update or delete on data_access_log
                for each row execute function data_access_log_guard();
            create trigger data_access_log_truncate before truncate on data_access_log
                for each statement execute function data_access_log_guard();"),
        (13, @"
            alter table history add column admin_viewable boolean not null default false;
            create index history_admin_viewable on history (owner, created_at desc) where admin_viewable;"),
        (14, @"
            alter table ai_calls add column prompt_chars int null;
            alter table ai_calls add column est_tokens int null;"),
        (15, @"
            create table data_policy (
                kind text not null,
                entity text not null,
                attribute text not null default '',
                allowed boolean not null,
                set_by text not null,
                set_at timestamptz not null,
                primary key (kind, entity, attribute)
            );"),
        (16, @"
            alter table history add column steps int null;
            alter table history add column duration_ms int null;
            create table feedback (
                result_id uuid primary key,
                owner uuid not null,
                rating text not null,
                reason text null,
                shared boolean not null default false,
                tables text[] not null default '{}',
                steps int null,
                duration_ms int null,
                created_at timestamptz not null,
                updated_at timestamptz not null
            );
            create index feedback_created on feedback (created_at);
            create table feedback_candidates (
                result_id uuid primary key references history (id) on delete cascade,
                owner uuid not null,
                question text not null,
                explanation text not null,
                reason text null,
                created_at timestamptz not null
            );"),
        (17, @"
            alter table feedback alter column owner drop not null;
            create function feedback_anonymize() returns trigger as $$
            begin
                update feedback set owner = null where result_id = old.id and owner is not null;
                return old;
            end;
            $$ language plpgsql;
            create trigger history_feedback_anonymize before delete on history
                for each row execute function feedback_anonymize();"),
        (18, @"
            alter table history add column kind text null;"),
        (19, @"
            create table suggestion_stats (
                day date not null,
                kind text not null,
                shown int not null default 0,
                clicked int not null default 0,
                primary key (day, kind)
            );"),
        (20, @"
            create table design_jobs (
                id uuid primary key,
                owner text not null,
                status text not null,
                step text not null default '',
                percent int not null default 0,
                title text not null,
                prefix text null,
                request text not null,
                result text null,
                error text null,
                problem_count int not null default 0,
                created_at timestamptz not null,
                updated_at timestamptz not null,
                finished_at timestamptz null
            );
            create index design_jobs_owner on design_jobs (lower(owner), created_at desc);"),
        (21, @"
            alter table history add column payload text null;"),
        (22, @"
            create table user_language (
                account text primary key,
                language text not null,
                updated_at timestamptz not null default now()
            );"),
        (23, @"
            create table quality_cases (
                id uuid primary key,
                created_by text not null,
                created_at timestamptz not null,
                source text not null,
                status text not null default 'active',
                verified boolean not null default false,
                language text not null default 'en',
                question text not null,
                entity text not null,
                answers text null,
                reference_fetch text not null,
                compare text null,
                template text null,
                note text null
            );
            create index quality_cases_entity on quality_cases (entity, created_at);"),
        (24, @"
            create table quality_runs (
                id uuid primary key,
                started_by text not null,
                started_at timestamptz not null,
                finished_at timestamptz null,
                status text not null,
                repeat int not null default 1,
                total int not null default 0,
                done int not null default 0,
                summary text null,
                error text null
            );
            create index quality_runs_started on quality_runs (started_at);
            create table quality_results (
                run_id uuid not null references quality_runs (id) on delete cascade,
                case_id text not null,
                question text not null,
                entity text not null,
                verified boolean not null,
                verdict text not null,
                passes int not null,
                runs int not null,
                detail text not null,
                duration_ms int not null,
                reference_rows int null,
                fingerprint text null,
                data_changed boolean not null default false,
                primary key (run_id, case_id)
            );"),
        (25, @"
            create table glossary_entries (
                id text primary key,
                kind text not null,
                entity text not null,
                attribute text null,
                conditions text not null default '[]',
                description text not null default '',
                enabled boolean not null default true,
                created_by text not null,
                created_at timestamptz not null,
                modified_by text not null,
                modified_at timestamptz not null
            );
            create table glossary_terms (
                folded_term text primary key,
                entry_id text not null references glossary_entries (id) on delete cascade,
                term text not null,
                language text not null default 'any',
                position int not null default 0
            );
            create index glossary_terms_entry on glossary_terms (entry_id);
            create table glossary_stats (
                entry_id text primary key references glossary_entries (id) on delete cascade,
                applied int not null default 0,
                not_applied int not null default 0,
                last_not_applied_at timestamptz null
            );"),
        (26, @"
            create table report_catalog (
                id text primary key,
                name text not null,
                description text null,
                keywords text not null default '[]',
                crm_report_id text null,
                url text null,
                record_entity text null,
                record_param text null,
                enabled boolean not null default true,
                updated_by text not null,
                updated_at timestamptz not null
            );"),
        (27, @"
            create table report_stats (
                day date not null,
                name text not null,
                shown int not null default 0,
                opened int not null default 0,
                primary key (day, name)
            );"),
        // CRM-proof sign-in: one-time challenges (only hashes of the nonce and the browser secret).
        (28, @"
            create table auth_challenges (
                nonce_hash text primary key,
                secret_hash text not null,
                expires_at timestamptz not null,
                used boolean not null default false
            );"),
        // Setup wizard settings: secrets are stored encrypted.
        (29, @"
            create table app_config (
                key text primary key,
                value text not null,
                secret boolean not null default false,
                updated_at timestamptz not null default now()
            );"),
    };

    public static int LatestVersion => Migrations[Migrations.Length - 1].Version;

    /// <summary>
    /// Runs migrations while serializing concurrent service starts.
    /// </summary>
    public static Task<MigrationResult> MigrateAsync(string connectionString, CancellationToken ct = default)
        => MigrateAsync(connectionString, Migrations, OptionalVersions, ct);

    /// <summary>
    /// Runs the given migrations, reading applied versions as a set so skipped ones can apply later.
    /// </summary>
    public static async Task<MigrationResult> MigrateAsync(string connectionString, IReadOnlyList<(int Version, string Sql)> migrations,
        IReadOnlySet<int> optionalVersions, CancellationToken ct = default)
    {
        var skipped = new List<SkippedMigration>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await Execute(connection, "select pg_advisory_lock(727274)", ct);
        try
        {
            await Execute(connection, "create table if not exists schema_version (version int primary key, applied_at timestamptz not null default now())", ct);

            var applied = new HashSet<int>();
            await using (var read = new NpgsqlCommand("select version from schema_version", connection))
            await using (var reader = await read.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    applied.Add(reader.GetInt32(0));
                }
            }

            foreach (var (version, sql) in migrations)
            {
                if (applied.Contains(version))
                {
                    continue;
                }
                await using var transaction = await connection.BeginTransactionAsync(ct);
                try
                {
                    await using (var apply = new NpgsqlCommand(sql, connection, transaction))
                    {
                        await apply.ExecuteNonQueryAsync(ct);
                    }
                    await using (var record = new NpgsqlCommand("insert into schema_version (version) values (@v)", connection, transaction))
                    {
                        record.Parameters.AddWithValue("v", version);
                        await record.ExecuteNonQueryAsync(ct);
                    }
                    await transaction.CommitAsync(ct);
                }
                catch (PostgresException ex) when (optionalVersions.Contains(version))
                {
                    await transaction.RollbackAsync(ct);
                    skipped.Add(new SkippedMigration(version, $"{ex.SqlState}: {ex.MessageText}"));
                }
            }
        }
        finally
        {
            await Execute(connection, "select pg_advisory_unlock(727274)", ct);
        }
        return new MigrationResult(skipped);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
