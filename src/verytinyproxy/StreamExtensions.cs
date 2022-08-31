using System.Buffers;
using System.Runtime.CompilerServices;

namespace VeryTinyProxy
{
    internal static class StreamExtensions
    {
        public static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(this Stream source,[EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var buffer = MemoryPool<byte>.Shared.Rent(4096).Memory;
            int read;
            while(!cancellationToken.IsCancellationRequested)
            {
                read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    yield break;
                }
                else
                {
                    yield return buffer[..read];
                }
            }
        }

        public static async ValueTask<int> PipeToAsync(this Stream source, Stream destination, CancellationToken cancellationToken, Action<int>? pipedDataCallback = null)
        {
            var piped = 0;
            try
            {
                await foreach (var buffer in source.ReadAllAsync(cancellationToken))
                {
                    await destination.WriteAsync(buffer, cancellationToken);
                    piped += buffer.Length;
                    pipedDataCallback?.Invoke(buffer.Length);
                }
            }catch(OperationCanceledException)
            {

            }
            return piped;
        }

        public static async ValueTask<(int outBytes, int inBytes)> Tunnel(this Stream inStream, Stream outStream, CancellationToken cancellationToken, ILogger? logger = null)
        {
            var cts = new CancellationTokenSource();
            var lcts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            Action<int>? pipedOut = logger == null ? null : (bytes) => logger.LogTrace(" >> {outBytes}", bytes);
            Action<int>? pipedIn = logger == null ? null : (bytes) => logger.LogTrace(" << {inBytes}", bytes);
            var outPipe = inStream.PipeToAsync(outStream, lcts.Token, pipedOut);
            var inPipe = outStream.PipeToAsync(inStream, lcts.Token, pipedIn);
            await Task.WhenAny(outPipe.AsTask(), inPipe.AsTask());
            cts.Cancel();
            return (await outPipe, await inPipe);
        }
    }
}