using System.Reflection;
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

        int passed = 0, failed = 0, skipped = 0;
        var failures = new List<string>();
        foreach (Type type in assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            MethodInfo[] factMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => HasFactAttribute(m))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();
            if (factMethods.Length == 0) continue;

            foreach (MethodInfo method in factMethods)
            {
                string testName = type.FullName + "." + method.Name;
                string? skip = GetSkipReason(method);
                if (!string.IsNullOrEmpty(skip))
                {
                    skipped++;
                    Console.WriteLine("SKIP " + testName + " : " + skip);
                    continue;
                }
                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(type);
                    method.Invoke(instance, null);
                    passed++;
                    Console.WriteLine("PASS " + testName);
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Exception failure = ex.InnerException ?? ex;
                    failures.Add(testName + " : " + failure.GetType().Name + ": " + failure.Message);
                    Console.WriteLine("FAIL " + testName + " : " + failure.GetType().Name + ": " + failure.Message);
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(testName + " : " + ex.GetType().Name + ": " + ex.Message);
                    Console.WriteLine("FAIL " + testName + " : " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (instance is IDisposable disposable)
                    {
                        try { disposable.Dispose(); }
                        catch (Exception ex)
                        {
                            failed++;
                            failures.Add(testName + " dispose: " + ex.GetType().Name + ": " + ex.Message);
                            Console.WriteLine("FAIL " + testName + " dispose: " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
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
        return 0;
    }

    private static bool HasFactAttribute(MethodInfo method)
    {
        return method.GetCustomAttributes(false).Any(a =>
            a.GetType().FullName == "Xunit.FactAttribute");
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
