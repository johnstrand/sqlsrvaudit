using System;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace SqlAudit.Tests;

public static class SqlExceptionHelper
{
    public static SqlException CreateSqlException(string message)
    {
        var errorCollection = Construct<SqlErrorCollection>();
        // SqlError ctor: Int32, Byte, Byte, String, String, String, Int32, Exception
        var error = Construct<SqlError>(0, (byte)0, (byte)0, "server", message, "procedure", 0, null);

        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance);
        addMethod!.Invoke(errorCollection, new object[] { error });

        // SqlException static method: CreateException - SqlErrorCollection, String
        var e = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(SqlErrorCollection), typeof(string) }, null);

        return (SqlException)e!.Invoke(null, new object[] { errorCollection, "8.0.0" })!;
    }

    private static T Construct<T>(params object?[] p)
    {
        var ctors = typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        var ctor = ctors.FirstOrDefault(c => c.GetParameters().Length == p.Length);
        if (ctor == null)
        {
            throw new InvalidOperationException($"Could not find ctor for {typeof(T).Name} with {p.Length} params");
        }
        return (T)ctor.Invoke(p);
    }
}
