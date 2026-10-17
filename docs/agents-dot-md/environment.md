# Development environment, pushing and releases

> Where the .NET SDK lives, how to push to the BenLocal account, and the release status.

## Development environment

- The .NET 10 SDK is installed in `~/.local/opt/dotnet`, not from a system package.
- `git` must be on `PATH`; the integration tests create real repositories in temporary folders.
- Credentials are never committed. The project currently has no server credentials at all.

## Pushing

- The `origin` remote is https://github.com/BenLocal/git-graph. The default gh account on this machine is benenen, so pushing this repository uses BenLocal's token without switching the active account:

```bash
GH_TOKEN=$(gh auth token -u BenLocal) git -c http.proxy= -c https.proxy= \
  -c credential.helper= -c credential.helper='!f(){ echo username=BenLocal; echo password=$GH_TOKEN; }; f' \
  push
```

- The local proxy is unreliable, so the push bypasses it as shown above.

## Releases

No installer or CI yet; only `dotnet run` / `dotnet publish`.
