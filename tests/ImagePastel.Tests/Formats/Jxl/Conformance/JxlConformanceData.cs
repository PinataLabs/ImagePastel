// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// <para>
/// Tests run in parallel, and CI starts with an empty cache, so several tests may need the same file at
/// once: within the test process one download serves them all, and a file another process cached in the
/// meantime is kept, as it has the same bytes.
/// </para>
/// </summary>
internal static class JxlConformanceData
{
    public const string RequiredVariable = "JXL_CONFORMANCE";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // The download of each cache path in flight, so that tests needing the same file share one.
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Downloads = new(StringComparer.OrdinalIgnoreCase);

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

        return await DownloadAsync(path, sha256, url, (file, token) => FetchAsync(url, file, token), cancellationToken);
    }

    /// <summary>
    /// Downloads a file into the cache at <paramref name="path"/>, checking its sha256 first. Calls for
    /// the same path while a download is in flight share it; a failed download is not kept, so a later
    /// call tries again.
    /// </summary>
    /// <param name="path">The cache path.</param>
    /// <param name="sha256">The file's pinned sha256.</param>
    /// <param name="source">Where the file comes from, for the error message.</param>
    /// <param name="fetch">Writes the file's bytes to the stream it is given.</param>
    /// <param name="cancellationToken">Cancels the wait of this caller; the shared download runs on.</param>
    /// <returns>The cache path.</returns>
    internal static async Task<string> DownloadAsync(string path, string sha256, string source, Func<Stream, CancellationToken, Task> fetch, CancellationToken cancellationToken)
    {
        Lazy<Task<string>> download = Downloads.GetOrAdd(path, _ => new Lazy<Task<string>>(() => DownloadOnceAsync(path, sha256, source, fetch)));
        try
        {
            return await download.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            // Once the file is in the cache, File.Exists finds it, so the entry is only needed in flight.
            if (download.Value.IsCompleted)
            {
                Downloads.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(path, download));
            }
        }
    }

    // The shared download is not cancelled with any one caller; the HTTP timeout bounds it.
    private static async Task<string> DownloadOnceAsync(string path, string sha256, string source, Func<Stream, CancellationToken, Task> fetch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream file = File.Create(temp))
            {
                await fetch(file, CancellationToken.None);
            }

            string actual = await ComputeSha256Async(temp, CancellationToken.None);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Downloaded {source} has sha256 {actual}, expected {sha256}.");
            }

            try
            {
                File.Move(temp, path, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(path))
            {
                // Another process cached the file in the meantime, and a test may be reading it, so it
                // cannot be replaced. It is named by its sha256 and was checked too, so it is the same.
            }

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

    private static async Task FetchAsync(string url, Stream file, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(file, cancellationToken);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
