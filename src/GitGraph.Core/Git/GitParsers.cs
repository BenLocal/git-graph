namespace GitGraph.Core.Git;

/// <summary>Pure parsers for git's machine-readable output. Kept separate from process code so they can be unit tested.</summary>
public static partial class GitParsers
{
    /// <summary>
    /// Parses <c>git status --porcelain=v1 -z</c>: "XY path", with renames followed by their old path.
    /// The index side (X) and the work tree side (Y) together give one state per file.
    /// </summary>
    public static List<FileStatusEntry> ParseStatus(string output)
    {
        var result = new List<FileStatusEntry>();
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var e = parts[i];
            if (e.Length < 4) continue;
            char x = e[0], y = e[1];
            var path = e[3..];
            var kind = (x, y) switch
            {
                ('?', '?') => FileStatusKind.Untracked,
                ('!', '!') => FileStatusKind.Ignored,
                ('U', _) or (_, 'U') or ('A', 'A') or ('D', 'D') => FileStatusKind.Conflict,
                ('R', _) or ('C', _) => FileStatusKind.Renamed,
                (_, 'D') or ('D', _) => FileStatusKind.Deleted,
                ('A', _) => FileStatusKind.Added,
                _ => FileStatusKind.Modified,
            };
            if (x is 'R' or 'C') i++; // skip the old path of a rename or copy
            result.Add(new FileStatusEntry(path, kind));
        }
        return result;
    }

    /// <summary>Per-commit header for contributor stats; with --shortstat, a summary line follows each header.</summary>
    public const string StatFormat = "%x1e%H%x00%aN%x00%aE%x00%at";

    [System.Text.RegularExpressions.GeneratedRegex(@"(\d+) insertion")]
    private static partial System.Text.RegularExpressions.Regex Insertions();

    [System.Text.RegularExpressions.GeneratedRegex(@"(\d+) deletion")]
    private static partial System.Text.RegularExpressions.Regex Deletions();

    /// <summary>
    /// Parses <c>git log --no-merges --format=StatFormat [--shortstat]</c>. Without --shortstat the line counts
    /// stay null; with it, a commit that changed only binary files counts zero lines, as on GitHub.
    /// </summary>
    public static List<Stats.CommitStat> ParseStats(string output, bool withLines)
    {
        var result = new List<Stats.CommitStat>();
        foreach (var chunk in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var nl = chunk.IndexOf('\n');
            var head = (nl < 0 ? chunk : chunk[..nl]).Split('\0');
            if (head.Length < 4 || !long.TryParse(head[3].Trim(), out var unix)) continue;
            int? adds = null, dels = null;
            if (withLines)
            {
                var rest = nl < 0 ? "" : chunk[(nl + 1)..];
                adds = Insertions().Match(rest) is { Success: true } a ? int.Parse(a.Groups[1].Value) : 0;
                dels = Deletions().Match(rest) is { Success: true } d ? int.Parse(d.Groups[1].Value) : 0;
            }
            result.Add(new Stats.CommitStat(head[0], head[1], head[2], DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime(), adds, dels));
        }
        return result;
    }

    public const char RecordSep = '\x1e';
    public const char FieldSep = '\0';

    /// <summary>Format string paired with <see cref="ParseLog"/>.</summary>
    public const string LogFormat = "%H%x00%P%x00%an%x00%ae%x00%at%x00%s%x1e";

    /// <summary>Format string paired with <see cref="ParseStashes"/>: name, hash, parents, author, date, reflog subject.</summary>
    public const string StashFormat = "%gd%x00%H%x00%P%x00%an%x00%ae%x00%at%x00%gs%x1e";

    /// <summary>
    /// Entries of <c>git stash list</c>, newest first. A stash is a commit whose first parent is the commit it was
    /// made on; its subject reads "On main: message" or "WIP on main: abc1234 subject".
    /// </summary>
    public static List<StashEntry> ParseStashes(string output)
    {
        var list = new List<StashEntry>();
        foreach (var raw in output.Split(RecordSep))
        {
            var rec = raw.TrimStart('\n', '\r');
            if (rec.Length == 0) continue;
            var f = rec.Split(FieldSep);
            if (f.Length < 7) continue;
            var parents = f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parents.Length == 0) continue;
            list.Add(new StashEntry(f[0], f[1], parents[0], parents.Length > 2, f[3], f[4], FromUnix(f[5]), f[6]));
        }
        return list;
    }

    /// <summary>Format string paired with <see cref="ParseFileHistory"/>, used with <c>--name-status -z</c>.</summary>
    public const string FileHistoryFormat = "%x1e%H%x00%P%x00%an%x00%ae%x00%at%x00%s%x00";

    /// <summary>
    /// <c>git log --follow --name-status -z</c> for one file: each record is the commit's fields, then the file's
    /// status and path (status, old path and new path for a rename).
    /// </summary>
    public static List<FileHistoryEntry> ParseFileHistory(string output)
    {
        var list = new List<FileHistoryEntry>();
        foreach (var rec in output.Split(RecordSep))
        {
            var f = rec.Split(FieldSep);
            if (f.Length < 6) continue;
            var parents = f[1].Length == 0 ? [] : f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var commit = new CommitInfo(f[0], parents, f[2], f[3], FromUnix(f[4]), f[5]);
            var rest = f.Skip(6).Select(x => x.Trim('\n')).Where(x => x.Length > 0).ToList();
            if (rest.Count < 2) continue;
            var kind = rest[0][0] switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Deleted,
                'R' => ChangeKind.Renamed,
                'C' => ChangeKind.Copied,
                'T' => ChangeKind.TypeChanged,
                _ => ChangeKind.Modified,
            };
            var change = kind is ChangeKind.Renamed or ChangeKind.Copied && rest.Count >= 3
                ? new FileChange(kind, rest[2], rest[1], null, null)
                : new FileChange(kind, rest[1], null, null, null);
            list.Add(new FileHistoryEntry(commit, change));
        }
        return list;
    }

    /// <summary>
    /// <c>git blame --porcelain</c>: a header "hash original final [count]" per line, the commit's details the first
    /// time it appears, then the line itself after a tab.
    /// </summary>
    public static BlameResult ParseBlame(string output)
    {
        var lines = new List<BlameLine>();
        var commits = new Dictionary<string, (string Name, string Email, long Time, string Summary)>(StringComparer.Ordinal);
        string? hash = null;
        var number = 0;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith('\t'))
            {
                if (hash != null) lines.Add(new BlameLine(number, hash, line[1..].TrimEnd('\r')));
                continue;
            }
            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];
            if (key.Length == 40 && key.All(char.IsAsciiHexDigit))
            {
                hash = key;
                var parts = value.Split(' ');
                number = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : number + 1;
                if (!commits.ContainsKey(hash)) commits[hash] = ("", "", 0, "");
                continue;
            }
            if (hash is null) continue;
            var c = commits[hash];
            commits[hash] = key switch
            {
                "author" => c with { Name = value },
                "author-mail" => c with { Email = value.Trim('<', '>') },
                "author-time" => c with { Time = long.TryParse(value, out var t) ? t : 0 },
                "summary" => c with { Summary = value },
                _ => c,
            };
        }
        return new BlameResult(lines, commits.ToDictionary(kv => kv.Key,
            kv => new BlameCommit(kv.Key, kv.Value.Name, kv.Value.Email, DateTimeOffset.FromUnixTimeSeconds(kv.Value.Time).ToLocalTime(), kv.Value.Summary)));
    }

    public static List<CommitInfo> ParseLog(string output)
    {
        var list = new List<CommitInfo>();
        foreach (var raw in output.Split(RecordSep))
        {
            var rec = raw.TrimStart('\n', '\r');
            if (rec.Length == 0) continue;
            var f = rec.Split(FieldSep);
            if (f.Length < 6) continue;
            var parents = f[1].Length == 0 ? [] : f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Add(new CommitInfo(f[0], parents, f[2], f[3], FromUnix(f[4]), f[5]));
        }
        return list;
    }

    /// <summary>
    /// The comments of one commit's note. The note is a log, one JSON object per line: an edit adds the comment again
    /// with the same id and a later date, a delete adds it with "deleted": true. That way two people's notes merge
    /// with git's cat_sort_uniq strategy (lines from both, sorted, duplicates dropped) and nothing is lost.
    /// Each comment's latest version counts; they come in the order they were first written.
    /// </summary>
    public static List<CodeComment> ParseComments(string note)
    {
        var latest = new Dictionary<string, (CodeComment Comment, bool Deleted, DateTimeOffset First)>();
        foreach (var raw in note.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('{')) continue;
            System.Text.Json.Nodes.JsonObject? o;
            try { o = System.Text.Json.Nodes.JsonNode.Parse(line) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { continue; }
            if (o?["id"]?.GetValue<string>() is not { Length: > 0 } id || o["date"] is null) continue;
            var comment = new CodeComment(id, o["file"]?.GetValue<string>() ?? "", o["side"]?.GetValue<string>() == "old",
                o["line"]?.GetValue<int>() ?? 0, o["author"]?.GetValue<string>() ?? "", o["email"]?.GetValue<string>() ?? "",
                o["date"]!.GetValue<DateTimeOffset>(), o["text"]?.GetValue<string>() ?? "");
            var deleted = o["deleted"]?.GetValue<bool>() == true;
            if (!latest.TryGetValue(id, out var known))
                latest[id] = (comment, deleted, comment.Date);
            else
            {
                var first = comment.Date < known.First ? comment.Date : known.First;
                latest[id] = comment.Date >= known.Comment.Date ? (comment, deleted, first) : (known.Comment, known.Deleted, first);
            }
        }
        return latest.Values.Where(v => !v.Deleted).OrderBy(v => v.First).Select(v => v.Comment).ToList();
    }

    /// <summary>One line of a comment note; see <see cref="ParseComments"/>.</summary>
    public static string CommentLine(CodeComment c, bool deleted = false)
    {
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = c.Id,
            ["file"] = c.File,
            ["side"] = c.OldSide ? "old" : "new",
            ["line"] = c.Line,
            ["author"] = c.Author,
            ["email"] = c.Email,
            ["date"] = c.Date,
            ["text"] = deleted ? "" : c.Text,
        };
        if (deleted) o["deleted"] = true;
        return o.ToJsonString();
    }

    public const string RebaseFormat = "%H%x00%P%x00%an%x00%ae%x00%at%x00%s%x00%B%x1e";

    public static List<RebaseCommit> ParseRebaseCommits(string output)
    {
        var list = new List<RebaseCommit>();
        foreach (var raw in output.Split(RecordSep))
        {
            var f = raw.TrimStart('\n', '\r').Split(FieldSep, 7);
            if (f.Length < 7) continue;
            var parents = f[1].Length == 0 ? [] : f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Add(new RebaseCommit(new CommitInfo(f[0], parents, f[2], f[3], FromUnix(f[4]), f[5]), f[6].TrimEnd('\n')));
        }
        return list;
    }

    public const string ReflogFormat = "%H%x00%gd%x00%gs%x1e";

    /// <summary>Reads <see cref="ReflogFormat"/> taken with --date=unix, so each selector ends in its time: HEAD@{1700000000}.</summary>
    public static List<ReflogEntry> ParseReflog(string output)
    {
        var list = new List<ReflogEntry>();
        foreach (var raw in output.Split(RecordSep))
        {
            var f = raw.TrimStart('\n', '\r').Split(FieldSep);
            if (f.Length < 3) continue;
            var at = f[1].LastIndexOf('{');
            var time = at >= 0 && long.TryParse(f[1][(at + 1)..].TrimEnd('}'), out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : default;
            var colon = f[2].IndexOf(": ", StringComparison.Ordinal);
            var (action, message) = colon < 0 ? (f[2], "") : (f[2][..colon], f[2][(colon + 2)..]);
            list.Add(new ReflogEntry(f[0], time, action, message));
        }
        return list;
    }

    public const string DetailFormat = "%H%x00%P%x00%an%x00%ae%x00%at%x00%cn%x00%ce%x00%ct%x00%B";

    public static CommitDetail? ParseDetail(string output)
    {
        var f = output.Split(FieldSep, 9);
        if (f.Length < 9) return null;
        var parents = f[1].Length == 0 ? [] : f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new CommitDetail(f[0], parents, f[2], f[3], FromUnix(f[4]), f[5], f[6], FromUnix(f[7]), f[8].TrimEnd());
    }

    public const string RefFormat = "%(refname)%00%(objectname)%00%(*objectname)%00%(upstream:short)%00%(HEAD)";

    public static List<GitRef> ParseRefs(string output)
    {
        var list = new List<GitRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(FieldSep);
            if (f.Length < 5) continue;
            var full = f[0];
            // Annotated tags point at a tag object; the peeled object is the commit we draw.
            var target = f[2].Length > 0 ? f[2] : f[1];
            if (full.StartsWith("refs/heads/", StringComparison.Ordinal))
                list.Add(new GitRef(full, full["refs/heads/".Length..], RefKind.LocalBranch, target, NullIfEmpty(f[3]), f[4] == "*"));
            else if (full.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                if (full.EndsWith("/HEAD", StringComparison.Ordinal)) continue;
                list.Add(new GitRef(full, full["refs/remotes/".Length..], RefKind.RemoteBranch, target, null, false));
            }
            else if (full.StartsWith("refs/tags/", StringComparison.Ordinal))
                list.Add(new GitRef(full, full["refs/tags/".Length..], RefKind.Tag, target, null, false));
        }
        return list;
    }

    public static List<WorktreeInfo> ParseWorktrees(string porcelain)
    {
        var list = new List<WorktreeInfo>();
        string? path = null, head = null, branch = null;
        bool bare = false, detached = false, locked = false, prunable = false;

        void Flush()
        {
            if (path != null)
                list.Add(new WorktreeInfo(path, head, branch, list.Count == 0, bare, detached, locked, prunable));
            path = head = branch = null;
            bare = detached = locked = prunable = false;
        }

        foreach (var rawLine in porcelain.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }
            var sp = line.IndexOf(' ');
            var key = sp < 0 ? line : line[..sp];
            var value = sp < 0 ? "" : line[(sp + 1)..];
            switch (key)
            {
                case "worktree": Flush(); path = value; break;
                case "HEAD": head = value; break;
                case "branch": branch = value; break;
                case "bare": bare = true; break;
                case "detached": detached = true; break;
                case "locked": locked = true; break;
                case "prunable": prunable = true; break;
            }
        }
        Flush();
        return list;
    }

    /// <summary>
    /// Merges <c>--name-status -z</c> and <c>--numstat -z</c> output of the same diff into one list.
    /// </summary>
    public static List<FileChange> ParseChanges(string nameStatusZ, string numstatZ)
    {
        var stats = new Dictionary<string, (int?, int?)>(StringComparer.Ordinal);
        var n = numstatZ.Split(FieldSep);
        for (var i = 0; i < n.Length; i++)
        {
            var entry = n[i].TrimStart('\n');
            if (entry.Length == 0) continue;
            var parts = entry.Split('\t');
            if (parts.Length < 3) continue;
            int? add = int.TryParse(parts[0], out var a) ? a : null;
            int? del = int.TryParse(parts[1], out var d) ? d : null;
            string path;
            if (parts[2].Length == 0 && i + 2 < n.Length)
            {
                // Rename/copy: "add\tdel\t" NUL old NUL new
                path = n[i + 2];
                i += 2;
            }
            else path = parts[2];
            stats[path] = (add, del);
        }

        var list = new List<FileChange>();
        var s = nameStatusZ.Split(FieldSep);
        for (var i = 0; i < s.Length; i++)
        {
            var status = s[i].TrimStart('\n');
            if (status.Length == 0) continue;
            var kind = status[0] switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Deleted,
                'R' => ChangeKind.Renamed,
                'C' => ChangeKind.Copied,
                'T' => ChangeKind.TypeChanged,
                'U' => ChangeKind.Conflicted,
                _ => ChangeKind.Modified,
            };
            string? oldPath = null;
            string path;
            if (kind is ChangeKind.Renamed or ChangeKind.Copied)
            {
                if (i + 2 >= s.Length) break;
                oldPath = s[i + 1];
                path = s[i + 2];
                i += 2;
            }
            else
            {
                if (i + 1 >= s.Length) break;
                path = s[i + 1];
                i += 1;
            }
            // Unmerged paths show up once per stage; keep the first.
            if (kind == ChangeKind.Conflicted && list.Exists(c => c.Path == path)) continue;
            var (add, del) = stats.TryGetValue(path, out var st) ? st : (null, null);
            list.Add(new FileChange(kind, path, oldPath, add, del));
        }
        return list;
    }

    public static IEnumerable<string> SplitZ(string output) =>
        output.Split(FieldSep, StringSplitOptions.RemoveEmptyEntries);

    private static DateTimeOffset FromUnix(string s) =>
        long.TryParse(s, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : default;

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
