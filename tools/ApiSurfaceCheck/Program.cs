using System.Reflection;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ApiSurfaceCheck <edition> <sdk-lib-directory>");
    return 2;
}

string edition = args[0];
string libDirectory = Path.GetFullPath(args[1]);
if (!Directory.Exists(libDirectory))
{
    Console.Error.WriteLine($"SDK directory not found: {libDirectory}");
    return 2;
}

string[] sdkPaths = Directory.GetFiles(libDirectory, "*.dll");
string? frameworkDirectory = edition switch
{
    "2010" => @"C:\Windows\Microsoft.NET\Framework\v2.0.50727",
    "2013" or "2015" => @"C:\Windows\Microsoft.NET\Framework\v4.0.30319",
    "2025" => Directory.GetDirectories(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref")
        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
        .Select(path => Path.Combine(path, "ref", "net8.0"))
        .FirstOrDefault(Directory.Exists),
    _ => null
};

string[] frameworkPaths = frameworkDirectory != null && Directory.Exists(frameworkDirectory)
    ? Directory.GetFiles(frameworkDirectory, "*.dll")
    : Array.Empty<string>();
string[] runtimePaths = frameworkPaths.Concat(new[]
{
    typeof(object).Assembly.Location,
    typeof(Console).Assembly.Location,
    typeof(List<>).Assembly.Location,
    typeof(Enumerable).Assembly.Location,
    typeof(System.Runtime.GCSettings).Assembly.Location
}).ToArray();

var resolverPaths = sdkPaths
    .Concat(runtimePaths)
    .Where(File.Exists)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var resolver = new PathAssemblyResolver(resolverPaths);
using var context = new MetadataLoadContext(resolver, "System.Private.CoreLib");

var assemblies = new List<Assembly>();
foreach (string path in sdkPaths)
{
    try
    {
        assemblies.Add(context.LoadFromAssemblyPath(path));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[INFO] skipped metadata file {Path.GetFileName(path)}: {ex.GetType().Name}");
    }
}

Type? FindType(string fullName)
{
    foreach (Assembly assembly in assemblies)
    {
        Type? type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        if (type != null) return type;
    }
    return null;
}

int failures = 0;
void RequireType(string fullName)
{
    if (FindType(fullName) == null)
    {
        Console.WriteLine($"[FAIL] missing type: {fullName}");
        failures++;
    }
    else
    {
        Console.WriteLine($"[OK]   type: {fullName}");
    }
}

void RequireProperty(string typeName, string propertyName)
{
    Type? type = FindType(typeName);
    if (type == null)
    {
        Console.WriteLine($"[FAIL] cannot inspect {typeName}.{propertyName}: type missing");
        failures++;
        return;
    }

    if (type.GetProperty(propertyName) == null)
    {
        Console.WriteLine($"[FAIL] missing property: {typeName}.{propertyName}");
        failures++;
    }
    else
    {
        Console.WriteLine($"[OK]   property: {typeName}.{propertyName}");
    }
}

void RequireMethod(string typeName, string methodName, int parameterCount)
{
    Type? type = FindType(typeName);
    if (type == null)
    {
        Console.WriteLine($"[FAIL] cannot inspect {typeName}.{methodName}: type missing");
        failures++;
        return;
    }

    bool found = type.GetMethods().Any(m => m.Name == methodName && m.GetParameters().Length == parameterCount);
    if (!found)
    {
        Console.WriteLine($"[FAIL] missing method: {typeName}.{methodName}/{parameterCount}");
        failures++;
    }
    else
    {
        Console.WriteLine($"[OK]   method: {typeName}.{methodName}/{parameterCount}");
    }
}

void RequireEnumValue(string typeName, string valueName)
{
    Type? type = FindType(typeName);
    if (type == null || type.GetField(valueName) == null)
    {
        Console.WriteLine($"[FAIL] missing enum value: {typeName}.{valueName}");
        failures++;
    }
    else
    {
        Console.WriteLine($"[OK]   enum: {typeName}.{valueName}");
    }
}

void RequireAbsent(string typeName)
{
    if (FindType(typeName) != null)
    {
        Console.WriteLine($"[INFO] type present but excluded by edition implementation: {typeName}");
    }
    else
    {
        Console.WriteLine($"[OK]   absent: {typeName}");
    }
}

Console.WriteLine($"=== SDK API surface: {edition} ===");
foreach (string path in sdkPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
{
    try
    {
        Assembly? assembly = assemblies.FirstOrDefault(a => string.Equals(a.Location, path, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"[INFO] {Path.GetFileName(path)}: {assembly?.GetName().Version?.ToString() ?? "metadata-unavailable"}");
    }
    catch { }
}

const string db = "Autodesk.AutoCAD.DatabaseServices";
const string geo = "Autodesk.AutoCAD.Geometry";

if (edition == "2010")
{
    RequireType($"{db}.Leader");
    RequireType($"{db}.MText");
    RequireProperty($"{db}.Leader", "Annotation");
    RequireProperty($"{db}.Leader", "Dimasz");
    RequireProperty($"{db}.Leader", "IsSplined");
    RequireProperty($"{db}.Leader", "HasArrowHead");
    RequireMethod($"{db}.Leader", "AppendVertex", 1);
    RequireAbsent($"{db}.MLeader");
}
else if (edition is "2013" or "2015" or "2025")
{
    RequireType($"{db}.Leader");
    RequireType($"{db}.MText");
    RequireProperty($"{db}.Leader", "Annotation");
    RequireProperty($"{db}.Leader", "DimensionStyle");
    RequireProperty($"{db}.Leader", "Dimasz");
    RequireProperty($"{db}.Leader", "IsSplined");
    RequireProperty($"{db}.Leader", "HasArrowHead");
    RequireProperty($"{db}.Leader", "NumVertices");
    RequireMethod($"{db}.Leader", "AppendVertex", 1);
    RequireMethod($"{db}.Leader", "VertexAt", 1);
    RequireType($"{geo}.Point3d");
}
else
{
    Console.Error.WriteLine($"Unknown edition: {edition}");
    return 2;
}

if (failures == 0)
{
    Console.WriteLine("[OK] API surface constraints passed.");
    return 0;
}

Console.WriteLine($"[FAIL] {failures} API surface constraint(s) failed.");
return 1;
