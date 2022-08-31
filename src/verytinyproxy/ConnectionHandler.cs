using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace VeryTinyProxy
{
    internal sealed class ConnectionHandler: IDisposable
    {
        private static readonly ReadOnlyMemory<byte> _ok = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("HTTP/1.1 200 Ok\r\n\r\n"));
        private static readonly string _badRequest = "HTTP/1.1 400 BadRequest\r\nContent-Lenght: {0}\r\n\r\n{1}";
        private readonly TcpClient inConnection;
        private readonly ILogger<ConnectionHandler> logger;
        private TcpClient? outConnection;
        private bool disposedValue;

        public ConnectionHandler(TcpClient newConnection, ILogger<ConnectionHandler> logger)
        {
            this.inConnection = newConnection;
            this.logger = logger;
        }

        public async ValueTask<(int outBytes, int inBytes)> HandleConnnection(CancellationToken cancellationToken)
        {
            try
            {
                var connectCommand = await ReadConnect(cancellationToken).ConfigureAwait(false);
                using var _ = logger.BeginScope(connectCommand.Address);
                await Authorize(connectCommand).ConfigureAwait(false);
                await Connect(connectCommand, cancellationToken).ConfigureAwait(false);
                await ReplyOk(cancellationToken).ConfigureAwait(false);
                return await Tunnel(cancellationToken).ConfigureAwait(false);
            }
            catch(InvalidDataException ex)
            {
                await ReplyBadRequest(ex.Message, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        private async ValueTask<ConnectCommand> ReadConnect(CancellationToken cancellationToken)
        {
            var buffer = MemoryPool<byte>.Shared.Rent(4096).Memory;
            var stream = inConnection.GetStream();
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            
            if (read == 0)
            {
                throw new InvalidDataException();
            }

            var command = buffer[..read].Span.ParseConnectCommand();
            return command;
        }

        private async ValueTask ReplyOk(CancellationToken cancellationToken)
        {
            var stream = inConnection.GetStream();
            await stream.WriteAsync(_ok, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(" << 200 OK");
        }

        private async ValueTask ReplyBadRequest(string message, CancellationToken cancellationToken)
        {
            var stream = inConnection.GetStream();
            var error = string.Format(_badRequest, Encoding.UTF8.GetByteCount(message), message);
            var buffer = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(error));
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            logger.LogWarning(" << 400 BadRequest : {message}", message);
        }

        private async ValueTask Connect(ConnectCommand command, CancellationToken cancellationToken)
        {
            if(command.HostAddress == null)
            {
                throw new InvalidOperationException($"{nameof(command.HostAddress)} cannot be null!");
            }
            logger.LogInformation("Connecting to {outConnection}...", command.Address);
            outConnection = new TcpClient();
            var sw = Stopwatch.StartNew();
            await outConnection.ConnectAsync(command.HostAddress, command.HostPort, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Connected in {connectionElapsed}ms.", sw.ElapsedMilliseconds);
        }

        private ValueTask Authorize(ConnectCommand command)
        {
            return ValueTask.CompletedTask;
        }

        private async ValueTask<(int outBytes, int inBytes)> Tunnel(CancellationToken cancellationToken)
        {
            if(outConnection == null)
            {
                throw new InvalidOperationException($"{nameof(outConnection)} cannot be null!");
            }
            using var inStream = inConnection.GetStream();
            using var outStream = outConnection.GetStream();
            return await inStream.Tunnel(outStream, cancellationToken, logger).ConfigureAwait(false);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    inConnection.Close();
                    outConnection?.Close();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}