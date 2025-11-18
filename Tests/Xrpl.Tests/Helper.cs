using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Threading.Tasks;

namespace XrplTests;

internal static class Helper
{
    public static T ThrowsException<T>(Action action, string? message = null) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            if (message != null)
            {
                Assert.AreEqual(message, ex.Message);
            }

            return ex;
        }

        Assert.Fail($"Expected exception of type {typeof(T)}");
        return null!;
    }
    public static async Task<T> ThrowsExceptionAsync<T>(Func<Task> action, string? message = null) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T ex)
        {
            if (message != null)
            {
                Assert.AreEqual(message, ex.Message);
            }

            return ex;
        }

        Assert.Fail($"Expected exception of type {typeof(T)}");
        return null!;
    }
}
