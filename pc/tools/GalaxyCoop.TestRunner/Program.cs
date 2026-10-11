// Loader for machines where Smart App Control blocks freshly built, unsigned
// DLLs from being mapped (its verdict can change on every rebuild). Assemblies
// are loaded from byte arrays, which the image-load check does not cover.
//
//   exec <App.dll> [args...]      run a console app's entry point
//   <Tests.dll> [name filter]     run xUnit [Fact]/[Theory] tests
//
// The test mode understands [Fact], [Theory] with [InlineData]/[MemberData],
// and Skip; it is not a replacement for `dotnet test` where that works.
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: GalaxyCoop.TestRunner exec <App.dll> [args...] | <Tests.dll> [name filter]");
    return 2;
}

bool exec = args[0] == "exec";
if (exec && args.Length < 2)
{
    Console.Error.WriteLine("usage: GalaxyCoop.TestRunner exec <App.dll> [args...]");
    return 2;
}
var testPath = Path.GetFullPath(exec ? args[1] : args[0]);
var dir = Path.GetDirectoryName(testPath)!;

AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    var candidate = Path.Combine(dir, name.Name + ".dll");
    return File.Exists(candidate) ? LoadBytes(ctx, candidate) : null;
};

var asm = LoadBytes(AssemblyLoadContext.Default, testPath);
if (exec)
{
    var entry = asm.EntryPoint ?? throw new InvalidOperationException($"{testPath} has no entry point");
    var appArgs = args.Skip(2).ToArray();
    object? ret;
    try
    {
        ret = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { appArgs });
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
    if (ret is Task<int> ti) return ti.GetAwaiter().GetResult();
    if (ret is Task t) t.GetAwaiter().GetResult();
    return ret is int code ? code : 0;
}

var filter = args.Length > 1 ? args[1] : null;
int passed = 0, failed = 0, skipped = 0;
var clock = Stopwatch.StartNew();

foreach (var type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsPublic).OrderBy(t => t.FullName))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).OrderBy(m => m.MetadataToken))
    {
        var attrs = method.GetCustomAttributesData();
        var fact = attrs.FirstOrDefault(a => a.AttributeType.Name is "FactAttribute" or "TheoryAttribute");
        if (fact is null) continue;
        var name = $"{type.Name}.{method.Name}";
        if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

        if (fact.NamedArguments.FirstOrDefault(n => n.MemberName == "Skip").TypedValue.Value is string reason)
        {
            skipped++;
            Console.WriteLine($"  SKIP {name}: {reason}");
            continue;
        }

        foreach (var row in Rows(type, attrs))
        {
            var label = row.Length == 0 ? name : $"{name}({string.Join(", ", row.Select(Describe))})";
            try
            {
                var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                var result = method.Invoke(instance, row);
                if (result is Task task) task.GetAwaiter().GetResult();
                (instance as IDisposable)?.Dispose();
                passed++;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
                failed++;
                Console.WriteLine($"  FAIL {label}\n       {inner.GetType().Name}: {inner.Message.Replace("\n", "\n       ")}");
                var frame = inner.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains(" in "));
                if (frame is not null) Console.WriteLine($"       {frame.Trim()}");
            }
        }
    }
}

Console.WriteLine($"{(failed == 0 ? "Passed" : "FAILED")}: {passed} passed, {failed} failed, {skipped} skipped in {clock.ElapsedMilliseconds} ms");
return failed == 0 ? 0 : 1;

static Assembly LoadBytes(AssemblyLoadContext ctx, string path)
{
    using var stream = new MemoryStream(File.ReadAllBytes(path));
    return ctx.LoadFromStream(stream);
}

static IEnumerable<object?[]> Rows(Type type, IList<CustomAttributeData> attrs)
{
    bool any = false;
    foreach (var a in attrs)
    {
        if (a.AttributeType.Name == "InlineDataAttribute")
        {
            any = true;
            var arg = a.ConstructorArguments.Single();
            yield return arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> list
                ? list.Select(x => x.Value).ToArray()
                : new[] { arg.Value };
        }
        else if (a.AttributeType.Name == "MemberDataAttribute")
        {
            any = true;
            var member = (string)a.ConstructorArguments[0].Value!;
            var source = type.GetMember(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Single();
            var data = source switch
            {
                MethodInfo m => m.Invoke(null, null),
                PropertyInfo p => p.GetValue(null),
                FieldInfo f => f.GetValue(null),
                _ => null,
            };
            foreach (var row in (IEnumerable)data!) yield return (object?[])row;
        }
    }
    if (!any) yield return Array.Empty<object?>();
}

static string Describe(object? v) => v switch
{
    null => "null",
    string s => $"\"{s}\"",
    _ => v.ToString() ?? "?",
};
