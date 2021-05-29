using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DotnetCat.Enums;
using DotnetCat.Handlers;
using DotnetCat.Nodes;

namespace DotnetCat
{
    /// <summary>
    /// Primary application startup object
    /// </summary>
    class Program
    {
        private static Parser _parser;  // Cmd-line argument parser

        /// Enable verbose console output
        public static bool Verbose => SockNode?.Verbose ?? false;

        /// Enable verbose exceptions
        public static bool Debug { get; set; }

        /// Using executable pipeline
        public static bool UsingExe { get; set; }

        /// Pipeline variant
        public static PipeType PipeVariant { get; set; }

        /// User-defined string payload
        public static string Payload { get; set; }

        /// Command-line arguments
        public static List<string> Args { get; set; }

        /// Network socket node
        public static Node SockNode { get; set; }

        /// Operating system
        public static Platform OS { get; private set; }

        /// File transfer option type
        public static TransferOpt Transfer { get; private set; }

        /// <summary>
        /// Primary application entry point
        /// </summary>
        private static void Main(string[] args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                OS = Platform.Win;
            }
            else
            {
                OS = Platform.Nix;
            }

            _parser = new Parser();

            // Display help info and exit
            if ((args.Length == 0) || Parser.NeedsHelp(args))
            {
                _parser.PrintHelp();
            }

            InitializeNode(args);
            ConnectNode();

            Console.WriteLine();
            Environment.Exit(0);
        }

        /// <summary>
        /// Initialize node fields and properties
        /// </summary>
        private static void InitializeNode(string[] args)
        {
            // Check for incomplete alias
            if (args.Contains("-"))
            {
                Error.Handle(Except.InvalidArgs, "-", true);
            }

            // Check for incomplete flag
            if (args.Contains("--"))
            {
                Error.Handle(Except.InvalidArgs, "--", true);
            }

            UsingExe = false;
            Args = DefragArgs(args);

            List<string> lowerArgs = new();
            Args?.ForEach(arg => lowerArgs.Add(arg.ToLower()));

            int index;

            // Discard 'NoExit' cmd-line args options
            if ((index = lowerArgs.IndexOf("-noexit")) > -1)
            {
                Args.RemoveAt(index);
            }

            Transfer = GetTransferOpts();
            index = Parser.IndexOfFlag("--listen", 'l');

            // Determine if node is client/server
            if ((index > -1) || (Parser.IndexOfAlias('l') > -1))
            {
                SockNode = new ServerNode();
                return;
            }
            SockNode = new ClientNode();
        }

        /// <summary>
        /// Ensure string-literal arguments aren't fragmented
        /// </summary>
        private static List<string> DefragArgs(string[] args)
        {
            int delta = 0;
            List<string> list = args.ToList();

            // Get arguments starting with quote
            var query = from arg in args
                        let pos = Array.IndexOf(args, arg)
                        let quote = arg.FirstOrDefault()
                        let valid = arg.EndsWith(quote) && arg.Length >= 2
                        where arg.StartsWith("'")
                            || arg.StartsWith("\"")
                        select new { arg, pos, quote, valid };

            foreach (var item in query)
            {
                if (delta > 0)  // Skip processed arguments
                {
                    delta -= 1;
                    continue;
                }
                int listIndex = list.IndexOf(item.arg);

                // Non-fragmented string
                if (item.valid)
                {
                    list[listIndex] = item.arg[1..(item.arg.Length - 1)];
                    continue;
                }

                // Get argument containing string EOL
                var eolQuery = (from arg in args
                                let pos = Array.IndexOf(args, arg, item.pos + 1)
                                where pos > item.pos
                                    && (arg == item.quote.ToString()
                                        || arg.EndsWith(item.quote))
                                select new { arg, pos }).FirstOrDefault();

                // Missing EOL (quote)
                if (eolQuery is null)
                {
                    Error.Handle(Except.StringEOL,
                                 string.Join(", ", args[item.pos..]), true);
                }

                delta = eolQuery.pos - item.pos;
                int endIndex = item.pos + delta;

                // Append fragments and remove duplicates
                for (int i = item.pos + 1; i < endIndex + 1; i++)
                {
                    list[listIndex] += $" {args[i]}";
                    list.Remove(args[i]);
                }

                string defragged = list[listIndex];
                list[listIndex] = defragged[1..(defragged.Length - 1)];
            }
            return list;
        }

        /// <summary>
        /// Get the file/socket communication operation type
        /// </summary>
        private static TransferOpt GetTransferOpts()
        {
            int outIndex = Parser.IndexOfFlag("--output", 'o');

            // Receive file data
            if ((outIndex > -1) || (Parser.IndexOfAlias('o') > -1))
            {
                return TransferOpt.Collect;
            }
            int sendIndex = Parser.IndexOfFlag("--send", 's');

            // Send file data
            if ((sendIndex > -1) || (Parser.IndexOfAlias('s') > -1))
            {
                return TransferOpt.Transmit;
            }
            return TransferOpt.None;
        }

        /// <summary>
        /// Parse arguments and initiate connection
        /// </summary>
        private static void ConnectNode()
        {
            _parser.ParseCharArgs();
            _parser.ParseFlagArgs();

            // Validate remaining cmd-line arguments
            switch (Args.Count)
            {
                case 0:   // Missing TARGET
                {
                    if (SockNode is ClientNode)
                    {
                        Error.Handle(Except.RequiredArgs, "TARGET", true);
                    }
                    break;
                }
                case 1:   // Validate TARGET
                {
                    if (Args[0].StartsWith('-'))
                    {
                        Error.Handle(Except.UnknownArgs, Args[0], true);
                    }

                    // Parse or resolve IP address
                    if (IPAddress.TryParse(Args[0], out IPAddress addr))
                    {
                        SockNode.Addr = addr;
                    }
                    else
                    {
                        SockNode.Addr = ResolveHostName(Args[0]);
                    }

                    // Invalid destination host
                    if (SockNode.Addr is null)
                    {
                        Error.Handle(Except.InvalidAddr, Args[0], true);
                    }
                    break;
                }
                default:  // Unexpected arguments
                {
                    string argsStr = string.Join(", ", Args);

                    if (Args[0].StartsWith('-'))
                    {
                        Error.Handle(Except.UnknownArgs, argsStr, true);
                    }
                    Error.Handle(Except.InvalidArgs, argsStr, true);
                    break;
                }
            }
            SockNode.Connect();
        }

        /// <summary>
        /// Resolve the IPv4 address of given hostname
        /// </summary>
        private static IPAddress ResolveHostName(string hostName)
        {
            IPHostEntry dnsAns;
            string machineName = Environment.MachineName;

            try  // Resolve host name as IP address
            {
                dnsAns = Dns.GetHostEntry(hostName);

                if (dnsAns.AddressList.Contains(IPAddress.Loopback))
                {
                    return IPAddress.Loopback;
                }
            }
            catch (SocketException)  // No DNS entries found
            {
                return null;
            }

            if (dnsAns.HostName.ToLower() != machineName.ToLower())
            {
                foreach (IPAddress addr in dnsAns.AddressList)
                {
                    // Return the first IPv4 address
                    if (addr.AddressFamily is AddressFamily.InterNetwork)
                    {
                        return addr;
                    }
                }
                return null;
            }

            using Socket socket = new(AddressFamily.InterNetwork,
                                      SocketType.Dgram,
                                      ProtocolType.Udp);

            // Get active local IP address
            socket.Connect("8.8.8.8", 53);
            return (socket.LocalEndPoint as IPEndPoint).Address;
        }
    }
}
