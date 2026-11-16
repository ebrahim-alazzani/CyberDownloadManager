using System.Net;
using System.Net.Http.Headers;
using CyberDownloadManager.Infrastructure.Engine;
using Xunit;

namespace CyberDownloadManager.Infrastructure.Tests;

public sealed class DownloadEngineTests
{
    [Fact]
    public async Task StartAsync_ShouldAssembleRequestedRangesInOrder()
    {
        var data = Enumerable.Range(0, 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
        using var client = new HttpClient(new RangeHandler(data));
        using var engine = new DownloadEngineImpl(httpClient: client);
        var path = Path.Combine(Path.GetTempPath(), $"cdm-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await engine.StartAsync(Guid.NewGuid(), "https://example.test/file.bin", path, 4);

            Assert.True(result.IsSuccess, result.Error.Description);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".download")) File.Delete(path + ".download");
            for (var index = 0; index < 4; index++)
                if (File.Exists($"{path}.part{index}")) File.Delete($"{path}.part{index}");
        }
    }

    [Fact]
    public async Task StartAsync_ShouldFallBackWhenServerIgnoresRangeRequests()
    {
        var data = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
        using var client = new HttpClient(new RangeHandler(data, ignoreRanges: true));
        using var engine = new DownloadEngineImpl(httpClient: client);
        var path = Path.Combine(Path.GetTempPath(), $"cdm-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await engine.StartAsync(Guid.NewGuid(), "https://example.test/file.bin", path, 4);

            Assert.True(result.IsSuccess, result.Error.Description);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".download")) File.Delete(path + ".download");
            for (var index = 0; index < 4; index++)
                if (File.Exists($"{path}.part{index}")) File.Delete($"{path}.part{index}");
        }
    }

    [Fact]
    public async Task PauseAndRestart_ShouldResumeFromTheExistingPartialFile()
    {
        var data = Enumerable.Range(0, 512 * 1024).Select(i => (byte)(i % 239)).ToArray();
        using var client = new HttpClient(new DelayedRangeHandler(data));
        using var engine = new DownloadEngineImpl(httpClient: client);
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"cdm-{Guid.NewGuid():N}.bin");
        try
        {
            var firstAttempt = engine.StartAsync(id, "https://example.test/file.bin", path, 1);
            await WaitUntilAsync(() => File.Exists(path + ".download") && new FileInfo(path + ".download").Length > 0);
            Assert.True((await engine.PauseAsync(id)).IsSuccess);
            Assert.True((await firstAttempt).IsFailure);

            var resumed = await engine.StartAsync(id, "https://example.test/file.bin", path, 1);

            Assert.True(resumed.IsSuccess, resumed.Error.Description);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".download")) File.Delete(path + ".download");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("The partial download was not created.");
            await Task.Delay(10);
        }
    }

    private sealed class RangeHandler(byte[] data, bool ignoreRanges = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                head.Content.Headers.ContentLength = data.Length;
                head.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(head);
            }

            if (ignoreRanges || request.Headers.Range?.Ranges.FirstOrDefault() is not { } range)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

            var start = checked((int)range.From!.Value);
            var end = checked((int)Math.Min(range.To!.Value, data.Length - 1));
            var body = data[start..(end + 1)];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, data.Length);
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedRangeHandler(byte[] data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                head.Content.Headers.ContentLength = data.Length;
                head.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(head);
            }

            var offset = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            var body = data[(int)offset..];
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedReadStream(body))
            };
            response.Content.Headers.ContentLength = body.Length;
            if (offset > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedReadStream(byte[] data) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken);
            var count = Math.Min(Math.Min(buffer.Length, 8 * 1024), data.Length - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
