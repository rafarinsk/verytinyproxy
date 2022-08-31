using System.Text;

namespace VeryTinyProxy
{
    internal static class ConnectCommandParser
    {
        static readonly Parser terminator = new TerminatorParser();
        static readonly Parser authValue = new AuthValueParser(terminator);
        static readonly Parser authMethod = new AuthMethodParser(authValue);
        static readonly Parser auth = new AuthParser(authMethod, (TerminatorParser)terminator);
        static readonly Parser hostPort = new HostPortParser(auth);
        static readonly Parser hostAddress = new HostAddrParser(hostPort);
        static readonly Parser host = new HostParser(hostAddress);
        static readonly Parser http = new HttpParser(host);
        static readonly Parser addr = new AddrsParser(http);
        static readonly Parser connect = new ConnectParser(addr);
        static readonly Parser entryPoint = connect;
        public static ConnectCommand ParseConnectCommand(this Span<byte> source)
        {
            return entryPoint.Parse(source);
        }

        private abstract class Parser
        {
            protected readonly Parser next;

            protected static void Validate(Span<byte> buffer, string expected)
            {
                var received = Decode(buffer);
                if (!received.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Found invalid data while decoding. Expected {expected} - received {received}!");
                }
            }

            protected static ConnectCommand ParseNext(Span<byte> buffer, int start, Parser nextParser, ConnectCommand command)
            {
                return buffer.Length > start + 1 
                    ? nextParser.Parse(buffer[(start + 1)..], command) 
                    : throw new ArgumentOutOfRangeException(nameof(start), "start cannot be greate or equal to buffer.Length!");
            }

            protected abstract ConnectCommand Parse(Span<byte> buffer, ConnectCommand command);

            protected static string Decode(Span<byte> buffer)
            {
                return Encoding.UTF8.GetString(buffer);
            }

            protected Parser(Parser next)
            {
                this.next = next;
            }

            public ConnectCommand Parse(Span<byte> buffer)
            {
                var cmd = new ConnectCommand();
                return Parse(buffer, cmd);
            }
        }

        private class ConnectParser : Parser
        {
            public ConnectParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 32)
                    {

                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing CONNECT!");
            }
        }

        private class AddrsParser : Parser
        {
            public AddrsParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == ' ')
                    {
                        command.Address = Decode(buffer[..i]);
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing CONNECT address!");
            }
        }

        private class HttpParser : Parser
        {
            public HttpParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 10)
                    {
                        var http = buffer[..(i - 1)];
                        Validate(http, "HTTP/1.1");
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing http version!");
            }
        }

        private class HostParser : Parser
        {
            public HostParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 32)
                    {
                        var header = buffer[..i];
                        Validate(header, "Host:");
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Host header!");
            }
        }

        private class HostAddrParser : Parser
        {
            public HostAddrParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 58)
                    {
                        command.HostAddress = Decode(buffer[..i]);
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Host address!");
            }
        }

        private class HostPortParser : Parser
        {
            public HostPortParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 10)
                    {
                        var port = Decode(buffer[..(i - 1)]);
                        command.HostPort = int.Parse(port);
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Host port!");
            }
        }

        private class AuthParser : Parser
        {
            private readonly TerminatorParser terminator;
            public AuthParser(Parser next, TerminatorParser terminator) : base(next)
            {
                this.terminator = terminator;
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                if (terminator.TryParse(buffer))
                {
                    return command;
                }

                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 32)
                    {
                        var auth = buffer[..i];
                        Validate(auth, "Proxy-Authorization:");
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Proxy-Authorization header!");
            }
        }

        private class AuthMethodParser : Parser
        {
            public AuthMethodParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 32)
                    {
                        command.Authorization ??= new ProxyAuth();
                        command.Authorization.Method = Decode(buffer[..i]);
                        ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Proxy-Authorization method!");
            }
        }

        private class AuthValueParser : Parser
        {
            public AuthValueParser(Parser next) : base(next)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == 10)
                    {
                        command.Authorization ??= new ProxyAuth();
                        command.Authorization.Value = Decode(buffer[..i]);
                        return ParseNext(buffer, i, next, command);
                    }
                }

                throw new InvalidDataException("Error parsing Proxy-Authorization value!");
            }
        }

        private class TerminatorParser : Parser
        {
            public TerminatorParser() : base(null)
            {
            }

            protected override ConnectCommand Parse(Span<byte> buffer, ConnectCommand command)
            {
                if (TryParse(buffer))
                {
                    return command;
                }

                throw new InvalidDataException("Error parsing teminator sequence!");
            }

            public bool TryParse(Span<byte> buffer)
            {
                return buffer.Length == 2 && buffer[0] == 13 && buffer[1] == 10;
            }
        }
    }
}