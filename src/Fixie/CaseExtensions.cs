﻿namespace Fixie
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    public static class CaseExtensions
    {
        public static void Execute(this Case @case, object instance)
        {
            try
            {
                var method = @case.Method;

                bool isDeclaredAsync = method.IsAsync();

                if (isDeclaredAsync && method.IsVoid())
                    ThrowForUnsupportedAsyncVoid();

                object returnValue;
                try
                {
                    if (method.ContainsGenericParameters)
                        throw new Exception("Could not resolve type parameters for generic test case.");

                    returnValue = method.Invoke(instance, @case.Parameters);
                }
                catch (TargetInvocationException exception)
                {
                    throw new PreservedException(exception.InnerException);
                }

                if (isDeclaredAsync)
                {
                    var task = (Task)returnValue;
                    try
                    {
                        task.Wait();
                    }
                    catch (AggregateException exception)
                    {
                        throw new PreservedException(exception.InnerExceptions.First());
                    }

                    if (method.ReturnType.IsGenericType)
                    {
                        var property = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);

                        returnValue = property.GetValue(task, null);
                    }
                    else
                    {
                        returnValue = null;
                    }
                }

                @case.ReturnValue = returnValue;
            }
            catch (Exception exception)
            {
                @case.Fail(exception);
            }

            @case.Executed = true;
        }

        static void ThrowForUnsupportedAsyncVoid()
        {
            throw new NotSupportedException(
                "Async void methods are not supported. Declare async methods with a " +
                "return type of Task to ensure the task actually runs to completion.");
        }
    }
}