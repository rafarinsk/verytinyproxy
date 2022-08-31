namespace VeryTinyProxy
{
    internal class ConnectCommand
    {
        public string? Address { get; set; }
        public string? HostAddress { get; set; }
        public ProxyAuth? Authorization { get; set; }
        public int HostPort { get; internal set; }
    }
}
