using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace PatentMarker.FactRunner;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: PatentMarker.FactRunner <test-assembly.dll>");
            return 2;
        }
        string assemblyPath = Path.GetFullPath(args[0]);
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("Test assembly not found: " + assemblyPath);
            return 2;
        }

        Environment.CurrentDirectory = Path.GetDirectoryName(assemblyPath)!;
        var context = new TestAssemblyLoadContext(assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);

        Console.WriteLine("FactRunner subset: public, parameterless, synchronous void instance Fact; no xUnit fixtures or theories.");
        int passed = 0, failed = 0, skipped = 0, unsupported = 0;
        var failures = new List<string>();
        Type[] types = assembly.GetTypes();
        if (types.Any(t => t.GetInterfaces().Any(i => IsXunitFixture(i, "Xunit.ICollectionFixture"))))
        {
            Console.WriteLine("SKIP/BLOCKED: xUnit collection fixtures are unsupported.");
            Console.WriteLine("SUMMARY: passed=0 failed=0 skipped=1");
            return 1;
        }
        if (HasUnsupportedExecutionAttributes(assembly, types))
        {
            Console.WriteLine("SKIP/BLOCKED: xUnit execution hooks, ordering and custom frameworks are unsupported.");
            Console.WriteLine("SUMMARY: passed=0 failed=0 skipped=1");
            return 1;
        }
        foreach (Type type in types
            .Where(t => (t.IsClass && (!t.IsAbstract || t.IsSealed)) || (t.IsValueType && !t.IsEnum))
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            MethodInfo[] factMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(m => HasFactAttribute(m))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();
            if (factMethods.Length == 0) continue;

            foreach (MethodInfo method in factMethods)
            {
                string testName = type.FullName + "." + method.Name;
                string? unsupportedReason = GetUnsupportedReason(type, method);
                if (unsupportedReason != null)
                {
                    skipped++;
                    unsupported++;
                    Console.WriteLine("SKIP/BLOCKED " + testName + " : " + unsupportedReason);
                    continue;
                }
                string? skip = GetSkipReason(method);
                if (skip != null)
                {
                    skipped++;
                    Console.WriteLine("SKIP " + testName + " : " + skip);
                    continue;
                }
                object? instance = null;
                Exception? failure = null;
                try
                {
                    instance = Activator.CreateInstance(type);
                    method.Invoke(instance, null);
                }
                catch (TargetInvocationException ex)
                {
                    failure = ex.InnerException ?? ex;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (instance is IDisposable disposable)
                    {
                        try { disposable.Dispose(); }
                        catch (Exception ex)
                        {
                            failure = failure == null ? ex : new AggregateException(failure, ex);
                        }
                    }
                }
                if (failure == null)
                {
                    passed++;
                    Console.WriteLine("PASS " + testName);
                }
                else
                {
                    failed++;
                    string detail = testName + " : " + failure.GetType().Name + ": " + failure.Message;
                    failures.Add(detail);
                    Console.WriteLine("FAIL " + detail);
                }
            }
        }

        Console.WriteLine("SUMMARY: passed=" + passed + " failed=" + failed + " skipped=" + skipped);
        if (failures.Count > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (string failure in failures) Console.WriteLine(" - " + failure);
            return 1;
        }
        if (passed == 0)
        {
            Console.WriteLine("SKIP/BLOCKED: no runnable Fact tests were executed.");
            return 1;
        }
        if (unsupported != 0) return 1;
        return 0;
    }

    private static bool HasUnsupportedExecutionAttributes(Assembly assembly, Type[] types)
    {
        if (assembly.GetCustomAttributesData().Any(IsUnsupportedExecutionAttribute)) return true;
        foreach (Type type in types)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                if (current.GetCustomAttributesData().Any(IsUnsupportedExecutionAttribute)) return true;
                foreach (MethodInfo method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttributesData().Any(IsUnsupportedExecutionAttribute)) return true;
                }
            }
        }
        return false;
    }

    private static bool IsUnsupportedExecutionAttribute(CustomAttributeData attribute)
    {
        for (Type? type = attribute.AttributeType; type != null; type = type.BaseType)
        {
            if (type.FullName == "Xunit.Sdk.BeforeAfterTestAttribute" ||
                type.FullName == "Xunit.TestCaseOrdererAttribute" ||
                type.FullName == "Xunit.TestCollectionOrdererAttribute" ||
                type.FullName == "Xunit.TestFrameworkAttribute" ||
                type.GetInterfaces().Any(i => i.FullName == "Xunit.Sdk.ITestFrameworkAttribute")) return true;
        }
        return false;
    }

    private static bool HasFactAttribute(MethodInfo method)
    {
        return method.GetCustomAttributes(false).Any(a =>
            IsFactAttribute(a.GetType()));
    }

    private static bool IsFactAttribute(Type? type)
    {
        while (type != null)
        {
            if (type.FullName == "Xunit.FactAttribute") return true;
            type = type.BaseType;
        }
        return false;
    }

    private static bool IsXunitFixture(Type type, string name)
    {
        return type.IsGenericType &&
            (type.GetGenericTypeDefinition().FullName ?? "").StartsWith(name, StringComparison.Ordinal);
    }

    private static string? GetUnsupportedReason(Type type, MethodInfo method)
    {
        object[] attributes = method.GetCustomAttributes(false)
            .Where(a => IsFactAttribute(a.GetType())).ToArray();
        if (attributes.Length != 1 || attributes[0].GetType().FullName != "Xunit.FactAttribute")
            return "only standard Fact attributes are supported";
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
            return "test class must be concrete, non-generic and have a public parameterless constructor";
        if (type.GetInterfaces().Any(i => i.FullName == "Xunit.IAsyncLifetime" ||
                i == typeof(IAsyncDisposable) || IsXunitFixture(i, "Xunit.IClassFixture")))
            return "test class fixtures and asynchronous lifetime are unsupported";
        if (!method.IsPublic || method.IsStatic || method.ContainsGenericParameters ||
            method.GetParameters().Length != 0 || method.ReturnType != typeof(void) ||
            method.IsDefined(typeof(AsyncStateMachineAttribute), false))
            return "only public, parameterless, synchronous void instance methods are supported";
        PropertyInfo? timeout = attributes[0].GetType().GetProperty("Timeout");
        if (timeout?.GetValue(attributes[0]) is int value && value != 0)
            return "Fact timeouts are unsupported";
        return null;
    }

    private static string? GetSkipReason(MethodInfo method)
    {
        foreach (object attribute in method.GetCustomAttributes(false))
        {
            if (attribute.GetType().FullName != "Xunit.FactAttribute") continue;
            PropertyInfo? skip = attribute.GetType().GetProperty("Skip");
            return skip?.GetValue(attribute, null) as string;
        }
        return null;
    }

    private sealed class TestAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        public TestAssemblyLoadContext(string mainAssemblyPath)
            : base("PatentMarker.FactRunner.TestAssembly", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
