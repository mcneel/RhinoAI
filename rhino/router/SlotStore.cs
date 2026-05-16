using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace RhMcp.Router;

// Cross-process slot registry backed by SQLite. Replaces the in-memory
// _children dict + _defaultGate/_macSpawnGate semaphores in earlier RhinoManager
// versions, so concurrent routers (one per Claude Code session) can't race on
// port allocation or duplicate-spawn the same per-version lead Rhino on Mac.
//
// Coordination contract:
//   - All write paths take BEGIN IMMEDIATE so a second router serialises behind
//     the first before deciding whether a slot already exists.
//   - The slot row itself IS the lock: status='launching' is a placeholder a
//     concurrent router observes and waits on; status='ready' is the live row.
//   - Liveness (pid alive + port listening) is NOT stored — it's probed by
//     RhinoManager. The DB only persists intent, never asserted health. Dead
//     rows get DELETEd by reapers; we never write status='dead'.
//
// On schema/version mismatch we drop the slots table and rebuild. Slot state
// is ephemeral runtime registry — wiping is correct on router upgrade.
public sealed class SlotStore : IDisposable
{
    private const string CurrentRouterVersion = "0.1.0";

    private readonly SqliteConnection _conn;
    private readonly ILogger<SlotStore> _log;
    private readonly object _connLock = new();

    public SlotStore(ILogger<SlotStore> log)
        : this(RouterPaths.StateDbPath, log) { }

    public SlotStore(string dbPath, ILogger<SlotStore> log)
    {
        _log = log;
        RouterPaths.EnsureDirectories();

        // Cache=Shared lets multiple connections within this process share a
        // page cache; multi-process concurrency comes from WAL + busy_timeout.
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();

        _conn = new SqliteConnection(cs);
        _conn.Open();

        // WAL gives readers + writers concurrency; busy_timeout makes the
        // driver auto-retry on SQLITE_BUSY for up to 5s before throwing.
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec("PRAGMA busy_timeout=5000;");

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        // meta is the version marker. If the stored version doesn't match the
        // router we're running, drop the slots table (it's runtime state, never
        // user data) and stamp the new version. Migrations are explicitly out
        // of scope — see commit that introduced this for rationale.
        Exec(@"CREATE TABLE IF NOT EXISTS meta (
                 key   TEXT PRIMARY KEY,
                 value TEXT NOT NULL);");

        var stored = Scalar("SELECT value FROM meta WHERE key='router_version';");
        if (stored != CurrentRouterVersion)
        {
            if (stored is not null)
            {
                _log.LogInformation("SlotStore: router version changed ({Old} -> {New}); wiping slot table.",
                    stored, CurrentRouterVersion);
            }
            Exec("DROP TABLE IF EXISTS slots;");
            Exec("DELETE FROM meta;");
        }

        // started_at unix-ms drives stale-launching reaping. router_pid lets a
        // router differentiate slots it owns from slots owned by a peer router
        // (used during shutdown — we kill only our own ready rows).
        Exec(@"CREATE TABLE IF NOT EXISTS slots (
                 slot_id     TEXT PRIMARY KEY,
                 version     TEXT NOT NULL,
                 port        INTEGER,
                 pid         INTEGER,
                 status      TEXT NOT NULL,
                 adopted     INTEGER NOT NULL,
                 started_at  INTEGER NOT NULL,
                 router_pid  INTEGER NOT NULL);");
        Exec("CREATE INDEX IF NOT EXISTS idx_slots_version_status ON slots(version, status);");
        Exec("CREATE INDEX IF NOT EXISTS idx_slots_pid            ON slots(pid);");

        // Animal-name pool lives in the DB so name allocation is cross-process
        // serialisable: picking happens inside the same transaction as the slot
        // INSERT, so two routers can't both grab 'armadillo'. Names "recycle"
        // implicitly — a name is taken iff a slot row uses it as slot_id, so
        // deletion frees it. Idx is just the seed order; first-fit favors the
        // earliest unclaimed entry, which makes test output deterministic.
        Exec(@"CREATE TABLE IF NOT EXISTS name_pool (
                 idx  INTEGER PRIMARY KEY,
                 name TEXT UNIQUE NOT NULL);");

        SeedNamePool();

        Exec("INSERT OR REPLACE INTO meta(key,value) VALUES('router_version', $v);",
             ("$v", CurrentRouterVersion));
    }

    private void SeedNamePool()
    {
        // INSERT OR IGNORE makes seeding idempotent across router restarts. The
        // pool ordering is fixed at first-seed; subsequent additions append at
        // a higher idx and only appear after the existing pool is exhausted.
        for (int i = 0; i < AnimalNames.Pool.Length; i++)
        {
            Exec("INSERT OR IGNORE INTO name_pool(idx, name) VALUES($i, $n);",
                 ("$i", i), ("$n", AnimalNames.Pool[i]));
        }
    }

    // Atomic "is this slot taken, or can I claim it?" The returned reservation
    // tells the caller which of three branches to follow:
    //   Existing  — slot row was already there; caller waits/uses as-is.
    //   Leader    — placeholder inserted AND no other row for this version
    //               existed; caller does the full Rhino launch (Windows: always;
    //               Mac: launches Rhino.app).
    //   Follower  — placeholder inserted but another row for this version is
    //               present; caller waits for that row to become ready and
    //               then calls _router_spawn_listener against it. Mac-only path.
    //
    // The leader/follower decision happens INSIDE the BEGIN IMMEDIATE, so two
    // concurrent reservations can't both decide they're the leader.
    public SlotReservation Reserve(string slotId, string version, int routerPid)
    {
        lock (_connLock)
        {
            using var tx = _conn.BeginTransaction(deferred: false); // BEGIN IMMEDIATE

            var existing = QueryOne(tx, "SELECT * FROM slots WHERE slot_id=$s;", ("$s", slotId));
            if (existing is not null)
            {
                tx.Commit();
                return SlotReservation.Existing(existing);
            }

            var hasPeerForVersion = ScalarLong(tx,
                "SELECT COUNT(*) FROM slots WHERE version=$v AND slot_id != $s;",
                ("$v", version), ("$s", slotId)) > 0;

            Exec(tx, @"INSERT INTO slots(slot_id, version, status, adopted, started_at, router_pid)
                       VALUES($s, $v, 'launching', 0, $t, $r);",
                 ("$s", slotId), ("$v", version),
                 ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                 ("$r", routerPid));

            tx.Commit();

            return hasPeerForVersion
                ? SlotReservation.Follower(slotId, version)
                : SlotReservation.Leader(slotId, version);
        }
    }

    // Pick an unused animal name AND insert the placeholder in one transaction
    // so concurrent routers can't both claim the same name. Falls back to
    // 'slot-N' (N = max numeric suffix of existing slot-N ids, + 1) when the
    // pool is exhausted. Returns the chosen slot_id alongside the reservation.
    public (SlotReservation reservation, string slotId) ReserveNewNamed(string version, int routerPid)
    {
        lock (_connLock)
        {
            using var tx = _conn.BeginTransaction(deferred: false);

            string? picked;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"SELECT name FROM name_pool
                                    WHERE name NOT IN (SELECT slot_id FROM slots)
                                    ORDER BY idx ASC LIMIT 1;";
                picked = cmd.ExecuteScalar() as string;
            }

            string slotId = picked ?? FallbackNumberedSlot(tx);

            var hasPeerForVersion = ScalarLong(tx,
                "SELECT COUNT(*) FROM slots WHERE version=$v;",
                ("$v", version)) > 0;

            Exec(tx, @"INSERT INTO slots(slot_id, version, status, adopted, started_at, router_pid)
                       VALUES($s, $v, 'launching', 0, $t, $r);",
                 ("$s", slotId), ("$v", version),
                 ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                 ("$r", routerPid));

            tx.Commit();

            var reservation = hasPeerForVersion
                ? SlotReservation.Follower(slotId, version)
                : SlotReservation.Leader(slotId, version);
            return (reservation, slotId);
        }
    }

    // Caller already holds the immediate transaction. Walks slot_ids matching
    // 'slot-<n>' and returns one greater than the highest n seen. No regex in
    // SQLite-stock; we scan in C# because the pool is tiny.
    private string FallbackNumberedSlot(SqliteTransaction tx)
    {
        int max = 0;
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT slot_id FROM slots WHERE slot_id LIKE 'slot-%';";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            if (int.TryParse(id.AsSpan("slot-".Length), out var n) && n > max) max = n;
        }
        return $"slot-{max + 1}";
    }

    // Pick a free port AND record it on our placeholder in one transaction so
    // concurrent routers can't both choose the same port. Caller passes a
    // probe that confirms the OS isn't listening on the candidate; this method
    // only excludes ports already claimed in the DB.
    public int ReservePort(string slotId, int basePort, Func<int, bool> isPortListening)
    {
        lock (_connLock)
        {
            using var tx = _conn.BeginTransaction(deferred: false);

            var taken = new HashSet<int>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT port FROM slots WHERE port IS NOT NULL;";
                using var r = cmd.ExecuteReader();
                while (r.Read()) taken.Add(r.GetInt32(0));
            }

            for (int p = basePort; p < 65000; p++)
            {
                if (taken.Contains(p)) continue;
                if (isPortListening(p)) continue;
                Exec(tx, "UPDATE slots SET port=$p WHERE slot_id=$s;", ("$p", p), ("$s", slotId));
                tx.Commit();
                return p;
            }

            tx.Rollback();
            throw new InvalidOperationException("No free ports available in spawn range.");
        }
    }

    public ChildRhino? WaitForReady(string slotId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var row = Get(slotId);
            if (row is null) return null;
            if (row.Status == SlotStatus.Ready) return row;
            Thread.Sleep(200);
        }
        return null;
    }

    public ChildRhino? FindReadyLead(string version, string excludingSlotId)
    {
        lock (_connLock)
        {
            return QueryOne(null,
                "SELECT * FROM slots WHERE version=$v AND status='ready' AND slot_id != $s ORDER BY started_at ASC LIMIT 1;",
                ("$v", version), ("$s", excludingSlotId));
        }
    }

    public void MarkReady(string slotId, int port, int pid)
    {
        lock (_connLock)
        {
            Exec("UPDATE slots SET status='ready', port=$p, pid=$pid WHERE slot_id=$s;",
                 ("$p", port), ("$pid", pid), ("$s", slotId));
        }
    }

    public void Delete(string slotId)
    {
        lock (_connLock)
        {
            Exec("DELETE FROM slots WHERE slot_id=$s;", ("$s", slotId));
        }
    }

    // Drop-file adoption. Atomically:
    //   1. Bail out (return null) if any slot already points at this (pid, port)
    //      — guards against duplicate announcements for a single listener.
    //   2. Pick the first unused name from name_pool, or fall back to 'slot-N'.
    //   3. Insert the adopted+ready row.
    // The whole thing runs under BEGIN IMMEDIATE so a peer router can't race us
    // into picking the same name or adopting the same listener twice.
    public string? AdoptIfNew(string version, int port, int pid, int routerPid)
    {
        lock (_connLock)
        {
            using var tx = _conn.BeginTransaction(deferred: false);

            var dup = ScalarLong(tx,
                "SELECT COUNT(*) FROM slots WHERE pid=$p AND port=$port;",
                ("$p", pid), ("$port", port)) > 0;
            if (dup) { tx.Commit(); return null; }

            string? picked;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"SELECT name FROM name_pool
                                    WHERE name NOT IN (SELECT slot_id FROM slots)
                                    ORDER BY idx ASC LIMIT 1;";
                picked = cmd.ExecuteScalar() as string;
            }
            string slotId = picked ?? FallbackNumberedSlot(tx);

            Exec(tx, @"INSERT INTO slots(slot_id, version, port, pid, status, adopted, started_at, router_pid)
                       VALUES($s, $v, $p, $pid, 'ready', 1, $t, $r);",
                 ("$s", slotId), ("$v", version), ("$p", port), ("$pid", pid),
                 ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                 ("$r", routerPid));

            tx.Commit();
            return slotId;
        }
    }

    public ChildRhino? Get(string slotId)
    {
        lock (_connLock)
        {
            return QueryOne(null, "SELECT * FROM slots WHERE slot_id=$s;", ("$s", slotId));
        }
    }

    public IReadOnlyList<ChildRhino> ListReady()
    {
        lock (_connLock)
        {
            return Query(null, "SELECT * FROM slots WHERE status='ready';");
        }
    }

    public IReadOnlyList<ChildRhino> ListAllOwnedBy(int routerPid)
    {
        lock (_connLock)
        {
            return Query(null, "SELECT * FROM slots WHERE router_pid=$r;", ("$r", routerPid));
        }
    }

    // Mac shared-pid: find any other ready slot pointing at the same Rhino
    // process. If one exists, this isn't the last listener and CloseAsync
    // should tear down just the listener via the control channel, not the pid.
    public ChildRhino? FindSiblingByPid(string slotId, int pid)
    {
        lock (_connLock)
        {
            return QueryOne(null,
                "SELECT * FROM slots WHERE pid=$p AND slot_id != $s AND status='ready' LIMIT 1;",
                ("$p", pid), ("$s", slotId));
        }
    }

    // Crashed-router cleanup. A router that died between Reserve() and
    // MarkReady() leaves a 'launching' row behind; any router (typically the
    // next one to try to spawn) deletes them once they exceed maxAge.
    public int ReapStaleLaunching(TimeSpan maxAge)
    {
        lock (_connLock)
        {
            var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)maxAge.TotalMilliseconds;
            return Exec("DELETE FROM slots WHERE status='launching' AND started_at < $c;", ("$c", cutoff));
        }
    }

    public void Dispose()
    {
        try { _conn.Close(); } catch { }
        _conn.Dispose();
    }

    // ----- helpers -----------------------------------------------------------

    private int Exec(string sql, params (string, object)[] args) => Exec(null, sql, args);

    private int Exec(SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = _conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return cmd.ExecuteNonQuery();
    }

    private string? Scalar(string sql, params (string, object)[] args)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return cmd.ExecuteScalar() as string;
    }

    private long ScalarLong(SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = _conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        var v2 = cmd.ExecuteScalar();
        return v2 is long l ? l : Convert.ToInt64(v2);
    }

    private ChildRhino? QueryOne(SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = _conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    private IReadOnlyList<ChildRhino> Query(SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        var result = new List<ChildRhino>();
        using var cmd = _conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(ReadRow(r));
        return result;
    }

    private static ChildRhino ReadRow(IDataReader r) => new(
        SlotId: r.GetString(r.GetOrdinal("slot_id")),
        Port:   r.IsDBNull(r.GetOrdinal("port")) ? 0 : r.GetInt32(r.GetOrdinal("port")),
        Pid:    r.IsDBNull(r.GetOrdinal("pid")) ? 0 : r.GetInt32(r.GetOrdinal("pid")),
        Version: r.GetString(r.GetOrdinal("version")),
        Adopted: r.GetInt32(r.GetOrdinal("adopted")) != 0,
        Status:  r.GetString(r.GetOrdinal("status")));
}

public static class SlotStatus
{
    public const string Launching = "launching";
    public const string Ready     = "ready";
}

// Tagged union shape via static factories. Concrete callsite branches on Kind.
// Property names deliberately don't match the factory names — a record's
// positional parameter becomes a same-named property, which would clash with
// the static method group otherwise.
public sealed record SlotReservation(
    SlotReservation.ReservationKind Kind,
    ChildRhino? ExistingSlot,
    string? SlotId,
    string? Version)
{
    public enum ReservationKind { Existing, Leader, Follower }

    public static SlotReservation Existing(ChildRhino slot) =>
        new(ReservationKind.Existing, slot, null, null);

    public static SlotReservation Leader(string slotId, string version) =>
        new(ReservationKind.Leader, null, slotId, version);

    public static SlotReservation Follower(string slotId, string version) =>
        new(ReservationKind.Follower, null, slotId, version);
}
