# Scanner privacy

PLAYLI PC Check runs only when the visitor opens a scanner downloaded from
[the PLAYLI compatibility page](https://playli.co/pages/can-you-run-it).
That page explains the hardware transfer before download. Downloading alone
does not execute the file. You can use the website's local DxDiag upload
instead, or decide not to run the scanner.

The report contains the CPU model, physical and logical processor counts and
instruction flags; installed RAM; Windows version and architecture; hardware
GPU models, separate dedicated/shared memory and measured DirectX, shader and
ray-tracing capabilities; and fixed-volume free capacity and SSD classification.
It includes a capture timestamp. Unknown fields remain unknown.

The scanner does not read personal files, account credentials, the clipboard,
installed applications, hardware serials, a machine/user name, or personal
paths into its report. Local drive roots and device identifiers are used only
to map storage descriptors and are not exported. It does not execute a game
or benchmark frame rates.

An executable without a valid website-issued filename binding exits before
collection. A bound executable sends only the report to the fixed HTTPS
endpoint `https://partners.playli.co/pc-check/report`, using the short-lived
capability in its filename. HTTPS infrastructure necessarily receives normal
connection metadata such as the source IP; the scanner does not add an IP
field to its hardware report. Redirects are disabled. The process waits for a
bounded server acknowledgement and exits, within an overall sixty-second
lifetime. It installs no service or startup task and requests no elevation.

The website remembers the normalized hardware in the originating browser's
localStorage, without a user account. Its forget control or clearing site data
removes that browser copy. The transfer service and its retention are governed
by [PLAYLI's privacy policy](https://playli.co/pages/privacy); this repository
contains the client, not that service or customer data. A received browser
acknowledgement clears the report through the service's existing intake path.

Do not post real reports, executable capability filenames, credentials or
personal device diagnostics in public issues. Use the
[PLAYLI contact page](https://playli.co/pages/contact) for private enquiries.
