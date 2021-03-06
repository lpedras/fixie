﻿namespace Fixie
{
    using System;
    using System.Linq;
    using System.Reflection;

    public static class ReflectionExtensions
    {
        public static string TypeName(this object o)
        {
            return o?.GetType().FullName;
        }

        public static bool IsVoid(this MethodInfo method)
        {
            return method.ReturnType == typeof(void);
        }

        public static bool IsStatic(this Type type)
        {
            return type.IsAbstract && type.IsSealed;
        }

        public static bool Has<TAttribute>(this Type type) where TAttribute : Attribute
        {
            return type.GetCustomAttributes<TAttribute>(true).Any();
        }

        public static bool Has<TAttribute>(this MethodInfo method) where TAttribute : Attribute
        {
            return method.GetCustomAttributes<TAttribute>(true).Any();
        }

        public static void Dispose(this object o)
        {
            (o as IDisposable)?.Dispose();
        }
    }
}