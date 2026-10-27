# Code signing policy

## Current status

This project is applying to the SignPath Foundation open-source code-signing
program. Approval, a signing account, and a signing certificate have **not**
been granted. Current build artifacts are **unsigned**. Do not interpret this
policy or a passing build as proof of a signature or of browser reputation.

If accepted, the intended arrangement is: free code signing provided by
[SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org). This describes the requested
arrangement, not a current endorsement. The certificate publisher will be
SignPath Foundation. Its [terms](https://signpath.org/terms) apply to acceptance
and operation of that program.

## People and approvals

- Committers and reviewers: [repository maintainers](https://github.com/liran15153-cmd/playli-pc-scanner/graphs/contributors).
- Release approver: [liran15153-cmd](https://github.com/liran15153-cmd).
- Signing requests require explicit maintainer approval for a reviewed source
  commit and the corresponding trusted CI artifact. No unreviewed pull-request
  artifact will be submitted for production signing.

## Artifact checks

The Windows CI build uses pinned action commits, a pinned .NET SDK and locked
package versions. It runs the protocol/parser tests and records source and
unsigned binary SHA-256 values. The source, licenses and provenance remain
reviewable. Only the Windows scanner artifact is in scope; the PLAYLI store,
comparison engine and server are separate systems.

Once signing is provisioned, a signed release must verify the actual embedded
Authenticode signature, expected publisher, certificate chain and timestamp.
Its checksum and distribution ZIP must be generated from the signed bytes.
Unsigned artifacts remain labelled as such and are not silently promoted.

Public distribution will use a new version and retain existing versioned
downloads. Signing does not guarantee immediate removal of SmartScreen or
Chrome reputation warnings. Security protections and Internet-origin markings
must not be disabled or removed to manufacture a passing test.

## Privacy

[Scanner privacy](PRIVACY.md) describes the explicit website-initiated transfer
and local browser persistence. This program does not transfer hardware to
SignPath. Real user reports and upload capabilities are not build inputs and
must never be included in CI artifacts.
