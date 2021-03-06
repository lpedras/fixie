﻿namespace Fixie.Cli
{
    using System;

    class CommandLineException : Exception
    {
        public CommandLineException(string message, Exception innerException)
            : base(message, innerException) { }

        public CommandLineException(string message)
            : base(message) { }
    }
}
