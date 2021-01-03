﻿using System;

namespace DotnetCat.Utils
{
    /// <summary>
    /// Application console status configuration
    /// </summary>
    class Status
    {
        /// Initialize new object
        public Status(string level, string symbol, ConsoleColor color)
        {
            this.Level = level;
            this.Symbol = symbol;
            this.Color = color;
        }

        public string Level { get; }

        public string Symbol { get; }

        public ConsoleColor Color { get; }
    }
}
