﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using DotnetCat.Enums;
using DotnetCat.Nodes;

namespace DotnetCat.Handlers
{
    /// <summary>
    /// Command line argument parser and validator
    /// </summary>
    class ArgumentParser
    {
        private readonly CommandHandler _cmd;

        private readonly ErrorHandler _error;

        /// Initialize new object
        public ArgumentParser()
        {
            _cmd = new CommandHandler();
            _error = new ErrorHandler();

            this.Help = GetHelp(GetUsage());
        }

        public string Help { get; }

        public List<string> Args
        {
            get => Program.Args;
            set => Program.Args = value;
        }

        public SocketNode SockNode
        {
            get => Program.SockNode;
            set => Program.SockNode = value;
        }

        public static string GetUsage()
        {
            return $"Usage: {GetAppTitle()} [OPTIONS] TARGET";
        }

        /// Print application help message to console output
        public void PrintHelp()
        {
            Console.WriteLine(Help);
            Environment.Exit(0);
        }

        /// Get index of cmd-line argument with the specified char
        public int IndexOfAlias(char alias)
        {
            List<int> query = (from arg in Args
                               where arg.Contains(alias)
                                   && arg[0] == '-'
                                   && arg[1] != '-'
                               select Args.IndexOf(arg)).ToList();

            return (query.Count() > 0) ? query[0] : -1;
        }

        /// Get the index of argument in cmd-line arguments
        public int IndexOfFlag(string flag, char? alias = null)
        {
            if (flag == "-")
            {
                return Args.IndexOf(flag);
            }

            // Assign argument alias
            if (string.IsNullOrEmpty(alias.ToString()))
            {
                foreach (char ch in alias.ToString())
                {
                    if (char.IsLetter(ch))
                    {
                        alias = ch;
                    }
                }
            }
            //alias ??= (flag.Length == 2) ? flag[1] : flag[2];

            // Locate matching arguments
            List<int> query = (from arg in Args
                               where arg.ToLower() == flag.ToLower()
                                   || arg == $"-{alias}"
                               select Args.IndexOf(arg)).ToList();

            return (query.Count() > 0) ? query[0] : -1;
        }

        /// Get value of an argument in cmd-line arguments
        public string ArgsValueAt(int index)
        {
            if ((index < 0) || (index >= Args.Count))
            {
                _error.Handle(Except.NamedArg, Args[index - 1], true);
            }

            return Args[index];
        }

        /// Check for help flag in cmd-line arguments
        public bool NeedsHelp(string[] args)
        {
            List<string> query = (from arg in args
                                  where arg.ToLower() == "--help"
                                      || (arg.Length > 1
                                          && arg[0] == '-'
                                          && arg[1] != '-'
                                          && (arg.Contains('h')
                                              || arg.Contains('?')))
                                  select arg).ToList();

            return query.Count() > 0;
        }

        /// Remove named argument/value in cmd-line arguments
        public void RemoveFlag(string arg, bool noValue = false)
        {
            int index = IndexOfFlag(arg);
            int length = noValue ? 1 : 2;

            for (int i = 0; i < length; i++)
            {
                Args.RemoveAt(index);
            }
        }

        /// Remove character of a cmd-line argument
        public void RemoveAlias(int index, char alias)
        {
            Args[index] = Args[index].Replace(alias.ToString(), "");
        }

        /// Enable verbose standard output
        public void SetVerbose(int argIndex, Argument type)
        {
            SockNode.Verbose = true;

            if (type == Argument.Flag)
            {
                Args.RemoveAt(argIndex);
            }
            else
            {
                RemoveAlias(argIndex, 'v');
            }
        }

        /// Transfer directory children recursively
        public void SetRecurse(int argIndex, Argument type)
        {
            Program.Recursive = true;

            if (type == Argument.Flag)
            {
                Args.RemoveAt(argIndex);
            }
            else
            {
                RemoveAlias(argIndex, 'r');
            }
        }

        /// Specify the local or remote host
        public void SetAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentNullException(nameof(address));
            }

            (bool isValid, IPAddress addr) = IsValidAddress(address);

            if (!isValid)
            {
                _error.Handle(Except.InvalidAddr, address, true);
            }

            SockNode.Addr = addr;
        }

        /// Determine if specified address is valid
        public (bool valid, IPAddress) IsValidAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return (false, null);
            }

            // Parse address string as IP
            try
            {
                return (true, IPAddress.Parse(address));
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException))
                {
                    throw ex;
                }
            }

            IPAddress addr = ResolveHostName(address);
            return (addr == null) ? (false, null) : (true, addr);
        }

        /// Resolve the IPv4 address of given hostname
        public IPAddress ResolveHostName(string hostName)
        {
            IPHostEntry dnsAns;
            string machineName = Environment.MachineName;

            // Resolve host name as IP address
            try
            {
                dnsAns = Dns.GetHostEntry(hostName);

                if (dnsAns.AddressList.Contains(IPAddress.Loopback))
                {
                    return IPAddress.Loopback;
                }
            }
            catch (SocketException)
            {
                return null;
            }

            if (dnsAns.HostName.ToLower() != machineName.ToLower())
            {
                foreach (IPAddress addr in dnsAns.AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return addr;
                    }
                }
                return null;
            }

            Socket socket = new Socket(AddressFamily.InterNetwork,
                                       SocketType.Dgram,
                                       ProtocolType.Udp);
            using (socket)
            {
                socket.Connect("8.8.8.8", 53);
                return (socket.LocalEndPoint as IPEndPoint).Address;
            }
        }

        /// Specify shell executable for command execution
        public void SetExec(int argIndex, Argument type)
        {
            string exec = ArgsValueAt(argIndex + 1);
            (bool exists, string path) = _cmd.ExistsOnPath(exec);

            // Failed to locate executable
            if (!exists)
            {
                _error.Handle(Except.ShellPath, exec, true);
            }

            Program.UsingExe = true;
            SockNode.Exe = path;

            // Remove argument flag
            if (type == Argument.Flag)
            {
                RemoveFlag("--exec");
                return;
            }

            RemoveAlias(argIndex, 'e');
            Args.RemoveAt(argIndex + 1);
        }

        /// Specify file path to output socket data
        public void SetCollect(int argIndex, Argument type)
        {
            string path = ArgsValueAt(argIndex + 1);
            SetFilePath(path);

            if (type == Argument.Flag)
            {
                RemoveFlag("--output");
                return;
            }

            RemoveAlias(argIndex, 'o');
            Args.RemoveAt(argIndex + 1);
        }

        /// Specify the file path for 
        public void SetTransmit(int argIndex, Argument type)
        {
            SetFilePath(Path.GetFullPath(ArgsValueAt(argIndex + 1)));

            if (type == Argument.Flag)
            {
                RemoveFlag("--send");
                return;
            }

            RemoveAlias(argIndex, 's');
            Args.RemoveAt(argIndex + 1);
        }

        /// Specify file path for file stream manipulation
        public void SetFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            // Determine if file location is valid
            if (!File.Exists(path) && !Directory.GetParent(path).Exists)
            {
                _error.Handle(Except.FilePath, path);
            }
            SockNode.FilePath = path;
        }

        /// Specify the port to use for connection
        public void SetPort(int argIndex, Argument type)
        {
            int portNum = -1;
            string port = ArgsValueAt(argIndex + 1);

            try
            {
                portNum = int.Parse(port);

                if ((portNum < 0) || (portNum > 65535))
                {
                    throw new FormatException();
                }
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException))
                {
                    throw ex;
                }
                _error.Handle(Except.InvalidPort, port);
            }
            SockNode.Port = portNum;

            if (type == Argument.Flag)
            {
                RemoveFlag("--port");
                return;
            }

            RemoveAlias(argIndex, 'p');
            Args.RemoveAt(argIndex + 1);
        }

        /// Get application help message as a string
        private static string GetHelp(string appUsage)
        {
            string appTitle = GetAppTitle();

            return string.Join("\r\n", new string[]
            {
                "DotnetCat (https://github.com/vandavey/DotnetCat)",
                $"{appUsage}\r\n",
                "Remote command shell application\r\n",
                "Positional Arguments:",
                "  TARGET                   Specify remote/local IPv4 address\r\n",
                "Optional Arguments:",
                "  -h/-?,   --help           Show this help message and exit",
                "  -v,      --verbose        Enable verbose console output",
                "  -l,      --listen         Listen for incoming connections",
                "  -r,      --recurse        Transfer a directory recursively",
                "  -p PORT, --port PORT      Specify port to use for endpoint.",
                "                            (Default: 4444)",
                "  -e EXEC, --exec EXEC      Specify command shell executable",
                "  -o PATH, --output PATH    Receive file from remote host",
                "  -s PATH, --send PATH      Send local file or folder\r\n",
                "Usage Examples:",
                $"  {appTitle} -le powershell.exe",
                $"  {appTitle} 10.0.0.152 -p 4444 localhost",
                $"  {appTitle} -ve /bin/bash 192.168.1.9\r\n",
            });
        }

        /// Get program title based on platform
        private static string GetAppTitle()
        {
            if (Program.OS == Platform.Unix)
            {
                return "dncat";
            }
            return "dncat.exe";
        }
    }
}
