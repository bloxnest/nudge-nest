# Security

## Only download from here

The only official downloads are on this repository's
[Releases page](https://github.com/bloxnest/nudge-nest/releases), linked from
[bloxnest.github.io/nudge-nest](https://bloxnest.github.io/nudge-nest/). If someone sends you
NudgeNest from anywhere else, don't run it.

## Check a download

Every release is built by [GitHub Actions](.github/workflows/release.yml) from the code in this
repository and published with a SHA-256 checksum and a signed build-provenance attestation.

```powershell
Get-FileHash .\NudgeNest.exe -Algorithm SHA256                 # must match the release page
gh attestation verify NudgeNest.exe --repo bloxnest/nudge-nest  # needs the GitHub CLI
```

## What the app can and can't do

- It uses standard Windows calls to bring the Roblox window forward for about half a second and press
  one key (`SendInput`), then puts your windows back.
- It never opens a network connection, never needs administrator rights, and never reads or writes
  Roblox's files, memory or process.

## Reporting a problem

If you find a security problem, please report it privately through GitHub's
**Report a vulnerability** button on the [Security tab](https://github.com/bloxnest/nudge-nest/security)
instead of opening a public issue. Please include the version (shown on the About page) and what you
found.
