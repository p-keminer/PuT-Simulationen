using System.Diagnostics;

namespace PuT3;

internal enum CableFault { Intact, Open, ScreenOpen, Intermittent, Short }
internal sealed record Socket(string Id, string Signal, string? Shield = null);
internal sealed record Cable(Socket A, Socket B, CableFault Fault = CableFault.Intact)
{
    public override string ToString() => $"{A.Id} ↔ {B.Id}";
}
internal readonly record struct Edge(string A, string B, double Ohms);
internal readonly record struct Source(string A, string B, double Volts, double Ohms, double MaxAmps);
internal sealed record Output(double[] Samples, double Rate, bool Repeat, double Start)
{
    public double At(double time)
    {
        double sample = Math.Max(0, (time - Start) * Rate);
        int index = Repeat ? (int)(sample % Samples.Length) : (int)Math.Min(sample, Samples.Length - 1);
        return Samples[index];
    }
}

internal sealed class Bench
{
    public static readonly IReadOnlyDictionary<string, Socket> Sockets = MakeSockets().ToDictionary(s => s.Id);

    private static IEnumerable<Socket> MakeSockets()
    {
        string[] x1 = ["+15", "-15", "AGND", "AO0", "AO1", "AGND", "AI0+", "AI0-", "AI1+", "AI1-",
            "DIO0", "DIO1", "DIO2", "DIO3", "DIO4", "DIO5", "DIO6", "DIO7", "DGND", "+5"];
        for (int i = 0; i < x1.Length; i++)
            yield return new($"myDAQ · {i + 1:00} {x1[i]}", x1[i]);
        yield return new("X2 AO0", "AO0", "AGND");
        yield return new("X3 AO1", "AO1", "AGND");
        yield return new("X4 AI0", "EXT0", "AI0-");
        yield return new("X5 AI1", "EXT1", "AI1-");
        string[][] blocks = [
            ["AO1", "AO0", "AGND", "AGND"],
            ["DIO0", "DIO1", "DIO3", "+15", "-15", "DIO4"],
            ["DIO5", "DGND", "+5", "DIO6", "DIO7", "DIO2"],
            ["AGND", "AI1-", "EXT1", "AGND", "EXT0", "AI0-"]];
        for (int block = 0; block < blocks.Length; block++)
            for (int pin = 0; pin < blocks[block].Length; pin++)
                yield return new($"X{block + 6}.{pin + 1} {blocks[block][pin]}", blocks[block][pin]);
        yield return new("DMM VΩ+", "DMM+");
        yield return new("DMM COM", "DMM-");
        yield return new("DMM A", "DMMA");
        yield return new("Widerstand A", "R-A");
        yield return new("Widerstand B", "R-B");
    }

    public readonly object Gate = new();
    public readonly List<Cable> Cables = [];
    // J4/J5: 0 offen, 1 externe Buchse, 2 Potentiometer.
    public int[] Jumpers { get; } = [0, 1, 1, 1, 1, 0, 1];
    public double[] Pots { get; } = [0.5, 0.5];
    public bool[] Buttons { get; } = new bool[4];
    public bool Usb { get; set; } = true;
    public bool Adapter { get; set; } = true;
    public double Resistance { get; set; } = 1000;
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public readonly Output?[] Analog = new Output?[2];
    public readonly bool?[] Digital = new bool?[8];
    public readonly Dictionary<string, double> NextSample = [];

    public void ResetOutputs()
    {
        Array.Clear(Analog);
        Array.Clear(Digital);
        NextSample.Clear();
    }

    public void Connect(string a, string b, CableFault fault = CableFault.Intact) =>
        Cables.Add(new(Sockets[a], Sockets[b], fault));

    public void Fixture()
    {
        Connect("X2 AO0", "X4 AI0");
        Connect("X6.1 AO1", "X7.2 DIO1");
        Connect("DMM VΩ+", "Widerstand A");
        Connect("DMM COM", "Widerstand B");
        Jumpers[4] = 2;
        Pots[1] = 0.75;
        Resistance = 3000;
    }

    public (double Level, bool Driven, bool Conflict) Read(string a, string b, double t, bool analog = false)
    {
        Circuit circuit = Snapshot(t);
        double value = circuit.Value(a) - circuit.Value(b);
        bool defined = !analog || (circuit.Driven(a) && circuit.Driven(b));
        bool fault = circuit.Overloaded || (analog && (Math.Abs(circuit.Value(a)) > 10.5 || Math.Abs(circuit.Value(b)) > 10.5));
        return (value, defined, fault);
    }

    public double Ohms(double t)
    {
        var powered = Snapshot(t);
        if (Math.Abs(powered.Value("DMM+") - powered.Value("DMM-")) > 0.05)
            return double.NaN;
        Circuit isolated = new(Wires(t), [new("R-A", "R-B", Resistance)], [], "DMM-", "DMM+");
        return isolated.Value("DMM+");
    }

    public Circuit Snapshot(double t)
    {
        var edges = new List<Edge> { new("R-A", "R-B", Resistance), new("DGND", "AGND", 0.1) };
        var sources = new List<Source>();
        if (Usb)
        {
            sources.Add(new("+15", "AGND", 15, 1, .032));
            sources.Add(new("-15", "AGND", -15, 1, .032));
            sources.Add(new("+5", "DGND", 5, 1, .100));
            for (int i = 0; i < Analog.Length; i++)
                if (Analog[i] is { } ao)
                    sources.Add(new($"AO{i}", "AGND", ao.At(t), 1, .002));
            for (int i = 0; i < Digital.Length; i++)
            {
                string pin = $"DIO{i}";
                if (Digital[i] is bool state)
                    sources.Add(new(pin, "DGND", state ? 3.3 : 0, 40, .004));
                else
                    edges.Add(new(pin, "DGND", 75_000));
                if (Adapter && Jumpers[6] != 0)
                    edges.Add(new(pin, "DGND", 4700));
            }
            if (Adapter && Jumpers[5] != 0)
                edges.Add(new("DIO5", "DGND", 10_000));
        }
        if (Adapter)
        {
            for (int i = 0; i < 2; i++)
            {
                string pot = $"POT{i}";
                edges.Add(new("+15", pot, 2490 + (1 - Pots[i]) * 10_000));
                edges.Add(new("-15", pot, 2490 + Pots[i] * 10_000));
            }
        }
        return new(Wires(t), edges, sources, "AGND");
    }

    private IEnumerable<(string A, string B)> Wires(double t)
    {
        if (Adapter)
        {
            if (Jumpers[0] != 0) yield return ("DGND", "AGND");
            for (int i = 0; i < 2; i++)
            {
                if (Jumpers[i + 1] != 0) yield return ($"AI{i}-", "AGND");
                if (Jumpers[i + 3] == 1) yield return ($"AI{i}+", $"EXT{i}");
                if (Jumpers[i + 3] == 2) yield return ($"AI{i}+", $"POT{i}");
            }
            for (int i = 0; i < Buttons.Length; i++)
                if (Buttons[i]) yield return ($"DIO{i}", "+5");
        }
        foreach (Cable cable in Cables)
        {
            if (!Adapter && (cable.A.Id.StartsWith('X') || cable.B.Id.StartsWith('X'))) continue;
            if (cable.Fault == CableFault.Open) continue;
            if (cable.Fault != CableFault.Intermittent || t % .4 < .2)
                yield return (cable.A.Signal, cable.B.Signal);
            if (cable.A.Shield is not { } a || cable.B.Shield is not { } b || cable.Fault == CableFault.ScreenOpen) continue;
            yield return (a, b);
            if (cable.Fault == CableFault.Short) yield return (cable.A.Signal, a);
        }
    }
}

internal sealed class Circuit
{
    private readonly Dictionary<string, string> roots = [];
    private readonly Dictionary<string, int> node = [];
    private readonly HashSet<string> driven;
    private readonly double[] volts;
    private readonly string reference;
    public bool Overloaded { get; }

    public Circuit(IEnumerable<(string A, string B)> wires, IReadOnlyList<Edge> edges,
        IReadOnlyList<Source> sources, string ground, string? inject = null)
    {
        foreach (var s in Bench.Sockets.Values)
        {
            roots.TryAdd(s.Signal, s.Signal);
            if (s.Shield != null) roots.TryAdd(s.Shield, s.Shield);
        }
        foreach (string p in new[] { "POT0", "POT1" }) roots.TryAdd(p, p);
        foreach (var (a, b) in wires) roots[Root(a)] = Root(b);
        reference = Root(ground);
        foreach (string root in roots.Values.Distinct())
            if (root != reference && Root(root) == root) node.Add(root, node.Count);

        driven = new() { reference };
        foreach (var s in sources) { driven.Add(Root(s.A)); driven.Add(Root(s.B)); }
        for (int i = 0; i < edges.Count; i++)
            foreach (var e in edges)
            {
                if (driven.Contains(Root(e.A))) driven.Add(Root(e.B));
                if (driven.Contains(Root(e.B))) driven.Add(Root(e.A));
            }

        var matrix = new double[node.Count, node.Count];
        var rhs = new double[node.Count];
        // A 1 TΩ shunt supplies a numeric reference for otherwise floating nets.
        for (int i = 0; i < node.Count; i++) matrix[i, i] = 1e-12;
        foreach (var e in edges) Stamp(e.A, e.B, 1 / e.Ohms, 0);
        foreach (var s in sources) Stamp(s.A, s.B, 1 / s.Ohms, s.Volts / s.Ohms);
        if (inject != null && Index(inject) is int at && at >= 0) rhs[at] += 1;
        volts = Solve(matrix, rhs);
        Overloaded = sources.Any(s => Math.Abs(s.Volts - (Value(s.A) - Value(s.B))) / s.Ohms > s.MaxAmps + 1e-7);

        void Stamp(string a, string b, double conductance, double current)
        {
            int i = Index(a);
            int j = Index(b);
            if (i == j) return;
            if (i >= 0) { matrix[i, i] += conductance; rhs[i] += current; }
            if (j >= 0) { matrix[j, j] += conductance; rhs[j] -= current; }
            if (i >= 0 && j >= 0) { matrix[i, j] -= conductance; matrix[j, i] -= conductance; }
        }
    }

    private string Root(string name)
    {
        while (roots[name] != name) name = roots[name];
        return name;
    }

    private int Index(string name) => node.GetValueOrDefault(Root(name), -1);
    public bool Driven(string name) => driven.Contains(Root(name));
    public double Value(string name) => Index(name) is int i && i >= 0 ? volts[i] : 0;

    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int k = 0; k < n; k++)
            for (int i = k + 1; i < n; i++)
            {
                if (a[i, k] == 0) continue;
                double scale = a[i, k] / a[k, k];
                for (int j = k + 1; j < n; j++) a[i, j] -= a[k, j] * scale;
                b[i] -= b[k] * scale;
            }
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = i + 1; j < n; j++) b[i] -= a[i, j] * b[j];
            b[i] /= a[i, i];
        }
        return b;
    }
}
