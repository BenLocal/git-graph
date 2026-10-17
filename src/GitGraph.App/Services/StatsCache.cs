using System.Security.Cryptography;
using System.Text;

namespace GitGraph.App.Services;

/// <summary>
/// Line counts per commit, kept on disk per repository so the slow counting pass (git diffs every commit)
/// only runs for commits it hasn't seen. Commits never change, so entries never go stale. Also remembers the
/// heads already counted, so git can skip everything reachable from them.
/// </summary>
public sealed class StatsCache
{
    private const int MaxHeads = 20;
    private readonly string _file;

    public Dictionary<string, (int Additions, int Deletions)> Lines { get; } = [];
    public List<string> Heads { get; } = [];

    private StatsCache(string file) => _file = file;

    /// <param name="commonDir">The repository's shared .git directory, so all its worktrees share one cache.</param>
    public static StatsCache Load(string commonDir)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitGraph", "stats");
        var key = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(commonDir)));
        var cache = new StatsCache(Path.Combine(dir, key + ".tsv"));
        try
        {
            if (!File.Exists(cache._file)) return cache;
            foreach (var line in File.ReadLines(cache._file))
            {
                var parts = line.Split('\t');
                if (parts[0] == "heads") cache.Heads.AddRange(parts.Skip(1));
                else if (parts.Length == 3 && int.TryParse(parts[1], out var a) && int.TryParse(parts[2], out var d))
                    cache.Lines[parts[0]] = (a, d);
            }
        }
        catch (IOException) { /* start afresh */ }
        return cache;
    }

    public void AddHead(string head)
    {
        Heads.Remove(head);
        Heads.Insert(0, head);
        if (Heads.Count > MaxHeads) Heads.RemoveRange(MaxHeads, Heads.Count - MaxHeads);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var sb = new StringBuilder("heads\t").AppendJoin('\t', Heads).Append('\n');
            foreach (var (hash, (a, d)) in Lines) sb.Append(hash).Append('\t').Append(a).Append('\t').Append(d).Append('\n');
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, _file, overwrite: true);
        }
        catch (IOException) { /* a cache; next time counts again */ }
    }
}
