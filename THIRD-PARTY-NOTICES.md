# Third-party notices

The helper's collector and loopback implementation are original code for PLAYLI and are licensed under the repository's MIT license. No code from Hardware.Info, systeminformation or LibreHardwareMonitor is included.

Redistributed dependencies:

- [.NET runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), MIT and accompanying third-party notices. Version is pinned in `Windows/Windows.csproj` and the produced runtimeconfig.
- [System.Management 10.0.0](https://www.nuget.org/packages/System.Management/10.0.0), MIT, from [dotnet/runtime](https://github.com/dotnet/runtime). Package dependency hashes are recorded in `Windows/packages.lock.json`.

`build.sh` places the unmodified license and third-party notice files from the actual installed runtime/package payloads next to the EXE and includes them in the distribution ZIP. Distribute those files with the executable. SDK notices are included as additional coverage when present; their inclusion does not claim every SDK component is shipped.

This repository contains no website hardware reference or third-party benchmark database. Storefront, server and comparison-engine source are not included.
