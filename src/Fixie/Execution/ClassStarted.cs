﻿namespace Fixie.Execution
{
    using System;

    public class ClassStarted : Message
    {
        public ClassStarted(Type @class)
        {
            Class = @class;
        }

        public Type Class { get; }
    }
}