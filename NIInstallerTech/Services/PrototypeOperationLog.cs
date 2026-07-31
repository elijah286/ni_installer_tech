using System;
using System.IO;
using System.Text.Json;

namespace NIInstallerTech.Services;

public sealed class PrototypeOperationLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };
    private readonly object _writeLock = new();
    private readonly string _rootDirectory;

    public PrototypeOperationLog(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NISetupPrototype");
        Directory.CreateDirectory(LogDirectory);
        FilePath = Path.Combine(LogDirectory, $"ni-setup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        Write("session", "started", "NI Setup prototype session started.");
    }

    public string RootDirectory => _rootDirectory;

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string FilePath { get; }

    public void Write(string phase, string outcome, string message, object? data = null)
    {
        var entry = new PrototypeLogEntry(DateTimeOffset.UtcNow, phase, outcome, message, data);
        var line = JsonSerializer.Serialize(entry, SerializerOptions) + Environment.NewLine;
        lock (_writeLock)
        {
            File.AppendAllText(FilePath, line);
        }
    }
}

internal sealed record PrototypeLogEntry(DateTimeOffset TimestampUtc, string Phase, string Outcome, string Message, object? Data);
