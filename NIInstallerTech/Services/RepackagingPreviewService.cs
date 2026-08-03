using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NIInstallerTech.Services;

public sealed record RepackagingArtifactStat(string Label, int Count, long Bytes)
{
    public string SizeText => RepackagingPreviewService.HumanizeBytes(Bytes);
}

public sealed record RepackagingPreview(
    string ProductName,
    int LegacyPackageCount,
    long LegacyMeasuredBytes,
    int LegacyMeasuredFileCount,
    bool LegacyFullyMeasured,
    int UniqueDependencyCount,
    int SharedPrerequisiteCount,
    long ProjectedEstimatedBytesLow,
    long ProjectedEstimatedBytesHigh,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<RepackagingArtifactStat> Breakdown,
    string EstimationBasis)
{
    public int ProjectedInstallableCount => 1;
    public bool HasMeasuredSize => LegacyMeasuredBytes > 0;

    // Bars compare against the measured legacy footprint; the projected bar uses the estimate midpoint.
    public double ArtifactBarMaximum => LegacyPackageCount;
    public double SizeBarMaximum => LegacyMeasuredBytes;
    public double ProjectedBarValue => (ProjectedEstimatedBytesLow + ProjectedEstimatedBytesHigh) / 2.0;

    public double EstimatedReductionLowPercent => HasMeasuredSize ? (1 - (double)ProjectedEstimatedBytesHigh / LegacyMeasuredBytes) * 100 : 0;
    public double EstimatedReductionHighPercent => HasMeasuredSize ? (1 - (double)ProjectedEstimatedBytesLow / LegacyMeasuredBytes) * 100 : 0;

    public string LegacySizeText => HasMeasuredSize
        ? RepackagingPreviewService.HumanizeBytes(LegacyMeasuredBytes)
        : "not measured on this host";
    public string ProjectedSizeText => HasMeasuredSize
        ? $"~{RepackagingPreviewService.HumanizeBytes(ProjectedEstimatedBytesLow)} – {RepackagingPreviewService.HumanizeBytes(ProjectedEstimatedBytesHigh)}"
        : "estimated at assembly";
    public string ReductionText => HasMeasuredSize
        ? $"Estimated {EstimatedReductionLowPercent:0}–{EstimatedReductionHighPercent:0}% smaller"
        : "Size comparison available once the package source is connected";

    public IReadOnlyList<string> TopHighlights => Highlights.Take(3).ToList();

    public string Summary
    {
        get
        {
            var sizeClause = HasMeasuredSize ? $" ({LegacySizeText})" : string.Empty;
            var reductionClause = HasMeasuredSize ? $", an estimated {EstimatedReductionLowPercent:0}–{EstimatedReductionHighPercent:0}% smaller" : string.Empty;
            return $"{ProductName} is assembled from {LegacyPackageCount} NIPM package(s){sizeClause}. The new workflow repackages it into one verified component{reductionClause}.";
        }
    }
}

public static class RepackagingPreviewService
{
    // Projected reduction range from deduplicating shared payloads and unified compression. Not yet validated.
    private const double MinReduction = 0.10;
    private const double MaxReduction = 0.30;

    public static RepackagingPreview Analyze(LegacyProductGroup product)
    {
        var packages = product.Packages;

        long measuredBytes = 0;
        var measuredFiles = 0;
        foreach (var package in packages)
        {
            try
            {
                var info = new FileInfo(package.PackagePath);
                if (info.Exists)
                {
                    measuredBytes += info.Length;
                    measuredFiles++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A missing or unreadable package file is reported through the measured-count gap below.
            }
        }
        var fullyMeasured = packages.Count > 0 && measuredFiles == packages.Count;

        var dependencyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            foreach (var dependency in package.Dependencies.Select(NormalizeDependency).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                dependencyCounts[dependency] = dependencyCounts.TryGetValue(dependency, out var count) ? count + 1 : 1;
            }
        }
        var uniqueDependencies = dependencyCounts.Count;
        var sharedPrerequisites = dependencyCounts.Count(pair => pair.Value >= 2);

        var low = (long)(measuredBytes * (1 - MaxReduction));
        var high = (long)(measuredBytes * (1 - MinReduction));

        var highlights = new List<string>
        {
            $"One verified component replaces {packages.Count} separately installed package(s).",
            "Every payload object is SHA-256 verified before install; no per-package installer runs."
        };
        if (sharedPrerequisites > 0)
            highlights.Add($"{sharedPrerequisites} shared prerequisite(s) are stored once instead of repeated per package.");
        highlights.Add("A single recoverable manifest replaces per-package installer metadata and custom actions.");
        highlights.Add("Unified compression is expected to reduce transfer size (estimated, not yet validated).");

        var breakdown = new List<RepackagingArtifactStat>
        {
            new("NIPM packages (.nipkg)", packages.Count, measuredBytes)
        };

        var basis = fullyMeasured
            ? $"Measured all {measuredFiles} package file(s). Projected size assumes a {MinReduction:P0}–{MaxReduction:P0} reduction from deduplication and unified compression. This is an estimate, not yet validated; the final size is set when the component is assembled."
            : measuredFiles > 0
                ? $"Measured {measuredFiles} of {packages.Count} package file(s); the remaining files were not reachable on this host, so the size projection covers the measured files only."
                : "The package files were not reachable on this host, so only counts are shown. Connect the package source to measure and project size.";

        return new RepackagingPreview(
            product.ProductName,
            packages.Count,
            measuredBytes,
            measuredFiles,
            fullyMeasured,
            uniqueDependencies,
            sharedPrerequisites,
            low,
            high,
            highlights,
            breakdown,
            basis);
    }

    public static string HumanizeBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string NormalizeDependency(string dependency)
    {
        var parenIndex = dependency.IndexOf('(');
        return (parenIndex > 0 ? dependency[..parenIndex] : dependency).Trim();
    }
}
