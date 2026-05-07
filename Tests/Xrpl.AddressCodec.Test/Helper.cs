using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace XrplTests;

internal static class Helper
{
    public static T Throws<T>(Action action, string message = null) where T : Exception
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
}
