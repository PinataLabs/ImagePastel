// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// The download into the conformance cache, which tests share: several tests may need the same file at
/// once, and CI starts with an empty cache. A fake fetch stands in for the network.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlConformanceDataTests
{
    private static readonly byte[] Content = { 1, 2, 3, 4, 5, 6, 7, 8 };

    private static readonly string ContentSha256 = Convert.ToHexStringLower(SHA256.HashData(Content));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Two tests that need the same file into an empty cache: one fetch serves both.
    [Fact]
    public async Task DownloadAsync_SameFileTwiceAtOnce_FetchesOnceAndGivesBothThePath()
    {
        string path = CachePath();
        int fetches = 0;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Fetch(Stream file, CancellationToken token)
        {
            Interlocked.Increment(ref fetches);
            await release.Task.WaitAsync(token);
            await file.WriteAsync(Content, token);
        }

        Task<string> first = JxlConformanceData.DownloadAsync(path, ContentSha256, "fake", Fetch, CancellationToken);
        Task<string> second = JxlConformanceData.DownloadAsync(path, ContentSha256, "fake", Fetch, CancellationToken);
        release.SetResult();

        Assert.Equal(path, await first);
        Assert.Equal(path, await second);
        Assert.Equal(1, fetches);
        Assert.Equal(Content, await File.ReadAllBytesAsync(path, CancellationToken));
    }

    // Another process (the other target framework's test run) caches the same file while this one
    // fetches it, and a test is reading it: the copy already there has the same bytes, so it stays.
    [Fact]
    public async Task DownloadAsync_FileCachedAndOpenMeanwhile_KeepsItAndGivesThePath()
    {
        string path = CachePath();
        FileStream reader = null;
        async Task Fetch(Stream file, CancellationToken token)
        {
            await File.WriteAllBytesAsync(path, Content, token);
            reader = File.OpenRead(path);
            await file.WriteAsync(Content, token);
        }

        try
        {
            Assert.Equal(path, await JxlConformanceData.DownloadAsync(path, ContentSha256, "fake", Fetch, CancellationToken));
        }
        finally
        {
            reader?.Dispose();
        }

        Assert.Equal(Content, await File.ReadAllBytesAsync(path, CancellationToken));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task DownloadAsync_WrongHash_ThrowsAndCachesNothing()
    {
        string path = CachePath();
        Task Fetch(Stream file, CancellationToken token) => file.WriteAsync(Content, token).AsTask();

        await Assert.ThrowsAsync<InvalidDataException>(() => JxlConformanceData.DownloadAsync(path, new string('0', 64), "fake", Fetch, CancellationToken));

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)));
    }

    // A cache of its own for each test, empty, as on CI.
    private static string CachePath()
    {
        string directory = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlConformanceDataTests)), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, ContentSha256);
    }
}
