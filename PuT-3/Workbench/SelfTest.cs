using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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

        Check("DMM leer", Request("OHM", "dmm")[0] == -3);
        bench.Fixture();
        Check("5.2 Widerstand über DMM-Buchsen", Near(Request("OHM", "dmm")[1], 3000));
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
        Check("5.3 falsche PFI0 liefert keine Frequenz", Request("FREQ", "ctr0", pfi: "PFI0")[0] == -5);
        Check("5.3 PFI muss explizit gewählt sein", Request("FREQ", "ctr0")[0] == -1);
        bench.Cables.RemoveAt(1);
        Check("5.3 Kabel getrennt", Request("FREQ", "ctr0", pfi: "PFI1")[0] == -5);
        var block = Request("AI", "ai0", count: 512, repeat: true);
        Check("5.4 kontinuierlicher Messblock", block.Length == 513 && block.Skip(1).All(v => Near(v, 3.25)));
        Check("5.4 zweiter Block läuft weiter", Request("AI", "ai0", count: 512, repeat: true).Length == 513);
        Check("5.5 Poti über AI1", Near(Request("AI", "ai1")[1], 5.0067, .01));
        Check("5.5 Portmuster D0/D1/D7", Request("DO", "port0", "131")[0] == 0 && Request("DI", "port0")[1] == 131);
        Check("5.5 falsche Portausgabe abgelehnt", Request("DO", "port0", "256")[0] == -2);
        Check("5.3 PFI als Ausgang erkennen", Request("FREQ", "ctr0", pfi: "PFI1")[0] == -7);
        Request("RESET", "port0");
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
        CheckWorkbench(Check, previewPath);
        return string.Join(Environment.NewLine, results) + $"{Environment.NewLine}TOTAL {results.Count} PASS{Environment.NewLine}";
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
            Click(Descendants(content).OfType<Button>().Single(button => button.Name == "RemoveCable"));
            check("Entfernen trennt die Leitung und blendet ihre Auswahl aus", bench.Cables.Count == 0 &&
                ((FrameworkElement)combo.Parent).Visibility == Visibility.Collapsed);

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
