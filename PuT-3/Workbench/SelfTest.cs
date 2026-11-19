using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PuT3;

internal static class SelfTest
{
    public static string Run(Bench bench, DaqServer server, string? previewPath = null)
    {
        var results = new List<string>();
        double[] Request(string operation, string channel, string? values = null, string pfi = "", int count = 1, int range = 10,
            int rate = 1000, bool repeat = false) =>
            server.Execute($"{operation}|myDAQ1/{channel}|{pfi}|{rate}|{count}|{range}|{(repeat ? 1 : 0)}|{values}");
        void Check(string name, bool ok)
        {
            if (!ok) throw new InvalidOperationException($"FAIL {name}\n" + string.Join('\n', results));
            results.Add("PASS " + name);
        }
        static bool Near(double value, double target, double tolerance = 0.003) => Math.Abs(value - target) <= tolerance;

        Check("J1 verbindet AGND und DGND beim Start", bench.Observe(0).SharesNode("AGND", "DGND"));
        Check("DMM leer", Request("OHM", "dmm")[0] == -3);
        bench.Fixture();
        Check("5.2 Widerstand über DMM-Buchsen", Near(Request("OHM", "dmm")[1], 3000));
        bench.Resistance = 123.46;
        Check("5.2 DMM niedriger Bereich mit 0,1 Ω Auflösung", Request("OHM", "dmm")[1] == 123.5);
        bench.Resistance = 3_014;
        Check("5.2 DMM 20-kΩ-Bereich mit 10 Ω Auflösung und Auftragsanzeige",
            Request("OHM", "dmm")[1] == 3_010 && server.LastActivity?.ResistanceOhms == 3_010);
        bench.Resistance = 15_000;
        Check("5.2 Zusatzwiderstand bleibt numerisch", Near(Request("OHM", "dmm")[1], 15_000));
        bench.Connect("DMM VΩ+", "DMM COM");
        Check("5.2 direkter Kurzschluss der Messspitzen", Request("OHM", "dmm")[1] == 0);
        bench.Cables.RemoveAt(bench.Cables.Count - 1);
        bench.Cables[3] = new(Bench.Sockets["DMM A"], Bench.Sockets["Widerstand B"]);
        Check("5.2 falsche DMM-Buchse", Request("OHM", "dmm")[0] == -3);

        Check("5.1 AO0 setzen", Request("AO", "ao0", "3.25")[0] == 0);
        Check("5.1 BNC-Durchgang", Near(Request("AI", "ai0")[1], 3.25));
        Check("5.4 Messbereich ±2 V begrenzt", Request("AI", "ai0", range: 2)[0] == 1);
        bench.Jumpers[3] = 0;
        Check("5.1 J4 offen", Request("AI", "ai0")[0] == -3);
        bench.Jumpers[3] = 1;
        bench.Cables[0] = bench.Cables[0] with { Fault = CableFault.Open };
        Check("5.1 Kabelbruch", Request("AI", "ai0")[0] == -3);
        bench.Cables[0] = bench.Cables[0] with { Fault = CableFault.Short };
        Check("5.1 BNC-Kurzschluss", Request("AI", "ai0")[0] == -4);
        bench.Cables[0] = bench.Cables[0] with { Fault = CableFault.ScreenOpen };
        Check("BNC-Kurzschluss sperrt weitere Aufträge bis zum Rücksetzen",
            bench.ProtectionTrip?.Signal == "AO0" && Request("AI", "ai0")[0] == -4);
        Check("Nach Korrektur lässt sich der Schutz zurücksetzen",
            bench.ResetProtection(bench.Clock.Elapsed.TotalSeconds) && Request("AO", "ao0", "3.25")[0] == 0);
        bench.Jumpers[1] = 0;
        Check("5.1 Schirm fehlt und J2 offen", Request("AI", "ai0")[0] == -3);
        bench.Jumpers[1] = 1;
        Check("5.1 Masse über J2 trotz offenem Schirm", Near(Request("AI", "ai0")[1], 3.25));
        bench.Cables[0] = bench.Cables[0] with { Fault = CableFault.Intermittent };
        Check("5.1 Wackelkontakt wechselt Leitfähigkeit", bench.Read("AI0+", "AI0-", 1.02, true).Driven == false &&
            bench.Read("AI0+", "AI0-", 1.25, true).Driven);
        bench.Cables[0] = bench.Cables[0] with { Fault = CableFault.Intact };

        string square = string.Join('\t', Enumerable.Repeat("0", 10).Concat(Enumerable.Repeat("5", 10)));
        Check("5.3 AO1 Rechteck setzen", Request("AO", "ao1", square, count: 20, repeat: true)[0] == 0);
        Check("5.3 Frequenz über gewählte PFI1", Near(Request("FREQ", "ctr0", pfi: "PFI1")[1], 50, .2));
        Check("5.3 SOURCE, AUX und OUT sind keine Eingänge für diese Frequenzmessung",
            new[] { "PFI0", "PFI2", "PFI3" }.All(pfi => Request("FREQ", "ctr0", pfi: pfi)[0] == -1));
        Check("5.3 PFI muss explizit gewählt sein", Request("FREQ", "ctr0")[0] == -1);
        bench.Cables.RemoveAt(1);
        Check("5.3 Kabel getrennt", Request("FREQ", "ctr0", pfi: "PFI1")[0] == -5);
        var block = Request("AI", "ai0", count: 512, repeat: true);
        Check("5.4 kontinuierlicher Messblock", block.Length == 513 && block.Skip(1).All(v => Near(v, 3.25)));
        Check("5.4 zweiter Block läuft weiter", Request("AI", "ai0", count: 512, repeat: true).Length == 513);
        Check("5.5 Poti über AI1", Near(Request("AI", "ai1")[1], 5.0067, .01));
        bench.Jumpers[6] = 1;
        Check("5.5 Portmuster D0/D1/D7", Request("DO", "port0", "131")[0] == 0 && Request("DI", "port0")[1] == 131);
        Check("5.5 falsche Portausgabe abgelehnt", Request("DO", "port0", "256")[0] == -2);
        Check("5.5 Eine Einzelausgabe ändert nur das ausgewählte Bit",
            Request("DO", "port0/line7", "0")[0] == 0 && Request("DI", "port0")[1] == 3 &&
            Request("DO", "port0/line7", "1")[0] == 0 && Request("DI", "port0")[1] == 131);
        Check("5.5 Die Einzelausgabe akzeptiert nur 0 und 1",
            Request("DO", "port0/line7", "2")[0] == -2 && Request("DI", "port0")[1] == 131);
        Check("5.3 PFI als Ausgang erkennen", Request("FREQ", "ctr0", pfi: "PFI1")[0] == -7);
        Check("Ausgänge zurücksetzen trennt AO-Treiber und schaltet DIO auf Eingang",
            Request("RESET", "port0")[0] == 0 && bench.Analog.All(output => output is null) &&
            bench.Digital.All(output => output is null) && bench.Captures.All(capture => capture is null) &&
            Request("AI", "ai0")[0] == -3);
        bench.Jumpers[6] = 0;
        bench.Buttons[2] = true;
        Check("DIO einzelne Eingangsleitung", Request("DI", "port0/line2")[1] == 1);
        bench.Usb = false;
        Check("USB getrennt", Request("AI", "ai0")[0] == -6);
        bench.Usb = true;
        bench.Connect("DMM VΩ+", "X8.3 +5");
        bench.Connect("DMM COM", "X8.2 DGND");
        Check("DMM Widerstand mit anliegender Fremdspannung", Request("OHM", "dmm")[0] == -8);

        server.Start();
        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.BoundPort);
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = 3000;
        stream.Write(Encoding.ASCII.GetBytes("PING\n"));
        byte[] header = new byte[8];
        stream.ReadExactly(header);
        int length = int.Parse(Encoding.ASCII.GetString(header), CultureInfo.InvariantCulture);
        byte[] data = new byte[length];
        stream.ReadExactly(data);
        Check("TCP Loopback und Nachrichtenrahmen", Encoding.ASCII.GetString(data) == "0\t1");
        CheckContinuousAnalog(Check);
        CheckProtection(Check);
        CheckWorkbench(Check, previewPath);
        return string.Join(Environment.NewLine, results) + $"{Environment.NewLine}TOTAL {results.Count} PASS{Environment.NewLine}";
    }

    private static void CheckContinuousAnalog(Action<string, bool> check)
    {
        var bench = new Bench();
        using var server = new DaqServer(bench, 0);
        bench.Connect("X2 AO0", "X4 AI0");
        string sine = string.Join('\t', Enumerable.Range(0, 100)
            .Select(i => (2 + 2 * Math.Sin(2 * Math.PI * i / 100)).ToString("G17", CultureInfo.InvariantCulture)));
        string ao = $"AO|myDAQ1/ao0||5000|100|10|1|{sine}";
        check("5.4 Sinus 50 Hz, 4 Vss und +2 V starten", server.Execute(ao)[0] == 0);
        Output? source = bench.Analog[0];
        double[] first = server.Execute("AI|myDAQ1/ai0||1000|100|10|1|");
        bool stable = first.Length == 101 && first[0] == 0 && first.Skip(1).Min() < .05 && first.Skip(1).Max() > 3.95;
        var wholeStream = new List<double>(first.Skip(1));
        for (int block = 0; block < 24; block++)
        {
            stable &= server.Execute(ao)[0] == 0 && ReferenceEquals(source, bench.Analog[0]);
            int count = block % 2 == 0 ? 100 : 99;
            double[] read = server.Execute($"AI|myDAQ1/ai0||1000|{count}|10|1|");
            stable &= read.Length == count + 1 && read[0] == 0;
            wholeStream.AddRange(read.Skip(1));
        }
        check("5.4 Wiederholtes AO-Schreiben behält Frequenz und Phase über AI-Blockgrenzen",
            stable && wholeStream.Skip(20).Zip(wholeStream, (actual, previousPeriod) => Math.Abs(actual - previousPeriod) < .002).All(same => same));
        bool newSource = server.Execute("AO|myDAQ1/ao0||1000|1|10|0|1")[0] == 0 && bench.Captures[0] is null;
        double[] dc = server.Execute("AI|myDAQ1/ai0||1000|100|10|1|");
        check("5.4 Neuer AO-Auftrag verwirft den alten Erfassungspunkt",
            newSource && dc.Length == 101 && dc[0] == 0 && dc.Skip(1).All(value => Math.Abs(value - 1) < .002));
    }

    private static void CheckProtection(Action<string, bool> check)
    {
        var bench = new Bench();
        using var server = new DaqServer(bench, 0);
        double Status(string operation, string channel, string value = "", int count = 1, int range = 10, bool repeat = false) =>
            server.Execute($"{operation}|myDAQ1/{channel}||1000|{count}|{range}|{(repeat ? 1 : 0)}|{value}")[0];

        const string digital = "X7.6 DIO4";
        const string plusFive = "X8.3 +5";
        const string plusFifteen = "X7.4 +15";
        const string minusFifteen = "X7.5 -15";
        const string analogGround = "X6.3 AGND";
        const string digitalGround = "X8.2 DGND";
        check("J7 startet geöffnet", bench.Jumpers[6] == 0);

        bench.Connect(plusFive, digital);
        var value = server.Execute("DI|myDAQ1/port0/line4||1000|1|10|0|");
        check("5 V am Digitaleingang sind gültig", value.SequenceEqual(new double[] { 0, 1 }) && bench.Powered);
        check("Ein aktiv getriebener Digitalausgang erkennt Rückstrom über 4 mA",
            Status("DO", "port0/line4", "0") == -4 && bench.ProtectionTrip?.Signal == "DIO4" && !bench.Powered);

        bench.Cables.Clear();
        check("Schutz aus, Digitalleitungen wieder im Eingangsbetrieb",
            bench.ResetProtection(0) && bench.Digital.All(output => output is null));
        bench.Connect(plusFifteen, digital);
        bench.Observe(0);
        check("+15 V am Digitaleingang löst bereits beim Stecken aus",
            bench.ProtectionTrip?.Signal == "DIO4" && Status("DI", "port0/line4") == -4);
        check("Zurücksetzen bei unverändertem Fehler löst den Schutz erneut aus",
            !bench.ResetProtection(0) && bench.ProtectionTrip?.Signal == "DIO4");

        bench.Cables.Clear();
        bench.Connect(minusFifteen, digital);
        check("Negative Spannung löst auch am unbenutzten Digitaleingang aus",
            !bench.ResetProtection(0) && bench.ProtectionTrip?.Signal == "DIO4");

        bench.Cables.Clear();
        bench.Connect(plusFifteen, "myDAQ · 07 AI0+");
        check("AI oberhalb ±10,5 V und unter ±16 V meldet Bereichsfehler ohne Auslösung",
            bench.ResetProtection(0) && Status("AI", "ai0") == -4 &&
            server.LastElectricalIssue?.Signal == "AI0+" && bench.Powered);
        var aiProtection = new Circuit([], [],
            [new("AI0+", "AGND", 17, 1, 1, 17)], "AGND");
        check("AI oberhalb der ±16-V-Schutzangabe löst den virtuellen Schutz aus",
            MyDaqLimits.Trip(aiProtection)?.Signal == "AI0+");
        var aoProtection = new Circuit([], [],
            [new("AO0", "AGND", -17, 1, 1, 17)], "AGND");
        check("AO außerhalb ±16 V erkennt eine externe Überspannung",
            MyDaqLimits.Trip(aoProtection)?.Signal == "AO0");

        bench.Cables.Clear();
        bench.Connect(plusFifteen, analogGround);
        check("Kurzschluss überlastet die +15-V-Schiene",
            !bench.ResetProtection(0) && bench.ProtectionTrip?.Signal == "+15" &&
            bench.ProtectionTrip.Message.Contains("32 mA"));
        var power = new Circuit([], [new("+15", "AGND", 600), new("-15", "AGND", 600)],
            [new("+15", "AGND", 15, 1, MyDaqLimits.Rail15Amps, 15),
             new("-15", "AGND", -15, 1, MyDaqLimits.Rail15Amps, 15)], "AGND");
        check("Die gemeinsame 500-mW-Grenze greift unterhalb der Einzelstromgrenzen",
            power.OutputFault is null && MyDaqLimits.Trip(power)?.Message.Contains("500 mW") == true);

        bench.Cables.Clear();
        bench.Connect("X6.1 AO1", "X7.2 DIO1");
        bench.Jumpers[0] = 0;
        check("Offener J1 trennt AGND und DGND",
            !bench.Observe(0).SharesNode("AGND", "DGND"));
        check("Ohne Massebezug meldet der Digitaleingang einen Fehler",
            bench.ResetProtection(0) && Status("AO", "ao1", "3") == 0 && Status("DI", "port0/line1") == -3 &&
            server.LastElectricalIssue?.Message.Contains("J1") == true);
        check("Ohne Massebezug meldet der Zähler einen Fehler",
            server.Execute("FREQ|myDAQ1/ctr0|PFI1|1000|1|10|0|")[0] == -3);
        bench.Connect(analogGround, digitalGround);
        check("Eine gesteckte Masseleitung gibt dem Digitaleingang einen Bezug",
            server.Execute("DI|myDAQ1/port0/line1||1000|1|10|0|").SequenceEqual([0, 1]));
        bench.Cables.RemoveAt(bench.Cables.Count - 1);
        bench.Jumpers[0] = 1;
        bench.Jumpers[6] = 1;
        check("AO-Ausgabe außerhalb des Messbereichs wird abgelehnt und bleibt aus",
            bench.ResetProtection(0) && Status("AO", "ao1", "10.1") == -2 &&
            server.LastElectricalIssue?.Signal == "AO1" && bench.Analog[1] is null && bench.Powered);
        check("Der spätere Rechteck-High-Pegel mit gestecktem J7 löst AO-Überstrom aus",
            Status("AO", "ao1", "0\t0\t4\t4", count: 4, repeat: true) == -4 &&
            bench.ProtectionTrip?.Signal == "AO1" && bench.ProtectionTrip.Message.Contains("2 mA") &&
            bench.ProtectionTrip.Message.Contains("J7 öffnen"));
        bench.Jumpers[6] = 0;
        check("Nach Öffnen von J7 kann das Rechteck den Digitaleingang speisen",
            bench.ResetProtection(0) && Status("AO", "ao1", "0\t0\t4\t4", count: 4, repeat: true) == 0);
        check("Gleichzeitige Analogausgaben brauchen denselben Bereich",
            Status("AO", "ao0", "1", range: 2) == -2 && bench.Powered && bench.Analog[0] is null);
        check("Undefinierter TTL-Pegel ergibt eine Meldung ohne Abschaltung",
            Status("AO", "ao1", "1.4") == 0 && Status("DI", "port0/line1") == -5 &&
            server.LastElectricalIssue?.Signal == "DIO1" && bench.Powered);
        check("16-Bit-Wandlung im gewählten Bereich",
            MyDaqLimits.QuantizeVoltage(0.0001, 10) == 0 &&
            MyDaqLimits.QuantizeVoltage(2.0 / 32768, 2) == 2.0 / 32768 &&
            MyDaqLimits.QuantizeVoltage(10, 10) == 10 - 10.0 / 32768);
        check("Abgelaufene Spitzen eines endlichen Signals lösen nach dem Umstecken nicht erneut aus",
            new Output([7, 0], 1, false, 0, 10).PeaksAfter(2) == (0, 0));
    }

    private static void CheckWorkbench(Action<string, bool> check, string? previewPath)
    {
        var bench = new Bench();
        using var transport = new DaqServer(bench, 0);
        var window = new WorkbenchWindow(bench, transport);
        var content = (FrameworkElement)window.Content;
        var desktop = new Size(1480, 910);
        void Layout(Size size)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            window.Measure(size);
            window.Arrange(new Rect(size));
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            // Apply the window's deferred SizeChanged handler before measuring the child again.
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        try
        {
            Layout(desktop);
            var board = Descendants(content).OfType<Canvas>().Single(c => c.Width == 1120 && c.ClipToBounds);
            var pins = board.Children.OfType<Button>().Where(button => Bench.Sockets.ContainsKey(AutomationProperties.GetName(button)))
                .ToDictionary(AutomationProperties.GetName);
            check("Alle Modellanschlüsse liegen auf der Steckansicht", pins.Count == Bench.Sockets.Count &&
                pins.Values.All(b => Canvas.GetLeft(b) >= 0 && Canvas.GetLeft(b) + b.Width <= board.Width &&
                    Canvas.GetTop(b) >= 0 && Canvas.GetTop(b) + b.Height <= board.Height));

            var hitBoxes = pins.Values.Select(button => new Rect(Canvas.GetLeft(button), Canvas.GetTop(button),
                button.Width, button.Height)).ToArray();
            check("Anschlussflächen überlappen sich nicht", !hitBoxes.SelectMany((a, i) =>
                hitBoxes.Skip(i + 1).Select(b => Rect.Intersect(a, b))).Any(r => !r.IsEmpty && r.Width > 0 && r.Height > 0));

            CheckImage(previewPath);

            var radio = Descendants(content).OfType<RadioButton>().Single(button => button.Name == "ExploreMode");
            radio.IsChecked = true;
            Click(pins["X2 AO0"]);
            Click(pins["X4 AI0"]);
            check("Erkunden zeigt die Anschlussbeschreibung und steckt kein Kabel", bench.Cables.Count == 0 &&
                Descendants(content).OfType<TextBlock>().Any(t => t.Text.Contains("Innenleiter führt zu J4.1")));
            Descendants(content).OfType<RadioButton>().Single(button => Equals(button.Content, "Verdrahten")).IsChecked = true;
            Click(pins["X2 AO0"]);
            Click(pins["Widerstand A"]);
            check("Ein BNC-Ende kann keine Widerstandsklemme belegen", bench.Cables.Count == 0);
            Click(pins["X4 AI0"]);
            check("Ein zweiter BNC-Anschluss steckt die Leitung ins elektrische Modell", bench.Cables.Count == 1 &&
                bench.Cables[0].A.Id == "X2 AO0" && bench.Cables[0].B.Id == "X4 AI0");
            var combo = Descendants(content).OfType<ComboBox>().Single(c => c.Name == "CableFault");
            combo.SelectedItem = "Kabelbruch";
            check("Der ausgewählte Kabelfehler erreicht das elektrische Modell", bench.Cables[0].Fault == CableFault.Open);
            CheckImage(previewPath == null ? null : Path.Combine(Path.GetDirectoryName(previewPath) ?? "",
                Path.GetFileNameWithoutExtension(previewPath) + "-kabelbruch.png"));
            Click(pins["DMM VΩ+"]);
            Click(pins["Widerstand A"]);
            check("Zwei Leitungen lassen sich unabhängig auswählen", bench.Cables.Count == 2);
            Layout(desktop);
            var cableLayer = board.Children.OfType<Canvas>().Single();
            var firstWire = cableLayer.Children.OfType<System.Windows.Shapes.Path>().Single(path => Equals(path.Tag, 0));
            var geometry = (PathGeometry)firstWire.Data;
            // The self-test lays out the visual tree without showing a window.
            bool reachable = Enumerable.Range(2, 7).Any(step =>
            {
                geometry.GetPointAtFractionLength(step / 10.0, out Point point, out _);
                DependencyObject? hit = null;
                VisualTreeHelper.HitTest(board,
                    visual => visual is UIElement { IsHitTestVisible: false }
                        ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
                    result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; },
                    new PointHitTestParameters(point));
                return ReferenceEquals(hit, firstWire);
            });
            check("Eine Leitung auf der Platine hat eine anklickbare Fläche", reachable);
            firstWire.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            var list = Descendants(content).OfType<ListBox>().Single(c => c.Name == "CableList");
            check("Klick auf Leitung markiert ihren Listeneintrag und beide Anschlüsse",
                list.SelectedIndex == 0 && Equals(pins["X2 AO0"].Tag, "selected") &&
                Equals(pins["X4 AI0"].Tag, "selected"));
            var remove = Descendants(content).OfType<Button>().Single(button => button.Name == "RemoveCable");
            Click(remove);
            check("Entfernen trennt ausschließlich die zuvor markierte Leitung",
                bench.Cables.Count == 1 && bench.Cables[0].A.Id == "DMM VΩ+" &&
                ((FrameworkElement)combo.Parent).Visibility == Visibility.Collapsed);
            list.SelectedIndex = 0;
            Click(remove);

            Click(pins["X7.4 +15"]);
            Click(pins["X7.6 DIO4"]);
            Layout(desktop);
            var notice = Descendants(content).OfType<Border>().Single(b => b.Name == "ElectricalNotice");
            var message = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "ElectricalMessage");
            check("Schutzmeldung nennt den falschen Anschluss und markiert ihn",
                notice.Visibility == Visibility.Visible && message.Text.Contains("DIO4") &&
                Equals(pins["X7.6 DIO4"].Tag, "electrical") && !bench.Powered);
            CheckImage(previewPath == null ? null : Path.Combine(Path.GetDirectoryName(previewPath) ?? "",
                Path.GetFileNameWithoutExtension(previewPath) + "-schutz.png"));
            Click(remove);
            Click(Descendants(content).OfType<Button>().Single(button => button.Name == "ResetProtection"));
            check("Nach Entfernen der Leitung und Rücksetzen ist die Anzeige wieder bereit",
                notice.Visibility == Visibility.Collapsed && bench.Powered);

            var output = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "AnalogOutputState");
            var analogRead = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "LastAnalogRead");
            check("Ausgangs- und Eingangsadressen stehen direkt in der Werkbank",
                new[] { "myDAQ1/ao0", "myDAQ1/ao1", "myDAQ1/ai0", "myDAQ1/ai1" }.All(address =>
                    Descendants(content).OfType<TextBlock>().Any(t => t.Text == address)));
            Click(pins["X2 AO0"]);
            Click(pins["X4 AI0"]);
            transport.Execute("AO|myDAQ1/ao0||1000|1|10|0|0.5");
            Click(pins["DMM VΩ+"]);
            Click(pins["Widerstand A"]);
            check("AO-Auftrag wird bestätigt, während die AI-Messung aussteht",
                output.Text.Contains("AO0 aktiv") && analogRead.Text.Contains("ausstehend") &&
                Descendants(content).OfType<TextBlock>().Any(t => t.Text == "Ausgabe bestätigt · myDAQ1/ao0"));
            transport.Execute("AI|myDAQ1/ai0||1000|4|10|0|");
            list.SelectedIndex = 1;
            Click(remove);
            check("Messbestätigung ergänzt den tatsächlichen Spannungsbereich",
                output.Text.Contains("AO0 aktiv") && analogRead.Text.Contains("4 Werte") &&
                transport.LastAnalogReading is { Status: 0, Minimum: > 0.49, Maximum: < 0.51 } &&
                Descendants(content).OfType<TextBlock>().Any(t => t.Text == "Messung abgeschlossen · myDAQ1/ai0"));
            list.SelectedIndex = 0;
            Click(remove);
            transport.Execute("AI|myDAQ1/ai0||1000|4|10|0|");
            Click(pins["DMM VΩ+"]);
            Click(pins["Widerstand A"]);
            check("Messung nach dem Abziehen meldet den fehlenden Signalweg",
                output.Text.Contains("AO0 aktiv") && analogRead.Text.Contains("Signalweg oder Messbezug fehlt") &&
                transport.LastAnalogReading is { Status: -3, SampleCount: 0 });
            transport.Execute("RESET|myDAQ1/port0||1000|1|10|0|");
            Click(remove);
            check("Zurückgesetzter Ausgang wird als aus angezeigt", output.Text == "Analogausgänge aus");

            transport.Execute("AO|myDAQ1/ao0||1000|2|10|1|0\t1");
            transport.Execute("AO|myDAQ1/ao1||1000|1|10|0|1.25");
            Click(pins["X2 AO0"]);
            Click(pins["X4 AI0"]);
            var stop0 = Descendants(content).OfType<Button>().Single(b => b.Name == "StopAo0");
            var stop1 = Descendants(content).OfType<Button>().Single(b => b.Name == "StopAo1");
            check("Nur laufende Analogausgänge lassen sich stoppen", stop0.IsEnabled && stop1.IsEnabled);
            if (previewPath is not null)
                CheckImage(Path.Combine(Path.GetDirectoryName(previewPath) ?? "",
                    Path.GetFileNameWithoutExtension(previewPath) + "-ausgaenge.png"));
            Click(stop0);
            check("AO0-Stopp beendet das Signal auf AI0 und lässt AO1 laufen",
                !stop0.IsEnabled && stop1.IsEnabled && output.Text == "AO1 aktiv" &&
                bench.Analog[1]?.At(bench.Clock.Elapsed.TotalSeconds) == 1.25 &&
                transport.Execute("AI|myDAQ1/ai0||1000|4|10|0|")[0] == -3);
            Click(stop1);
            check("AO1 kann getrennt ausgeschaltet werden",
                !stop1.IsEnabled && output.Text == "Analogausgänge aus" && bench.Cables.Count == 1);

            Layout(new Size(1000, 760));
            var side = Descendants(content).OfType<Border>().Single(b => b.Child is StackPanel group &&
                group.Children.Contains(combo.Parent as UIElement));
            check("Im schmalen Fenster rückt die Seitenleiste unter die Werkbank", Grid.GetRow(side) == 1);
        }
        finally { window.Close(); }

        void CheckImage(string? file)
        {
            if (file is null) return;
            content.Measure(desktop);
            content.Arrange(new Rect(desktop));
            content.UpdateLayout();
            var image = new RenderTargetBitmap((int)desktop.Width, (int)desktop.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
