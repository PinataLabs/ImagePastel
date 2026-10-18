// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// One JPEG XL conformance vector: a directory under <c>tests/Images/Input/Jxl/Conformance</c>
/// with a <c>test.json</c> that holds the per-frame thresholds and the sha256 of each reference file.
/// A <c>&lt;id&gt;_5</c> vector holds the Level 5 thresholds and reuses the input of <c>&lt;id&gt;</c>.
/// </summary>
internal sealed class JxlConformanceVector
{
    public const string Level5Suffix = "_5";

    private static readonly Lazy<JxlConformanceSource> SourceLazy = new(() => JxlConformanceSource.Load(Path.Combine(RootDirectory, "source.json")));

    private JxlConformanceVector(string id, IReadOnlyList<JxlConformanceFrame> frames, IReadOnlyDictionary<string, string> sha256Sums)
    {
        this.Id = id;
        this.Frames = frames;
        this.Sha256Sums = sha256Sums;
    }

    public static string RootDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Conformance");

    public static JxlConformanceSource Source => SourceLazy.Value;

    /// <summary>Gets the ids of every vector, including the <c>_5</c> Level 5 variants.</summary>
    public static IReadOnlyList<string> AllIds => Directory.GetDirectories(RootDirectory)
        .Select(Path.GetFileName)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<string> Level5Ids => ReadList("main_level5.txt");

    public static IReadOnlyList<string> Level10Ids => ReadList("main_level10.txt");

    public string Id { get; }

    /// <summary>Gets the id of the vector whose <c>input.jxl</c> this vector decodes.</summary>
    public string InputId => this.Id.EndsWith(Level5Suffix, StringComparison.Ordinal) ? this.Id[..^Level5Suffix.Length] : this.Id;

    public string DirectoryPath => Path.Combine(RootDirectory, this.Id);

    /// <summary>Gets a value indicating whether the input is downloaded rather than committed, because of its licence.</summary>
    public bool IsInputFetched => Source.FetchedInputs.ContainsKey(this.InputId);

    /// <summary>Gets the committed input path. Fetched inputs are resolved by <see cref="JxlConformanceData"/>.</summary>
    public string CommittedInputPath => Path.Combine(RootDirectory, this.InputId, "input.jxl");

    public IReadOnlyList<JxlConformanceFrame> Frames { get; }

    public IReadOnlyDictionary<string, string> Sha256Sums { get; }

    public static JxlConformanceVector Load(string id)
    {
        string path = Path.Combine(RootDirectory, id, "test.json");
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = json.RootElement;

        List<JxlConformanceFrame> frames = new();
        foreach (JsonElement frame in root.GetProperty("frames").EnumerateArray())
        {
            frames.Add(new JxlConformanceFrame(
                frame.GetProperty("name").GetString(),
                frame.GetProperty("rms_error").GetDouble(),
                frame.GetProperty("peak_error").GetDouble(),
                frame.TryGetProperty("duration", out JsonElement duration) ? duration.GetDouble() : null));
        }

        Dictionary<string, string> sums = new(StringComparer.Ordinal);
        foreach (JsonProperty sum in root.GetProperty("sha256sums").EnumerateObject())
        {
            sums.Add(sum.Name, sum.Value.GetString());
        }

        return new JxlConformanceVector(id, frames, sums);
    }

    public override string ToString() => this.Id;

    private static string[] ReadList(string fileName) => File.ReadAllLines(Path.Combine(RootDirectory, fileName))
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToArray();
}

/// <summary>
/// The error thresholds for one displayed frame, in nominal [0, 1] sample units, and its duration in
/// seconds, which only animations list.
/// </summary>
internal sealed record JxlConformanceFrame(string Name, double RmsError, double PeakError, double? Duration = null);

/// <summary>The pinned upstream source of the conformance vectors, from <c>source.json</c>.</summary>
internal sealed class JxlConformanceSource
{
    private JxlConformanceSource(string commit, string referenceUrl, string inputUrl, IReadOnlyDictionary<string, string> fetchedInputs)
    {
        this.Commit = commit;
        this.ReferenceUrl = referenceUrl;
        this.InputUrl = inputUrl;
        this.FetchedInputs = fetchedInputs;
    }

    public string Commit { get; }

    /// <summary>Gets the URL template of a reference file, with a <c>{sha256}</c> placeholder.</summary>
    public string ReferenceUrl { get; }

    /// <summary>Gets the URL template of an input, with <c>{commit}</c> and <c>{id}</c> placeholders.</summary>
    public string InputUrl { get; }

    /// <summary>Gets the sha256 of each input that is downloaded rather than committed.</summary>
    public IReadOnlyDictionary<string, string> FetchedInputs { get; }

    public static JxlConformanceSource Load(string path)
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = json.RootElement;
        Dictionary<string, string> fetched = new(StringComparer.Ordinal);
        foreach (JsonProperty input in root.GetProperty("fetchedInputs").EnumerateObject())
        {
            fetched.Add(input.Name, input.Value.GetString());
        }

        return new JxlConformanceSource(
            root.GetProperty("commit").GetString(),
            root.GetProperty("referenceUrl").GetString(),
            root.GetProperty("inputUrl").GetString(),
            fetched);
    }
}
