using System.Globalization;
using GalaxyCoop.Companion;

const string Usage = """
GalaxyCoop companion

  attach   [--pid N]                         print live state from the game mailbox
  loopback [--pid N] [--delay-ms 400] [--offset X,Y,Z]
                                             echo your own Mario back as a ghost
  connect  --server HOST[:PORT] [--name NAME] [--password X] [--pid N]
           [--character mario|luigi] [--no-follow-host]
           [--sync-enemies] [--sync-switches] [--share-stars]
                                             join a GalaxyCoop server
""";

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return 0;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var opts = Options.Parse(args.Skip(1).ToArray());
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (args[0])
    {
        case "attach":
            await AttachMode.RunAsync(opts, cts.Token);
            break;
        case "probe":
            ProbeMode.Run(opts);
            break;
        case "loopback":
            await LoopbackMode.RunAsync(opts, cts.Token);
            break;
        case "connect":
            await ConnectMode.RunAsync(opts, cts.Token);
            break;
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'\n{Usage}");
            return 2;
    }
}
catch (OperationCanceledException) { }
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
return 0;

namespace GalaxyCoop.Companion
{
    public sealed class Options
    {
        readonly Dictionary<string, string?> _values = new();

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new ArgumentException($"unexpected argument '{args[i]}'");
                var key = args[i][2..];
                string? value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
                o._values[key] = value;
            }
            return o;
        }

        public bool Has(string key) => _values.ContainsKey(key);
        public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public int? GetInt(string key) => Get(key) is string s ? int.Parse(s, CultureInfo.InvariantCulture) : null;

        public System.Numerics.Vector3 GetVec(string key, System.Numerics.Vector3 fallback)
        {
            if (Get(key) is not string s) return fallback;
            var p = s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (p.Length != 3) throw new ArgumentException($"--{key} expects X,Y,Z");
            return new(p[0], p[1], p[2]);
        }
    }
}
