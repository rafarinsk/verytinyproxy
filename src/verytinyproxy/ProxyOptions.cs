using System.Net;

namespace VeryTinyProxy
{
    public class ProxyOptions
    {
        public int Port { get; internal set; } = 22222;

        internal IPEndPoint GetEndPoint()
        {
            return new IPEndPoint(IPAddress.Any, Port);
        }
    }
}