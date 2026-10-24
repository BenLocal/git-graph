namespace GitGraph.Core.Git;

public sealed record CommitInfo(
    string Hash,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public bool IsMerge => Parents.Count > 1;
}

public sealed record CommitDetail(
    string Hash,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string CommitterName,
    string CommitterEmail,
    DateTimeOffset CommitDate,
    string Message);

public enum RefKind { LocalBranch, RemoteBranch, Tag }

public sealed record GitRef(string FullName, string Name, RefKind Kind, string Target, string? Upstream, bool IsHead)
{
    /// <summary>For remote branches: "origin" of "origin/main".</summary>
    public string? Remote => Kind == RefKind.RemoteBranch && Name.IndexOf('/') is var i and > 0 ? Name[..i] : null;
}

public sealed record HeadInfo(string? Hash, string? Branch)
{
    public bool IsDetached => Hash != null && Branch == null;
}

public sealed record WorktreeInfo(string Path, string? Head, string? Branch, bool IsMain, bool IsBare, bool IsDetached, bool IsLocked, bool IsPrunable)
{
    public string BranchShortName => Branch is null ? "(detached)" :
        Branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? Branch["refs/heads/".Length..] : Branch;
}

public enum ChangeKind { Added, Modified, Deleted, Renamed, Copied, TypeChanged, Untracked, Conflicted }

public sealed record FileChange(ChangeKind Kind, string Path, string? OldPath, int? Additions, int? Deletions)
{
    public bool IsBinary => Additions is null && Deletions is null && Kind != ChangeKind.Untracked;

    public string StatusLetter => Kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Modified => "M",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.TypeChanged => "T",
        ChangeKind.Untracked => "U",
        ChangeKind.Conflicted => "!",
        _ => "?",
    };
}

/// <summary>
/// What a change list or diff compares. <see cref="To"/> null means the working tree.
/// </summary>
/// <summary>Commands that can stop part-way (on conflicts) and then be continued or aborted.</summary>
public enum GitOperation { Merge, CherryPick, Revert, Rebase }

/// <summary>What <c>git reset</c> does besides moving the branch: keep changes staged, unstaged, or throw them away.</summary>
public enum ResetMode { Soft, Mixed, Hard }

/// <summary>One move of HEAD or a branch, newest first: "commit (amend)" with its message, "checkout" with "moving from a to b".</summary>
public sealed record ReflogEntry(string Hash, DateTimeOffset Time, string Action, string Message);

public enum UndoKind
{
    /// <summary>Switch back to the branch or commit checked out before.</summary>
    Checkout,
    /// <summary>Move the branch back and keep the index and working tree: the undone commit's changes stay staged.</summary>
    SoftReset,
    /// <summary>Move the branch back and its files with it, keeping uncommitted changes (git stops if they are in the way).</summary>
    KeepReset,
}

/// <summary>How to take back the last thing that moved HEAD: <see cref="Action"/> and <see cref="Message"/> say what that was.</summary>
public sealed record UndoStep(UndoKind Kind, string Target, string Action, string Message, string? Branch);

/// <summary>A file's state in the working tree, for colouring the file tree as VS Code does. Earlier members win for folders.</summary>
public enum FileStatusKind { Conflict, Deleted, Modified, Renamed, Added, Untracked, Ignored }

/// <summary>One entry of <c>git status</c>; <see cref="Path"/> ends with "/" for a whole untracked or ignored folder.</summary>
public sealed record FileStatusEntry(string Path, FileStatusKind Kind);

/// <summary>
/// How the checked-out branch relates to its upstream: commits to push (<see cref="Ahead"/>) and to pull
/// (<see cref="Behind"/>). <see cref="Upstream"/> is null when the branch isn't tracking anything yet.
/// </summary>
public sealed record SyncStatus(string? Branch, string? Upstream, int Ahead, int Behind, IReadOnlyList<string> Remotes)
{
    public bool HasRemote => Remotes.Count > 0;
    public bool CanPublish => Branch != null && Upstream is null && HasRemote;
}

public sealed record DiffTarget(string From, string? To)
{
    public bool IsWorkingTree => To is null;
}

/// <summary>What the uncommitted changes row needs: how many paths changed, and which are untracked.</summary>
public sealed record UncommittedSummary(int Count, IReadOnlyList<string> Untracked);

/// <summary>Uncommitted changes split the way the index sees them; see <see cref="GitRepository.GetWorkingChangesAsync"/>.</summary>
public sealed record WorkingChanges(IReadOnlyList<FileChange> Staged, IReadOnlyList<FileChange> Unstaged, IReadOnlyList<FileChange> Conflicted)
{
    public int Count => Staged.Count + Unstaged.Count + Conflicted.Count;
}

/// <summary>One entry of the stash. <see cref="HasUntracked"/>: it also saved untracked files (in its third parent).</summary>
public sealed record StashEntry(string Name, string Hash, string BaseHash, bool HasUntracked, string AuthorName, string AuthorEmail,
    DateTimeOffset Date, string Message);

/// <summary>A commit that changed a file, with the file's path (and earlier path, for a rename) in that commit.</summary>
public sealed record FileHistoryEntry(CommitInfo Commit, FileChange Change);

/// <summary>Who last changed a line: one of <see cref="BlameResult.Commits"/>.</summary>
public sealed record BlameLine(int Number, string Hash, string Text);

public sealed record BlameCommit(string Hash, string AuthorName, string AuthorEmail, DateTimeOffset Date, string Summary)
{
    /// <summary>git blame's hash for lines changed in the working tree but not committed.</summary>
    public const string NotCommitted = "0000000000000000000000000000000000000000";

    public bool IsCommitted => Hash != NotCommitted;
}

public sealed record BlameResult(IReadOnlyList<BlameLine> Lines, IReadOnlyDictionary<string, BlameCommit> Commits);

/// <summary>
/// Narrows the commit list. <see cref="Path"/> is relative to the working tree's top level. <see cref="Code"/> keeps
/// commits that change how often that text occurs: the ones that added or removed it (git's pickaxe, <c>-S</c>).
/// </summary>
public sealed record CommitFilter(string? Author, DateTimeOffset? Since, string? Path, string? Code = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Author) && Since is null && string.IsNullOrWhiteSpace(Path) && string.IsNullOrWhiteSpace(Code);
    /// <summary>Filters that drop commits without rewriting the parents of the ones left.</summary>
    public bool DropsParents => !string.IsNullOrWhiteSpace(Author) || Since != null || !string.IsNullOrWhiteSpace(Code);
}
