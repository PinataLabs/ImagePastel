// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.ImageSharp.Tests.TestUtilities.Tests;

public class SemaphoreReadMemoryStreamTests
{
    private readonly SemaphoreSlim continueSemaphore = new SemaphoreSlim(0);
    private readonly SemaphoreSlim notifyWaitPositionReachedSemaphore = new SemaphoreSlim(0);
    private readonly byte[] buffer = new byte[128];

    [Fact]
    public void Read_BeforeWaitLimit_ShouldFinish()
    {
        using Stream stream = this.CreateTestStream();
        int read = stream.Read(this.buffer);
        Assert.Equal(this.buffer.Length, read);
    }

    [Fact]
    public async Task ReadAsync_BeforeWaitLimit_ShouldFinish()
    {
        using Stream stream = this.CreateTestStream();
        int read = await stream.ReadAsync(this.buffer, 0, this.buffer.Length, TestContext.Current.CancellationToken);
        Assert.Equal(this.buffer.Length, read);
    }

    [Fact]
    public async Task Read_AfterWaitLimit_ShouldPause()
    {
        using Stream stream = this.CreateTestStream();
        _ = stream.Read(this.buffer);
        Assert.Equal(0, this.notifyWaitPositionReachedSemaphore.CurrentCount);

        Task readTask = Task.Factory.StartNew(
            () =>
            {
                _ = stream.Read(this.buffer);
                _ = stream.Read(this.buffer);
                _ = stream.Read(this.buffer);
                _ = stream.Read(this.buffer);
                _ = stream.Read(this.buffer);
            },
            TaskCreationOptions.LongRunning);

        await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);
        await this.notifyWaitPositionReachedSemaphore.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);
        this.continueSemaphore.Release();
        await readTask;
    }

    [Fact]
    public async Task ReadAsync_AfterWaitLimit_ShouldPause()
    {
        using Stream stream = this.CreateTestStream();
        _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length, TestContext.Current.CancellationToken);

        Task readTask = Task.Factory.StartNew(
            async () =>
            {
                _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length);
                _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length);
                _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length);
                _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length);
                _ = await stream.ReadAsync(this.buffer, 0, this.buffer.Length);
            },
            TaskCreationOptions.LongRunning);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);
        await this.notifyWaitPositionReachedSemaphore.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.False(readTask.IsCompleted);
        this.continueSemaphore.Release();
        await readTask;
    }

    private Stream CreateTestStream(int size = 1024, int waitAfterPosition = 256)
    {
        byte[] buffer = new byte[size];
        return new SemaphoreReadMemoryStream(buffer, waitAfterPosition, this.notifyWaitPositionReachedSemaphore, this.continueSemaphore);
    }
}
