using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GitGraph.Core.Navigation;

/// <summary>A definition found in the index, with the file it is in.</summary>
public sealed record IndexedSymbol(string Path, SymbolDef Symbol);

/// <summary>A type that names <see cref="SuperRef.SuperName"/> as a supertype, with the file it is in.</summary>
public sealed record IndexedSuper(string Path, SuperRef Super);

/// <summary>What one file contributes to the index.</summary>
public sealed record FileSymbols(IReadOnlyList<SymbolDef> Definitions, IReadOnlyList<SuperRef> Supers, IReadOnlyCollection<string> Names)
{
    public static readonly FileSymbols Empty = new([], [], []);

    public static FileSymbols From(ParsedSource src) =>
        new(src.Definitions(), src.Supers(), src.Identifiers().Select(i => i.Name).ToHashSet(StringComparer.Ordinal));
}

/// <summary>
/// The symbol index of one repository (all its worktrees) or one plain folder, in SQLite. Parse results are
/// stored per file content (blob hash), so files with the same content in two worktrees, or before and after a
/// branch switch, share them. Each work tree root maps its paths to blobs. One writer at a time; readers may run
/// alongside it (WAL).
/// </summary>
public sealed class SymbolIndex : IDisposable
{
    private const int SchemaVersion = 1;
    private readonly SqliteConnection _db;

    private SymbolIndex(SqliteConnection db) => _db = db;

    /// <summary>The folder of the index files; tests point it somewhere temporary.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitGraph", "index");

    /// <summary>Where the index of a repository (keyed by its common git dir) or plain folder lives.</summary>
    public static string PathFor(string key) =>
        Path.Combine(Folder, Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(key))) + ".db");

    /// <summary>
    /// Opens or creates the index. An index from another schema or older queries is emptied, since its contents
    /// would be wrong.
    /// </summary>
    public static SymbolIndex Open(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        db.Open();
        var index = new SymbolIndex(db);
        index.Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");
        index.Exec("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        var version = $"{SchemaVersion}:{NavLanguage.QueriesHash()}";
        if (index.GetMeta("version") != version)
        {
            index.Exec("""
                DROP TABLE IF EXISTS names; DROP TABLE IF EXISTS supers; DROP TABLE IF EXISTS symbols;
                DROP TABLE IF EXISTS files; DROP TABLE IF EXISTS blobs; DELETE FROM meta;
                CREATE TABLE blobs(id INTEGER PRIMARY KEY, hash TEXT NOT NULL UNIQUE, lang TEXT NOT NULL);
                CREATE TABLE files(root TEXT NOT NULL, path TEXT NOT NULL,
                    blob_id INTEGER NOT NULL REFERENCES blobs(id) ON DELETE CASCADE,
                    mtime INTEGER NOT NULL, size INTEGER NOT NULL, PRIMARY KEY(root, path));
                CREATE INDEX files_blob ON files(blob_id);
                CREATE TABLE symbols(blob_id INTEGER NOT NULL REFERENCES blobs(id) ON DELETE CASCADE,
                    name TEXT NOT NULL, kind INTEGER NOT NULL, start INTEGER NOT NULL, end INTEGER NOT NULL,
                    line INTEGER NOT NULL, col INTEGER NOT NULL, container TEXT);
                CREATE INDEX symbols_name ON symbols(name);
                CREATE INDEX symbols_blob ON symbols(blob_id);
                CREATE TABLE supers(blob_id INTEGER NOT NULL REFERENCES blobs(id) ON DELETE CASCADE,
                    type_name TEXT NOT NULL, super_name TEXT NOT NULL, start INTEGER NOT NULL, end INTEGER NOT NULL,
                    line INTEGER NOT NULL, col INTEGER NOT NULL);
                CREATE INDEX supers_super ON supers(super_name);
                CREATE INDEX supers_blob ON supers(blob_id);
                CREATE TABLE names(name TEXT NOT NULL, blob_id INTEGER NOT NULL REFERENCES blobs(id) ON DELETE CASCADE,
                    PRIMARY KEY(name, blob_id)) WITHOUT ROWID;
                CREATE INDEX names_blob ON names(blob_id);
                """);
            index.SetMeta("version", version);
        }
        return index;
    }

    public void Dispose() => _db.Dispose();

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public string? GetMeta(string key)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var cmd = Command("INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v",
            ("$k", key), ("$v", value));
        cmd.ExecuteNonQuery();
    }

    /// <summary>A file of a work tree as the index last saw it.</summary>
    public sealed record FileRow(string Path, string Blob, long Mtime, long Size);

    public Dictionary<string, FileRow> Files(string root)
    {
        using var cmd = Command("SELECT f.path, b.hash, f.mtime, f.size FROM files f JOIN blobs b ON b.id = f.blob_id WHERE f.root = $r",
            ("$r", root));
        using var r = cmd.ExecuteReader();
        var result = new Dictionary<string, FileRow>(StringComparer.Ordinal);
        while (r.Read()) result[r.GetString(0)] = new FileRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3));
        return result;
    }

    /// <summary>The blob hashes already parsed, of all work trees.</summary>
    public HashSet<string> Blobs()
    {
        using var cmd = Command("SELECT hash FROM blobs");
        using var r = cmd.ExecuteReader();
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>A batch of changes, applied atomically, so a cancelled sync leaves a consistent index.</summary>
    public sealed class Batch : IDisposable
    {
        private readonly SymbolIndex _index;
        private readonly SqliteTransaction _tx;

        internal Batch(SymbolIndex index)
        {
            _index = index;
            _tx = index._db.BeginTransaction();
        }

        public void AddBlob(string hash, NavLanguage language, FileSymbols symbols)
        {
            using var blob = Cmd("INSERT INTO blobs(hash, lang) VALUES($h, $l) ON CONFLICT(hash) DO NOTHING RETURNING id",
                ("$h", hash), ("$l", language.Id));
            if (blob.ExecuteScalar() is not long id) return; // already there
            using (var cmd = Cmd("INSERT INTO symbols VALUES($b, $n, $k, $s, $e, $l, $c, $ct)"))
            {
                foreach (var d in symbols.Definitions)
                {
                    Set(cmd, ("$b", id), ("$n", d.Name), ("$k", (int)d.Kind), ("$s", d.Span.Start), ("$e", d.Span.End),
                        ("$l", d.Span.Line), ("$c", d.Span.Column), ("$ct", d.Container));
                    cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = Cmd("INSERT INTO supers VALUES($b, $t, $sn, $s, $e, $l, $c)"))
            {
                foreach (var s in symbols.Supers)
                {
                    Set(cmd, ("$b", id), ("$t", s.TypeName), ("$sn", s.SuperName), ("$s", s.Span.Start), ("$e", s.Span.End),
                        ("$l", s.Span.Line), ("$c", s.Span.Column));
                    cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = Cmd("INSERT OR IGNORE INTO names VALUES($n, $b)"))
            {
                foreach (var n in symbols.Names)
                {
                    Set(cmd, ("$n", n), ("$b", id));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Points a path at a blob, which must already be in the index.</summary>
        public void SetFile(string root, string path, string blob, long mtime, long size)
        {
            using var cmd = Cmd("""
                INSERT INTO files(root, path, blob_id, mtime, size) SELECT $r, $p, id, $m, $s FROM blobs WHERE hash = $h
                ON CONFLICT(root, path) DO UPDATE SET blob_id = excluded.blob_id, mtime = excluded.mtime, size = excluded.size
                """, ("$r", root), ("$p", path), ("$h", blob), ("$m", mtime), ("$s", size));
            cmd.ExecuteNonQuery();
        }

        public void RemoveFile(string root, string path)
        {
            using var cmd = Cmd("DELETE FROM files WHERE root = $r AND path = $p", ("$r", root), ("$p", path));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Drops parse results no work tree uses any more.</summary>
        public int RemoveUnusedBlobs()
        {
            using var cmd = Cmd("DELETE FROM blobs WHERE id NOT IN (SELECT blob_id FROM files)");
            return cmd.ExecuteNonQuery();
        }

        public void Commit() => _tx.Commit();

        public void Dispose() => _tx.Dispose();

        private SqliteCommand Cmd(string sql, params (string, object?)[] args)
        {
            var cmd = _index.Command(sql, args);
            cmd.Transaction = _tx;
            return cmd;
        }

        private static void Set(SqliteCommand cmd, params (string Name, object? Value)[] args)
        {
            if (cmd.Parameters.Count == 0)
                foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            else
                foreach (var (name, value) in args) cmd.Parameters[name].Value = value ?? DBNull.Value;
        }
    }

    public Batch BeginBatch() => new(this);

    /// <summary>Definitions named <paramref name="name"/> in a work tree.</summary>
    public List<IndexedSymbol> FindDefinitions(string root, string name)
    {
        using var cmd = Command("""
            SELECT f.path, s.name, s.kind, s.start, s.end, s.line, s.col, s.container
            FROM symbols s JOIN files f ON f.blob_id = s.blob_id
            WHERE s.name = $n AND f.root = $r ORDER BY f.path, s.start
            """, ("$n", name), ("$r", root));
        using var r = cmd.ExecuteReader();
        var result = new List<IndexedSymbol>();
        while (r.Read())
            result.Add(new IndexedSymbol(r.GetString(0), new SymbolDef(r.GetString(1), (SymbolKind)r.GetInt32(2),
                new TextSpan(r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)), r.IsDBNull(7) ? null : r.GetString(7))));
        return result;
    }

    /// <summary>Types in a work tree that name <paramref name="superName"/> as a supertype.</summary>
    public List<IndexedSuper> FindSubtypes(string root, string superName)
    {
        using var cmd = Command("""
            SELECT f.path, s.type_name, s.super_name, s.start, s.end, s.line, s.col
            FROM supers s JOIN files f ON f.blob_id = s.blob_id
            WHERE s.super_name = $n AND f.root = $r ORDER BY f.path, s.start
            """, ("$n", superName), ("$r", root));
        using var r = cmd.ExecuteReader();
        var result = new List<IndexedSuper>();
        while (r.Read())
            result.Add(new IndexedSuper(r.GetString(0), new SuperRef(r.GetString(1), r.GetString(2),
                new TextSpan(r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)))));
        return result;
    }

    /// <summary>Files of a work tree with an identifier named <paramref name="name"/>.</summary>
    public List<string> FilesMentioning(string root, string name)
    {
        using var cmd = Command("""
            SELECT f.path FROM names n JOIN files f ON f.blob_id = n.blob_id
            WHERE n.name = $n AND f.root = $r ORDER BY f.path
            """, ("$n", name), ("$r", root));
        using var r = cmd.ExecuteReader();
        var result = new List<string>();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }
}
