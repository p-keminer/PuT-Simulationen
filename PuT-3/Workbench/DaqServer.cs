using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PuT3;

internal sealed record DaqActivity(string Operation, string Channel, int Status,
    int SampleCount = 0, double Minimum = 0, double Maximum = 0, double? ResistanceOhms = null);

internal sealed class DaqServer : IDisposable
{
    public const int Port = 48371;
    public const int MaxSamples = 16_384;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Bench bench;
    private readonly SemaphoreSlim connections = new(8);
    public DaqActivity? LastActivity { get; private set; }
    public DaqActivity? LastAnalogReading { get; private set; }
    public ElectricalIssue? LastElectricalIssue { get; private set; }

    public void ClearElectricalIssue() => LastElectricalIssue = null;

    public int BoundPort => ((IPEndPoint)listener.LocalEndpoint).Port;

    public DaqServer(Bench bench, int port = Port)
    {
        this.bench = bench;
        listener = new(IPAddress.Loopback, port);
    }

    public void Start()
    {
        listener.Start(8);
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                if (!connections.Wait(0)) { client.Dispose(); continue; }
                _ = Handle(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }

    private async Task Handle(TcpClient client)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using NetworkStream stream = client.GetStream();
            var bytes = new byte[4096];
            using var request = new MemoryStream();
            while (request.Length <= 420_000)
            {
                int count = await stream.ReadAsync(bytes, timeout.Token);
                if (count == 0) return;
                int lf = Array.IndexOf(bytes, (byte)'\n', 0, count);
                request.Write(bytes, 0, lf < 0 ? count : lf);
                if (lf >= 0) break;
            }
            string command = Encoding.ASCII.GetString(request.ToArray());
            double? captureUntil = null;
            double[] answer = request.Length <= 420_000 ? Execute(command, out captureUntil) : [-2];
            if (captureUntil is { } deadline)
            {
                double remaining = deadline - bench.Clock.Elapsed.TotalSeconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), timeout.Token);
            }
            byte[] body = Encoding.ASCII.GetBytes(string.Join('\t', answer.Select(x => x.ToString("G17", Invariant))));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(body.Length.ToString("D8", Invariant)), timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            client.Dispose();
            connections.Release();
        }
    }

    // Wire format: operation|physical channel|terminal|rate|samples|range|repeat|TAB-separated output samples
    public double[] Execute(string request) => Execute(request, out _);

    private double[] Execute(string request, out double? captureUntil)
    {
        var fields = request.TrimEnd('\r', '\n').Split('|');
        string operation = fields[0].ToUpperInvariant();
        lock (bench.Gate)
        {
            captureUntil = null;
            if (operation == "PING") return [0, 1];
            double[] result = ExecuteRequest(operation, fields, out captureUntil);
            if (fields.Length == 8)
            {
                string channel = fields[1].Trim();
                var activity = new DaqActivity(operation, channel, (int)result[0]);
                if (operation == "AI")
                {
                    if (result.Length > 1)
                    {
                        double minimum = result[1];
                        double maximum = minimum;
                        foreach (double sample in result.AsSpan(2))
                        {
                            minimum = Math.Min(minimum, sample);
                            maximum = Math.Max(maximum, sample);
                        }
                        activity = activity with { SampleCount = result.Length - 1, Minimum = minimum, Maximum = maximum };
                    }
                    LastAnalogReading = activity;
                }
                if (operation == "OHM" && result[0] == 0)
                    activity = activity with { ResistanceOhms = result[1] };
                LastActivity = activity;
            }
            return result;
        }
    }

    private double[] ExecuteRequest(string operation, string[] fields, out double? captureUntil)
    {
        captureUntil = null;
        if (!bench.Usb) return [-6];
        if (fields.Length != 8) return [-2];
        string physical = fields[1].Trim().ToLowerInvariant();
        if (!physical.StartsWith("mydaq1/", StringComparison.Ordinal)) return [-1];
        string channel = physical[7..];
        double now = bench.Clock.Elapsed.TotalSeconds;
        LastElectricalIssue = null;
        Circuit circuit = bench.Observe(now, checkPeaks: true);
        if (bench.ProtectionTrip is not null) return [-4];
        if (operation == "RESET")
        {
            bench.ResetOutputs();
            return [0];
        }
        if (operation == "OHM" && channel == "dmm")
        {
            if (MyDaqLimits.DmmVoltage(circuit) is { } issue) return Reject(-8, issue);
            var measurement = MyDaqLimits.ResolveResistance(bench.DmmResistance(now));
            return measurement is { } value ? [0, value] : [-3];
        }
        string terminal = fields[2].Trim().ToLowerInvariant().TrimStart('/');
        if (terminal.StartsWith("mydaq1/", StringComparison.Ordinal)) terminal = terminal[7..];
        if (operation == "FREQ" && channel == "ctr0")
            return terminal == "pfi1" ? Frequency(1, now, circuit) :
                Reject(-1, new("DIO1", "Bei der Frequenzmessung mit ctr0 liegt das Signal an PFI1 / DIO1 (GATE)."));
        int line = Numbered(channel, "port0/line", 7);
        if (operation is "DI" or "DO" && (channel == "port0" || line >= 0))
            return Digital(operation, line, fields[7], now, circuit);
        int analog = Numbered(channel, operation == "AI" ? "ai" : "ao", 1);
        if (operation is not ("AI" or "AO") || analog < 0) return [-1];
        if (!TryNumber(fields[3], out double rate) || rate is < 1 or > 200_000 ||
            !int.TryParse(fields[4], NumberStyles.None, Invariant, out int count) || count is < 1 or > MaxSamples ||
            !TryNumber(fields[5], out double range) || !MyDaqLimits.IsAnalogRange(range) ||
            fields[6] is not ("0" or "1")) return [-2];

        bool repeat = fields[6] == "1";
        if (operation == "AI")
            return count / rate > 1.5 ? [-2] : AnalogIn(analog, rate, count, range, repeat, now, out captureUntil);
        if (!ParseSamples(fields[7], out var samples) || samples.Length != count)
            return [-2];
        string pin = $"AO{analog}";
        if (samples.Any(x => Math.Abs(x) > range))
            return Reject(-2, new(pin, $"Der angeforderte Wert liegt außerhalb des eingestellten Bereichs von ±{range:g} V an {pin}."));
        if (bench.Analog[1 - analog] is { } other && other.Range != range)
            return Reject(-2, new(pin, "AO0 und AO1 brauchen gleichzeitig denselben Spannungsbereich."));

        for (int i = 0; i < samples.Length; i++)
            samples[i] = MyDaqLimits.QuantizeVoltage(samples[i], range);
        bench.SetAnalogOutput(analog, samples, rate, repeat, now, range, circuit);
        bench.Observe(now, checkPeaks: true);
        return bench.ProtectionTrip is not null ? [-4] : [0];
    }

    private double[] Reject(int code, ElectricalIssue issue)
    {
        LastElectricalIssue = issue;
        return [code];
    }

    private double[] AnalogIn(int analog, double rate, int count, double range, bool continuous, double now, out double? captureUntil)
    {
        captureUntil = null;
        // Use the sample grid across block boundaries. Restart after an idle client or a rate change.
        const double idleSeconds = 2;
        AnalogCapture capture = new(now, 0, rate, now);
        if (continuous && bench.Captures[analog] is { } running && running.Rate == rate && now - running.LastRequest <= idleSeconds)
            capture = running;
        bench.Captures[analog] = null;
        var values = new double[count + 1];
        for (int i = 0; i < count; i++)
        {
            var sample = bench.Read($"AI{analog}+", $"AI{analog}-", capture.TimeAt(i), analog: true);
            if (sample.Fault is { } issue) return Reject(-4, issue);
            if (!sample.Driven) return [-3];
            if (Math.Abs(sample.Level) > range)
            {
                values[0] = 1;
                LastElectricalIssue ??= new($"AI{analog}+", $"AI{analog} erreicht den Messbereich von ±{range:g} V. Die Messwerte werden begrenzt.");
            }
            values[i + 1] = MyDaqLimits.QuantizeVoltage(sample.Level, range);
        }
        captureUntil = capture.TimeAt(count);
        if (continuous)
            bench.Captures[analog] = capture with { NextIndex = capture.NextIndex + count, LastRequest = now };
        return values;
    }

    private double[] Digital(string operation, int line, string value, double now, Circuit circuit)
    {
        if (operation == "DO")
        {
            if (!TryNumber(value, out double raw) || raw != Math.Truncate(raw) || raw < 0 || raw > (line < 0 ? 255 : 1))
                return [-2];
            int bits = (int)raw;
            if (line >= 0) bench.Digital[line] = bits != 0;
            else for (int i = 0; i < 8; i++) bench.Digital[i] = (bits & (1 << i)) != 0;
            bench.Observe(now, checkPeaks: true);
            return bench.ProtectionTrip is not null ? [-4] : [0];
        }

        int result = 0;
        for (int i = 0; i < 8; i++)
        {
            if (line >= 0 && line != i) continue;
            string pin = $"DIO{i}";
            if (bench.AnalogOutputNeedsDigitalReference(circuit, pin))
                return Reject(-3, MyDaqLimits.MissingDigitalReference(pin));
            double volts = circuit.Value(pin) - circuit.Value("DGND");
            if (volts is > MyDaqLimits.DigitalLowMax and < MyDaqLimits.DigitalHighMin)
                return Reject(-5, new(pin, $"{pin} liegt bei {volts:0.###} V. Zwischen 0,8 und 2 V ist der Logikpegel undefiniert."));
            if (volts >= MyDaqLimits.DigitalHighMin) result |= 1 << i;
        }
        return [0, line >= 0 ? result >> line : result];
    }

    private double[] Frequency(int pfi, double now, Circuit circuit)
    {
        if (bench.Digital[pfi].HasValue) return [-7];
        string pin = $"DIO{pfi}";
        if (bench.AnalogOutputNeedsDigitalReference(circuit, pin))
            return Reject(-3, MyDaqLimits.MissingDigitalReference(pin));
        double highest = bench.Analog.Where(a => a != null)
            .Select(a => a!.Rate).DefaultIfEmpty(1000).Max();
        double rate = Math.Clamp(highest, 2000, 20_000);
        const int MaxCounterSamples = 10_000;
        double first = 0, last = 0;
        int edges = 0;
        bool armed = false;
        for (int i = 0; i < MaxCounterSamples; i++)
        {
            double offset = i / rate;
            var reading = bench.Read(pin, "DGND", now + offset);
            if (reading.Fault is { } issue) return Reject(-4, issue);
            if (reading.Level <= MyDaqLimits.DigitalLowMax) armed = true;
            if (reading.Level < MyDaqLimits.DigitalHighMin || !armed) continue;
            if (edges++ == 0) first = offset;
            last = offset;
            armed = false;
        }
        return edges >= 2 ? [0, (edges - 1) / (last - first)] : [-5];
    }

    private static int Numbered(string value, string prefix, int max) =>
        value.Length == prefix.Length + 1 && value.StartsWith(prefix, StringComparison.Ordinal) &&
        value[^1] >= '0' && value[^1] <= '0' + max ? value[^1] - '0' : -1;

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, Invariant, out value) && double.IsFinite(value);

    private static bool ParseSamples(string text, out double[] result)
    {
        var words = text.Trim().Split('\t');
        result = new double[words.Length];
        if (words.Length > MaxSamples) return false;
        for (int i = 0; i < result.Length; i++)
            if (!TryNumber(words[i], out result[i])) return false;
        return true;
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}
