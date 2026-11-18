using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PuT3;

internal sealed class DaqServer : IDisposable
{
    public const int Port = 48371;
    public const int MaxSamples = 16_384;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Bench bench;
    private readonly SemaphoreSlim connections = new(8);
    public string? LastRequest { get; private set; }

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
            double started = bench.Clock.Elapsed.TotalSeconds;
            double[] answer = request.Length <= 420_000 ? Execute(command) : [-2];
            if (answer[0] >= 0 && command.StartsWith("AI|", StringComparison.OrdinalIgnoreCase))
            {
                string[] fields = command.Split('|');
                double duration = (answer.Length - 1) / double.Parse(fields[3], Invariant);
                double remaining = duration - (bench.Clock.Elapsed.TotalSeconds - started);
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
    public double[] Execute(string request)
    {
        var fields = request.TrimEnd('\r', '\n').Split('|');
        string operation = fields[0].ToUpperInvariant();
        lock (bench.Gate)
        {
            if (operation == "PING") return [0, 1];
            if (!bench.Usb) return [-6];
            if (fields.Length != 8) return [-2];
            string physical = fields[1].Trim().ToLowerInvariant();
            if (!physical.StartsWith("mydaq1/", StringComparison.Ordinal)) return [-1];
            string channel = physical[7..];
            double now = bench.Clock.Elapsed.TotalSeconds;
            LastRequest = $"{operation} · {fields[1].Trim()}";
            if (operation == "RESET")
            {
                bench.ResetOutputs();
                return [0];
            }
            if (operation == "OHM" && channel == "dmm")
            {
                double resistance = bench.Ohms(now);
                return double.IsNaN(resistance) ? [-8] : resistance >= 100_000_000 ? [-3] : [0, resistance];
            }
            string terminal = fields[2].Trim().ToLowerInvariant().TrimStart('/');
            if (terminal.StartsWith("mydaq1/", StringComparison.Ordinal)) terminal = terminal[7..];
            int pfi = Numbered(terminal, "pfi", 3);
            if (operation == "FREQ" && channel == "ctr0")
                return pfi < 0 ? [-1] : Frequency(pfi, now);
            int line = Numbered(channel, "port0/line", 7);
            if (operation is "DI" or "DO" && (channel == "port0" || line >= 0))
                return Digital(operation, line, fields[7], now);
            int analog = Numbered(channel, operation == "AI" ? "ai" : "ao", 1);
            if (operation is not ("AI" or "AO") || analog < 0) return [-1];
            if (!TryNumber(fields[3], out double rate) || rate is < 1 or > 200_000 ||
                !int.TryParse(fields[4], NumberStyles.None, Invariant, out int count) || count is < 1 or > MaxSamples ||
                !TryNumber(fields[5], out double range) || (range != 2 && range != 10) ||
                fields[6] is not ("0" or "1")) return [-2];

            bool repeat = fields[6] == "1";
            if (operation == "AI")
                return count / rate > 1.5 ? [-2] : AnalogIn(analog, rate, count, range, repeat, now);
            if (!ParseSamples(fields[7], out var samples) || samples.Length != count || samples.Any(x => Math.Abs(x) > range))
                return [-2];

            bench.Analog[analog] = new(samples, rate, repeat, now);
            return bench.Snapshot(now).Overloaded ? [-4] : [0];
        }
    }

    private double[] AnalogIn(int analog, double rate, int count, double range, bool continuous, double now)
    {
        string cursor = $"ai{analog}";
        double time = now;
        if (continuous && bench.NextSample.TryGetValue(cursor, out double next) && Math.Abs(next - now) <= 2)
            time = next;
        var values = new double[count + 1];
        for (int i = 0; i < count; i++)
        {
            var sample = bench.Read($"AI{analog}+", $"AI{analog}-", time + i / rate, analog: true);
            if (sample.Conflict) return [-4];
            if (!sample.Driven) return [-3];
            if (Math.Abs(sample.Level) > range) values[0] = 1;
            double quantized = Math.Round(sample.Level / range * 32768);
            values[i + 1] = Math.Clamp(quantized, -32768, 32767) * range / 32768;
        }
        if (continuous)
            bench.NextSample[cursor] = time + count / rate;
        else
            bench.NextSample.Remove(cursor);
        return values;
    }

    private double[] Digital(string operation, int line, string value, double now)
    {
        if (operation == "DO")
        {
            if (!TryNumber(value, out double raw) || raw != Math.Truncate(raw) || raw < 0 || raw > (line < 0 ? 255 : 1))
                return [-2];
            int bits = (int)raw;
            if (line >= 0) bench.Digital[line] = bits != 0;
            else for (int i = 0; i < 8; i++) bench.Digital[i] = (bits & (1 << i)) != 0;
            return bench.Snapshot(now).Overloaded ? [-4] : [0];
        }

        int result = 0;
        Circuit circuit = bench.Snapshot(now);
        if (circuit.Overloaded) return [-4];
        for (int i = 0; i < 8; i++)
        {
            if (line >= 0 && line != i) continue;
            double volts = circuit.Value($"DIO{i}") - circuit.Value("DGND");
            if (volts < -0.3 || volts > 5.25) return [-4];
            if (volts is > 0.8 and < 2) return [-5];
            if (volts >= 2) result |= 1 << i;
        }
        return [0, line >= 0 ? result >> line : result];
    }

    private double[] Frequency(int pfi, double now)
    {
        if (bench.Digital[pfi].HasValue) return [-7];
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
            var reading = bench.Read($"DIO{pfi}", "DGND", now + offset);
            if (reading.Conflict || reading.Level is < -0.3 or > 5.25) return [-4];
            if (reading.Level <= 0.8) armed = true;
            if (reading.Level < 2 || !armed) continue;
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
