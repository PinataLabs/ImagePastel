// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Resolves conformance files that are not committed: the <c>.npy</c> references and the inputs
/// with restrictive licences. Files are cached in the gitignored <c>tests/Images/External/Jxl/.cache</c>.
/// A test that needs a missing file is skipped, unless <c>JXL_CONFORMANCE=1</c> is set: then the file
/// is downloaded, and a failed download fails the test.
/// <para>
/// Cached files are named by their sha256, and a download is checked before it lands in the cache.
/// So a cached file is never re-hashed: a re-pinned file gets a new name rather than reusing stale bytes.
/// </para>
/// </summary>
internal static class JxlConformanceData
{
    public const string RequiredVariable = "JXL_CONFORMANCE";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static bool IsRequired => Environment.GetEnvironmentVariable(RequiredVariable) == "1";

    public static string CacheDirectory => Path.Combine(TestEnvironment.SolutionDirectoryFullPath, "tests", "Images", "External", "Jxl", ".cache");

    /// <summary>Gets the path of the vector's input, downloading it if it is not committed.</summary>
    public static Task<string> GetInputPathAsync(JxlConformanceVector vector, CancellationToken cancellationToken)
    {
        if (!vector.IsInputFetched)
        {
            return Task.FromResult(vector.CommittedInputPath);
        }

        JxlConformanceSource source = JxlConformanceVector.Source;
        string url = source.InputUrl.Replace("{commit}", source.Commit).Replace("{id}", vector.InputId);
        return GetOrDownloadAsync(GetFetchedInputCachePath(vector), url, source.FetchedInputs[vector.InputId], cancellationToken);
    }

    /// <summary>Gets where a fetched input is cached: under its pinned sha256, never under its id.</summary>
    public static string GetFetchedInputCachePath(JxlConformanceVector vector)
        => Path.Combine(CacheDirectory, "inputs", JxlConformanceVector.Source.FetchedInputs[vector.InputId] + ".jxl");

    /// <summary>Gets the path of a reference file named in the vector's <c>test.json</c>, such as <c>reference_image.npy</c>.</summary>
    public static Task<string> GetReferencePathAsync(JxlConformanceVector vector, string fileName, CancellationToken cancellationToken)
    {
        // One small reference is committed upstream, so it is committed here too.
        string committed = Path.Combine(vector.DirectoryPath, fileName);
        if (File.Exists(committed))
        {
            return Task.FromResult(committed);
        }

        if (!vector.Sha256Sums.TryGetValue(fileName, out string sha256))
        {
            throw new ArgumentException($"{vector.Id}/test.json has no sha256 for '{fileName}'.", nameof(fileName));
        }

        string url = JxlConformanceVector.Source.ReferenceUrl.Replace("{sha256}", sha256);
        string path = Path.Combine(CacheDirectory, "objects", sha256);
        return GetOrDownloadAsync(path, url, sha256, cancellationToken);
    }

    public static async Task<NpyArray> LoadReferenceImageAsync(JxlConformanceVector vector, CancellationToken cancellationToken)
        => NpyArray.Load(await GetReferencePathAsync(vector, "reference_image.npy", cancellationToken));

    private static async Task<string> GetOrDownloadAsync(string path, string url, string sha256, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return path;
        }

        if (!IsRequired)
        {
            Assert.Skip($"Conformance data is not cached ({Path.GetFileName(path)}). Set {RequiredVariable}=1 to download it.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using FileStream file = File.Create(temp);
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            string actual = await ComputeSha256Async(temp, cancellationToken);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Downloaded {url} has sha256 {actual}, expected {sha256}.");
            }

            // Another test may have fetched the same file in the meantime; its copy is identical.
            File.Move(temp, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
