using System;

namespace NIInstallerTech.Services;

public sealed record UpdateRelease(
    string Version,
    Uri DownloadUri,
    Uri ChecksumUri,
    string Notes);