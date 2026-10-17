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
public enum GitOperation { Merge, CherryPick }

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
