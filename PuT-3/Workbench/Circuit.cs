using System.Diagnostics;

namespace PuT3;

internal enum CableFault { Intact, Open, ScreenOpen, Intermittent, Short }
internal sealed record Socket(string Id, string Signal, string? Shield = null);
internal sealed record Cable(Socket A, Socket B, CableFault Fault = CableFault.Intact)
{
    public override string ToString() => $"{A.Id} ↔ {B.Id}";
}
internal readonly record struct Edge(string A, string B, double Ohms);
internal readonly record struct Source(string A, string B, double Volts, double Ohms, double MaxAmps, double SupplyVolts);
internal readonly record struct AnalogCapture(double Origin, long NextIndex, double Rate, double LastRequest)
{
    public double TimeAt(int offset) => Origin + (NextIndex + offset) / Rate;
}

internal sealed record Output(double[] Samples, double Rate, bool Repeat, double Start, double Range)
{
    public double Minimum { get; } = Samples.Min();
    public double Maximum { get; } = Samples.Max();

    public (double Minimum, double Maximum) PeaksAfter(double time)
    {
        double elapsedSamples = (time - Start) * Rate;
        if (Repeat || elapsedSamples < 1) return (Minimum, Maximum);
        int first = (int)Math.Min(elapsedSamples, Samples.Length - 1);
        double min = Samples[first], max = min;
        for (int i = first + 1; i < Samples.Length; i++)
        {
            min = Math.Min(min, Samples[i]);
            max = Math.Max(max, Samples[i]);
        }
        return (min, max);
    }

    public double At(double time)
    {
        double indexAtTime = Math.Floor(Math.Max(0, (time - Start) * Rate) + 0.0000001);
        int index = Repeat ? (int)(indexAtTime % Samples.Length) : (int)Math.Min(indexAtTime, Samples.Length - 1);
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
    public int[] Jumpers { get; } = [0, 1, 1, 1, 1, 0, 0];
    public double[] Pots { get; } = [0.5, 0.5];
    public bool[] Buttons { get; } = new bool[4];
    public bool Usb { get; set; } = true;
    public bool Adapter { get; set; } = true;
    public double Resistance { get; set; } = 1000;
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public readonly Output?[] Analog = new Output?[2];
    public readonly bool?[] Digital = new bool?[8];
    public readonly AnalogCapture?[] Captures = new AnalogCapture?[2];
    public ElectricalIssue? ProtectionTrip { get; private set; }
    public bool Powered => Usb && ProtectionTrip is null;

    public void ResetOutputs()
    {
        Array.Clear(Analog);
        Array.Clear(Digital);
        Array.Clear(Captures);
    }

    public void SetAnalogOutput(int channel, double[] samples, double rate, bool repeat, double now, double range, Circuit circuit)
    {
        if (repeat && Analog[channel] is { Repeat: true } current && current.Rate == rate && current.Range == range &&
            samples.AsSpan().SequenceEqual(current.Samples)) return;

        Analog[channel] = new(samples, rate, repeat, now, range);
        string pin = $"AO{channel}";
        for (int input = 0; input < Captures.Length; input++)
            if (circuit.SharesNode(pin, $"AI{input}+") || circuit.SharesNode(pin, $"AI{input}-"))
                Captures[input] = null;
    }

    public bool AnalogOutputNeedsDigitalReference(Circuit circuit, string pin)
    {
        if (circuit.SharesNode("AGND", "DGND")) return false;
        for (int i = 0; i < Analog.Length; i++)
            if (Analog[i] is not null && circuit.SharesNode($"AO{i}", pin)) return true;
        return false;
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
        Jumpers[0] = 1; // Provide a common return path for AO to PFI.
        Jumpers[6] = 0; // Disconnect the LED load from the analog source.
    }

    public bool ResetProtection(double t)
    {
        ProtectionTrip = null;
        ResetOutputs();
        Observe(t);
        return Powered;
    }

    public Circuit Observe(double t, bool checkPeaks = false)
    {
        Circuit circuit = Snapshot(t);
        if (!Powered) return circuit;
        var issue = MyDaqLimits.Trip(circuit);
        if (issue is null && checkPeaks)
        {
            // Probe both future extrema when the other AO is constant. Two varying outputs are checked at sample time.
            var first = Analog[0]?.PeaksAfter(t) ?? (Minimum: 0, Maximum: 0);
            var second = Analog[1]?.PeaksAfter(t) ?? (Minimum: 0, Maximum: 0);
            bool firstVaries = first.Minimum != first.Maximum;
            bool secondVaries = second.Minimum != second.Maximum;
            if (firstVaries != secondVaries)
            {
                int varying = firstVaries ? 0 : 1;
                var peaks = firstVaries ? first : second;
                issue = MyDaqLimits.Trip(Snapshot(t, varying, peaks.Minimum)) ??
                    MyDaqLimits.Trip(Snapshot(t, varying, peaks.Maximum));
            }
        }
        if (issue is not null)
        {
            ProtectionTrip = issue;
            ResetOutputs();
        }
        return circuit;
    }

    public (double Level, bool Driven, ElectricalIssue? Fault) Read(string a, string b, double t, bool analog = false)
    {
        Circuit circuit = Observe(t);
        double value = circuit.Value(a) - circuit.Value(b);
        bool defined = !analog || (circuit.Driven(a) && circuit.Driven(b));
        var issue = ProtectionTrip ?? (analog ? MyDaqLimits.WorkingVoltage(circuit, a) ??
            MyDaqLimits.WorkingVoltage(circuit, b) : null);
        return (value, defined, issue);
    }

    public double DmmResistance(double t)
    {
        // Solve V/I with unit current as normalization. NI does not specify the physical test current.
        Circuit isolated = new(Wires(t), [new("R-A", "R-B", Resistance)], [], "DMM-", "DMM+");
        return isolated.Value("DMM+");
    }

    private Circuit Snapshot(double t, int peakChannel = -1, double peakValue = 0)
    {
        var edges = new List<Edge> { new("R-A", "R-B", Resistance) };
        var sources = new List<Source>();
        if (Powered)
        {
            sources.Add(new("+15", "AGND", 15, 1, MyDaqLimits.Rail15Amps, 15));
            sources.Add(new("-15", "AGND", -15, 1, MyDaqLimits.Rail15Amps, 15));
            sources.Add(new("+5", "DGND", 5, 1, MyDaqLimits.Rail5Amps, 5));
            for (int i = 0; i < Analog.Length; i++)
                if (Analog[i] is { } ao)
                    sources.Add(new($"AO{i}", "AGND", i == peakChannel ? peakValue : ao.At(t), MyDaqLimits.AnalogOutputOhms, MyDaqLimits.AnalogOutputAmps, 15));
            for (int i = 0; i < Digital.Length; i++)
            {
                string pin = $"DIO{i}";
                if (Digital[i] is bool state)
                    sources.Add(new(pin, "DGND", state ? MyDaqLimits.DigitalHighOutput : 0, 40,
                        MyDaqLimits.DigitalOutputAmps, MyDaqLimits.DigitalHighOutput));
                else
                    edges.Add(new(pin, "DGND", 75_000));
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
        return new(Wires(t), edges, sources, "AGND", loadLeds: Powered && Adapter && Jumpers[6] != 0);
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
    public ElectricalIssue? OutputFault { get; }
    public double OutputWatts { get; }

    public Circuit(IEnumerable<(string A, string B)> wires, IReadOnlyList<Edge> edges,
        IReadOnlyList<Source> sources, string ground, string? inject = null, bool loadLeds = false)
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
        int conductingLeds = 0;
        volts = loadLeds ? SolveWithLeds() : Solve(matrix, rhs);
        double highestOverload = 1;
        foreach (var source in sources)
        {
            double current = Math.Abs(source.Volts - (Value(source.A) - Value(source.B))) / source.Ohms;
            OutputWatts += source.SupplyVolts * current;
            double ratio = current / (source.MaxAmps + 1e-7);
            if (ratio <= highestOverload) continue;
            highestOverload = ratio;
            string message = $"{source.A} müsste im Modell ca. {current * 1000:0.##} mA liefern oder aufnehmen. Grenze: {source.MaxAmps * 1000:0.##} mA.";
            if (source.A is "AO0" or "AO1" && HasLedLoad(source.A))
                message += " J7 ist geschlossen. Die Adapter-LEDs belasten diese Leitung. Für die Frequenzmessung J7 öffnen und den Schutz zurücksetzen.";
            OutputFault = new(source.A, message);
        }

        bool HasLedLoad(string output)
        {
            for (int line = 0; line < 8; line++)
                if ((conductingLeds & (1 << line)) != 0 && SharesNode(output, $"DIO{line}")) return true;
            return false;
        }

        double[] SolveWithLeds()
        {
            // RN1 is 680 Ω per LED. A fixed forward voltage approximates the red LEDs on the miniAdap-1.
            const double forwardVolts = 1.8;
            const double conductance = 1.0 / 680;
            var baseMatrix = (double[,])matrix.Clone();
            var baseRhs = (double[])rhs.Clone();
            var levels = Solve(matrix, rhs);
            int dgnd = Index("DGND");
            for (int step = 0; step < 8; step++)
            {
                double groundLevel = dgnd >= 0 ? levels[dgnd] : 0;
                int conducting = 0;
                for (int line = 0; line < 8; line++)
                {
                    int pin = Index($"DIO{line}");
                    if ((pin >= 0 ? levels[pin] : 0) - groundLevel > forwardVolts + 1e-9)
                        conducting |= 1 << line;
                }
                if (conducting == conductingLeds) break;

                conductingLeds = conducting;
                matrix = (double[,])baseMatrix.Clone();
                rhs = (double[])baseRhs.Clone();
                for (int line = 0; line < 8; line++)
                    if ((conductingLeds & (1 << line)) != 0)
                        Stamp($"DIO{line}", "DGND", conductance, forwardVolts * conductance);
                levels = Solve(matrix, rhs);
            }
            return levels;
        }

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
    public bool SharesNode(string first, string second) => Root(first) == Root(second);
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
