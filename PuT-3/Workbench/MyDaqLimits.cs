namespace PuT3;

internal sealed record ElectricalIssue(string? Signal, string Message);

// NI myDAQ Device Specifications (373061G), pp. 3–6 and 10–11.
// The DIO voltage window follows the miniAdap-1 manual's wiring guidance.
internal static class MyDaqLimits
{
    public const double AnalogWorkingVoltage = 10.5;
    public const double AnalogProtectionVoltage = 16;
    public const double DigitalInputMin = 0;
    public const double DigitalInputMax = 5;
    public const double DigitalLowMax = 0.8;
    public const double DigitalHighMin = 2;
    public const double DigitalHighOutput = 3.3;
    public const double AnalogOutputAmps = 0.002;
    public const double AnalogOutputOhms = 1;
    public const double DigitalOutputAmps = 0.004;
    public const double Rail15Amps = 0.032;
    public const double Rail5Amps = 0.100;
    public const double TypicalOutputWatts = 0.500;
    private const double VoltageTolerance = 0.00001;
    private const double PowerTolerance = 0.000001;
    private static readonly string[] AnalogPins = ["AI0+", "AI0-", "AI1+", "AI1-", "AO0", "AO1"];
    // Table 7, NI myDAQ Device Specifications. Full scale and resolution in ohms.
    private static readonly (double Maximum, double Resolution)[] ResistanceRanges =
        [(200, 0.1), (2_000, 1), (20_000, 10), (200_000, 100), (2_000_000, 1_000), (20_000_000, 10_000)];

    public static bool IsAnalogRange(double range) => range is 2 or 10;

    public static double? ResolveResistance(double ohms)
    {
        foreach (var (maximum, resolution) in ResistanceRanges)
            if (ohms < maximum)
                return Math.Min(maximum - resolution, Math.Round(ohms / resolution) * resolution);
        return null;
    }

    public static double QuantizeVoltage(double voltage, double range)
    {
        const int halfCodes = 1 << 15;
        double code = Math.Round(voltage / range * halfCodes);
        return Math.Clamp(code, -halfCodes, halfCodes - 1) * range / halfCodes;
    }

    public static ElectricalIssue MissingDigitalReference(string pin) =>
        new(pin, $"{pin} bekommt ein Analogsignal ohne gemeinsamen Massebezug. Verbinde AGND mit DGND über J1 oder eine Masseleitung.");

    public static ElectricalIssue? Trip(Circuit circuit)
    {
        for (int i = 0; i < 8; i++)
        {
            string pin = $"DIO{i}";
            if (circuit.SharesNode(pin, "+15") || circuit.SharesNode(pin, "-15"))
                return new(pin, $"{pin} ist mit einer ±15-V-Versorgungsschiene verbunden. Erlaubter DIO-Bereich: 0 bis 5 V.");
        }
        for (int i = 0; i < 8; i++)
        {
            string pin = $"DIO{i}";
            double voltage = circuit.Value(pin) - circuit.Value("DGND");
            if (voltage < DigitalInputMin - VoltageTolerance || voltage > DigitalInputMax + VoltageTolerance)
                return new(pin, $"{pin} liegt im Modell bei ca. {voltage:0.##} V gegen DGND. Für DIO sind 0 bis 5 V vorgesehen.");
        }
        foreach (string pin in AnalogPins)
        {
            double voltage = circuit.Value(pin) - circuit.Value("AGND");
            if (Math.Abs(voltage) > AnalogProtectionVoltage + VoltageTolerance)
                return new(pin, $"{pin} liegt im Modell bei ca. {voltage:0.##} V gegen AGND. Der Schutzbereich endet bei ±16 V.");
        }
        if (circuit.OutputFault is { } issue) return issue;
        if (circuit.OutputWatts > TypicalOutputWatts + PowerTolerance)
            return new(null, $"Die Ausgänge benötigen im Modell ca. {circuit.OutputWatts * 1000:0.#} mW. Das Modellbudget beträgt 500 mW.");
        return null;
    }

    public static ElectricalIssue? WorkingVoltage(Circuit circuit, string pin)
    {
        double voltage = circuit.Value(pin) - circuit.Value("AGND");
        return Math.Abs(voltage) > AnalogWorkingVoltage + VoltageTolerance
            ? new(pin, $"{pin} liegt im Modell bei ca. {voltage:0.##} V gegen AGND. Der Arbeitsbereich endet bei ±10,5 V je Klemme.")
            : null;
    }

    public static ElectricalIssue? DmmVoltage(Circuit circuit)
    {
        double voltage = circuit.Value("DMM+") - circuit.Value("DMM-");
        return Math.Abs(voltage) > 0.05
            ? new("DMM+", $"Ca. {voltage:0.##} V zwischen VΩ+ und COM. Widerstand nur am spannungsfreien Messkreis messen.")
            : null;
    }
}
