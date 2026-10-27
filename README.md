# PLAYLI PC Check

An open-source, one-shot Windows 10/11 x64 hardware reader for the
[PLAYLI game compatibility page](https://playli.co/pages/can-you-run-it).
This repository contains the scanner only. The storefront, comparison engine,
transfer service and user data are separate and are not included.

**Signing status: application pending. Builds in this repository are currently
unsigned.** See the [Code signing policy](SIGNING.md). No SignPath endorsement
or warning-free download is claimed.

## What happens when you run it

1. Request the scanner on the PLAYLI website, which explains the hardware
   transfer. Downloading a file does not execute it.
2. Open the downloaded file in Windows. A valid website binding is required.
3. The scanner reads the allowlisted hardware, sends it to PLAYLI over HTTPS,
   waits for a bounded acknowledgement, and exits. The website remembers the
   normalized report in that browser without login.

There is no native UI, installer, admin prompt, service, startup registration,
game execution or permanent background process. There are at most three
network attempts and a sixty-second overall lifetime. Redirects are disabled.
See [privacy and collected fields](PRIVACY.md). You may choose the website's
local DxDiag import instead of running the executable.

The filename is `PLAYLI-PC-Check-<64 lowercase hex characters>.exe`. Its
short-lived upload capability binds the scan to the originating browser;
the executable bytes are identical between sessions. A generic/unbound EXE
exits with status 2 **before collection**. Duplicate-download suffixes such as
` (1)` are accepted. Never publish a real capability filename or report.

The ZIP and GitHub Actions artifacts are review packages with a generic EXE;
they are not bound to a visitor. Do not bypass browser, antivirus, SmartScreen
or Internet-origin protections to run a build.

## Build and verify

Requirements: the .NET SDK pinned in `global.json` (10.0.402), Python 3, and
Bash (Git Bash on Windows). No signing or store credentials are needed.

```bash
bash build.sh
```

To use explicit runtime paths:

```bash
PLAYLI_DOTNET=/path/to/dotnet PLAYLI_PYTHON=/path/to/python bash build.sh
```

The build restores locked Windows dependencies from NuGet.org, runs the core
protocol/parser tests, publishes a self-contained x64 WinExe, copies the
actual redistribution notices, and verifies the PE resources, manifest,
versions, hashes and ZIP. Results are under the ignored `artifacts/` directory:

- `win-x64/PLAYLI-PC-Check.exe` and its SHA-256 sidecar;
- `PLAYLI-PC-Check-win-x64.zip` and its SHA-256 sidecar;
- `build-receipt.json`, `pc-check-release.json` and `distribution-verification.json`.

The receipt fingerprints the source/build configuration and records the
unsigned binary hash. It does not claim physical Windows execution, a live
HTTPS transfer, a code signature or publication. Repeat distribution checks:

```bash
python scripts/verify-release.py
```

GitHub Actions builds the same source on a hosted Windows runner with pinned
action commits and read-only repository permissions. Pull requests do not
have signing credentials or production publishing steps. The review artifact
is explicitly named `playli-pc-scanner-unsigned`.

## Implementation

RAM uses GetPhysicallyInstalledSystemMemory. WMI reads selected CPU fields;
CPUID/runtime checks expose instruction flags. DXGI keeps dedicated and
shared GPU memory separate. Per-adapter Direct3D feature queries measure shader,
UMA and DXR support. Storage descriptors are query-only and unsupported or
denied measurements remain unknown. Software renderers are excluded. The
collector does not enable devices or change drivers.

`Windows/Program.cs` invokes the automatic intake path only. Legacy local
export and loopback classes remain in Core for regression tests and are not
started by the production executable. The fixed destination is
`https://partners.playli.co/pc-check/report`.

## Code signing policy

We are applying for free open-source signing through SignPath Foundation.
Acceptance and a signing certificate are pending. Read the
[policy, roles and verification requirements](SIGNING.md) before trusting any
artifact. Unsigned CI artifacts remain unsigned; no production release is
created automatically by the build workflow.

## License and security

Original source and project assets are available under the [MIT license](LICENSE).
Dependencies retain their [third-party licenses and notices](THIRD-PARTY-NOTICES.md).
The license does not grant permission to imply PLAYLI or SignPath endorsement
or a right to use their trademarks. Read [security reporting](SECURITY.md)
before posting diagnostic information.
