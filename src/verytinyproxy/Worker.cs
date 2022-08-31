using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;

namespace VeryTinyProxy
{
    public class Worker : BackgroundService
    {
        private readonly ProxyOptions options;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<Worker> _logger;
        
        public Worker(IOptions<ProxyOptions> options, ILoggerFactory loggerFactory)
        {
            this.options = options.Value;
            this.loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Worker>();

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            var requestHandler = new TcpListener(options.GetEndPoint());
            requestHandler.Start();
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var connection = await requestHandler.AcceptTcpClientAsync(stoppingToken);
                    HandleConnection(connection, stoppingToken);
                }
            } catch (OperationCanceledException)
            {

            }
            _logger.LogInformation("Worker stopped at: {time}", DateTimeOffset.Now);
        }

        private async Task HandleConnection(TcpClient connection, CancellationToken cancellationToken)
        {
            var remoteEP = connection.Client.RemoteEndPoint;
            using var logScope = _logger.BeginScope(remoteEP);
            _logger.LogInformation("New connection.");
            var handler = new ConnectionHandler(connection, loggerFactory.CreateLogger<ConnectionHandler>());
            try
            {
                var (outBytes, inBytes) = await handler.HandleConnnection(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("bytes sent: {outBytes} in bytes received: {inBytes}.", outBytes, inBytes);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidDataException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{errorMessage}", ex.Message);
            }
            finally
            {
                connection.Close();
            }
        }
    }
}