# Publishing to NuGet.org

The [`Release`](../.github/workflows/release.yml) workflow packs every `TyFi.Idempotency.*`
project and pushes it to NuGet.org whenever you push a version tag.

It uses [**Trusted Publishing**](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing),
which is what NuGet.org recommends: your workflow exchanges a short-lived GitHub OIDC token
for a temporary API key (valid ~1 hour) at publish time, so **no long-lived secret is ever
stored in GitHub**. You configure it once.

## One-time: reserve the package ID prefix (recommended)

Because these packages share the `TyFi.Idempotency` prefix, reserve it so only your account
can publish under it. After the first successful publish, open
<https://www.nuget.org/account/Packages> and request the `TyFi.Idempotency.*` reserved
namespace for your user or organization.

## Set up Trusted Publishing (recommended)

### 1. Register the policy on NuGet.org

1. Sign in to <https://www.nuget.org>.
2. Click your username → **Trusted Publishing**.
3. **Add** a new policy and enter (values are case-insensitive):
   - **Policy owner:** your **organization** (so it owns the packages) or your user. You must
     be an active member of the org you pick.
   - **Repository Owner:** your GitHub user/org, e.g. `TyFiConsulting`.
   - **Repository:** `idempotency-handler`.
   - **Workflow File:** `release.yml` — the **file name only**, not the
     `.github/workflows/` path.
   - **Environment:** leave empty (the workflow does not use a GitHub Actions environment).
   - **Scopes:** allow *Push new packages and versions*, glob `TyFi.Idempotency.*`.
4. Save.

> **Private repos start "pending activation" for 7 days.** The policy works during that
> window; the first successful publish locks it permanently to your repo + owner IDs. If no
> publish happens in 7 days it goes inactive — just re-run a release to restart the window.

### 2. Add your NuGet username as a variable

The `NuGet/login` action needs your nuget.org **username (profile name), not your email**.
This is public information (it appears on your package pages), so store it as a plain
**Actions variable**, not a secret.

1. In GitHub: `idempotency-handler` repo → **Settings → Secrets and variables → Actions →
   Variables tab → New repository variable** (or set it once at the organization level and
   grant it to this repo).
2. Name it exactly `NUGET_USER` and set the value to your nuget.org profile name. Save.

That's the only configuration required — there is no API key or secret to store or rotate.

### 3. Release

```bash
# Bump to the version you want to publish, then:
git tag v0.2.0
git push origin v0.2.0
```

The workflow derives the version from the tag (`v0.2.0` → `0.2.0`), builds, tests, packs,
logs in via OIDC to get a short-lived key, and pushes. `--skip-duplicate` makes re-runs safe.

### How it works

1. The `release.yml` job declares `permissions: id-token: write`.
2. `NuGet/login@v1` requests a GitHub OIDC token and exchanges it at nuget.org for a
   temporary API key.
3. `dotnet nuget push` uses that key immediately (it expires in ~1 hour, one token → one
   key).

## Alternative: API key (only if you can't use Trusted Publishing)

Trusted Publishing is preferred. If your environment can't use OIDC, fall back to a stored
key:

1. On NuGet.org: **Account → API Keys → Create**, scope **Push**, glob `TyFi.Idempotency.*`,
   owner = your org. Copy the value (shown once).
2. In GitHub, add it as the `NUGET_API_KEY` secret.
3. In `release.yml`, replace the *NuGet login* + *Push* steps with a single push:
   ```yaml
   - name: Push to NuGet.org
     env:
       NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
     run: >
       dotnet nuget push "artifacts/*.nupkg"
       --api-key "$NUGET_API_KEY"
       --source https://api.nuget.org/v3/index.json
       --skip-duplicate
   ```
   You can then remove the `id-token: write` permission and the `NUGET_USER` variable.

## First publish (manual, optional)

If you prefer to seed the packages before automating (this uses a temporary personal key):

```bash
dotnet pack -c Release -p:Version=0.1.0 -o artifacts
dotnet nuget push "artifacts/*.nupkg" \
  --api-key "<your key>" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
```

## Versioning

- The version comes from the git tag; there is no version committed in the `.csproj` files
  (the local fallback is `0.1.0-dev`).
- Use [Semantic Versioning](https://semver.org): `vMAJOR.MINOR.PATCH`, and
  `v1.2.0-preview.1` for prereleases.
- Symbol packages (`.snupkg`) and Source Link are enabled, so consumers get debugging
  support automatically.
