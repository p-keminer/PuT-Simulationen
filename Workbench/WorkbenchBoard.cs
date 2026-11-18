using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PuT3;

internal sealed partial class WorkbenchWindow
{
    private const double BoardWidth = 1120;
    private const double BoardHeight = 720;
    private static readonly Brush PhotoInk = ColorOf("#172A2E");
    private static readonly Brush GlowingLed = ColorOf("#FF5F5A");
    private static readonly Brush LedReflection = ColorOf("#FFE4B0");
    private static readonly Brush PowerLed = ColorOf("#3DE29B");
    private static readonly Brush JumperLabel = ColorOf("#DCDFEACF");
    private static readonly Brush CableShadow = ColorOf("#AA111F27");
    private static readonly Brush FaultLine = ColorOf("#FFC0AC");
    private static readonly Brush[] ResistorColors =
    [
        ColorOf("#2F2620"), ColorOf("#77462D"), ColorOf("#A8423B"), ColorOf("#CB722E"),
        ColorOf("#D6A831"), ColorOf("#446747"), ColorOf("#4B6884"), ColorOf("#775789"),
        ColorOf("#777C78"), ColorOf("#DEDACE"), ColorOf("#BE9950")
    ];
    private readonly Dictionary<string, Point> anchors = [];
    private readonly Ellipse[] leds = new Ellipse[8];
    private readonly Border[] resistorBands = new Border[4];
    private Ellipse power = null!;

    private void BuildBoard()
    {
        using var data = Application.GetResourceStream(PackUri("Assets/anschluesse.json")).Stream;
        foreach (var (key, point) in JsonSerializer.Deserialize<Dictionary<string, double[]>>(data)!)
        {
            if (point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1]) ||
                point[0] < 0 || point[0] > BoardWidth || point[1] < 0 || point[1] > BoardHeight)
                throw new InvalidOperationException($"Ungültige Bildposition für {key}.");
            anchors.Add(key, new Point(point[0], point[1]));
        }
        using var pixels = Application.GetResourceStream(PackUri("Assets/steckwerkbank.png")).Stream;
        var bitmap = BitmapFrame.Create(pixels, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        var image = new Image { Source = bitmap, Width = BoardWidth, Height = BoardHeight, Stretch = Stretch.Fill,
            IsHitTestVisible = false };
        Place(board, image, 0, 0);
        Place(board, wires, 0, 0, 1);
        var dark = Theme("StageInk");

        Legend("NI myDAQ", 180, 45, 30, PhotoInk, bold: true);
        Legend("Schraubklemmen und Multimeter", 181, 83, 14, PhotoInk);
        var usb = anchors["USB indicator"];
        power = Indicator(usb, 11);
        Legend("USB", usb.X - 17, usb.Y + 11, 11, PhotoInk, 34);
        Legend("DMM", 815, 27, 11, PhotoInk, 40);

        Legend("miniAdap‑1", 155, 237, 25, dark, bold: true);
        Legend("Experimentieradapter", 316, 249, 13, dark);
        Legend("X1", 253, 296, 11, PhotoInk);
        Legend("S1", 225, 544, 11, PhotoInk, 24);
        Legend("Prüfwiderstand", 171, 647, 19, dark, bold: true);

        foreach (Socket pin in Bench.Sockets.Values)
        {
            string[] parts = pin.Id.Split(' ');
            if (pin.Id.StartsWith("myDAQ", StringComparison.Ordinal))
            {
                Pin(pin, width: 38, above: 46, below: 14, label: pin.Signal, labelOffset: -40, PhotoInk,
                    annotation: parts[2]);
            }
            else if (pin.Id.StartsWith("DMM", StringComparison.Ordinal))
            {
                Pin(pin, 51, 20, 36, parts[1], 19, PhotoInk);
            }
            else if (pin.Shield is not null)
            {
                Pin(pin, 63, 30, 40, pin.Id, 22, PhotoInk);
            }
            else if (pin.Id.StartsWith('X'))
            {
                string text = pin.Signal switch { "EXT0" => "J4.1", "EXT1" => "J5.1", _ => pin.Signal };
                Pin(pin, 35, 18, 19, text, 4, PhotoInk);
            }
            else
            {
                Pin(pin, 49, 19, 37, parts[1], 20, dark);
            }
        }

        for (int i = 0; i < 4; i++)
        {
            string group = $"X{i + 6}";
            var pins = Bench.Sockets.Keys.Where(key => key.StartsWith(group + ".", StringComparison.Ordinal)).ToArray();
            double center = pins.Select(key => anchors[key].X).Average();
            Legend(group, center - 23, 577, 11, PhotoInk, 46, bold: true);
        }

        for (int i = 0; i < leds.Length; i++)
        {
            string label = $"D{i}";
            var point = anchors[label];
            leds[i] = Indicator(point, 17);
            Legend(label, point.X - 14, point.Y + 13, 11, PhotoInk, 28);
        }
        for (int i = 0; i < model.Jumpers.Length; i++) BuildJumper(i);
        for (int i = 0; i < model.Pots.Length; i++) BuildPot(i);
        for (int i = 0; i < model.Buttons.Length; i++) BuildBoardButton(i);
        for (int i = 0; i < resistorBands.Length; i++)
        {
            var point = anchors[$"Resistor band {i}"];
            var colorBand = new Border { Width = 4, Height = 20, CornerRadius = new CornerRadius(1),
                Opacity = .86, IsHitTestVisible = false };
            resistorBands[i] = colorBand;
            Place(board, colorBand, point.X - 2, point.Y - 7, 1);
        }
        UpdateResistorBands(model.Resistance);
    }

    private void UpdateResistorBands(double resistance)
    {
        int multiplier = resistance >= 10_000 ? 3 : 2;
        int digits = (int)(resistance / (multiplier == 3 ? 1000 : 100));
        int[] colorNumbers = [digits / 10, digits % 10, multiplier, 10];
        for (int i = 0; i < resistorBands.Length; i++)
            resistorBands[i].Background = ResistorColors[colorNumbers[i]];
    }

    private void Pin(Socket socket, double width, double above, double below, string label, double labelOffset,
        Brush foreground, string? annotation = null)
    {
        Point point = anchors[socket.Id];
        var contents = new Canvas { Width = width - 2, Height = above + below - 2, IsHitTestVisible = false };
        var caption = new StackPanel { Width = contents.Width };
        if (annotation != null)
        {
            caption.Children.Add(new TextBlock { Text = annotation, Foreground = foreground,
                FontFamily = new FontFamily("Consolas"), FontSize = 9, TextAlignment = TextAlignment.Center });
        }
        caption.Children.Add(new TextBlock { Text = label, Foreground = foreground,
            FontFamily = new FontFamily("Consolas"), FontSize = width < 39 ? 11 : 12,
            FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
        Place(contents, caption, 0, above + labelOffset);
        var pin = new Button { Content = contents, Style = (Style)FindResource("Contact"),
            Width = width, Height = above + below, ToolTip = Describe(socket) };
        AutomationProperties.SetName(pin, socket.Id);
        pin.Click += (_, _) => Plug(socket);
        sockets.Add(socket.Id, pin);
        Place(board, pin, point.X - width / 2, point.Y - above, 2);
    }

    private void BuildJumper(int index)
    {
        string key = $"J{index + 1}";
        bool threePins = index is 3 or 4;
        Point point = anchors[key];
        string[] options = threePins ? ["Offen", "1–2", "2–3"] : ["Offen", "Zu"];
        string explanation = index switch
        {
            0 => "J1 brückt analoge und digitale Masse auf der Platine.",
            1 => "J2 verbindet AI0− mit der analogen Masse.",
            2 => "J3 verbindet AI1− mit der analogen Masse.",
            3 => "J4 wählt für AI0 die externe Buchse (1–2) oder Poti R6 (2–3).",
            4 => "J5 wählt für AI1 die externe Buchse (1–2) oder Poti R7 (2–3).",
            5 => "J6 schaltet den Summer zur Digitalleitung DIO5.",
            _ => "J7 verbindet den gemeinsamen Rückleiter der acht LEDs."
        };
        var shunt = new Border { Width = 21, Height = 11, CornerRadius = new CornerRadius(2),
            Background = ColorOf("#38454B"), BorderBrush = ColorOf("#A6B2B3"), BorderThickness = new Thickness(1),
            IsHitTestVisible = false };
        Place(board, shunt, point.X, point.Y - 4, 1);
        var caption = new TextBlock { Foreground = PhotoInk, FontSize = 10, FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center };
        var contents = new Canvas { Width = 52, Height = 34, IsHitTestVisible = false };
        Place(contents, new Border { Width = 49, Background = JumperLabel, CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0, 0.5, 0, 0.5), Child = caption }, 1.5, 20);
        var button = new Button { Content = contents, Style = (Style)FindResource("Contact"), Width = 54, Height = 36 };
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        var items = new List<MenuItem>();
        void Update()
        {
            int setting = model.Jumpers[index];
            caption.Text = $"{key} {options[setting]}";
            button.ToolTip = $"{explanation} Stellung {options[setting]}.";
            AutomationProperties.SetName(button, $"Steckbrücke {key}, {options[setting]}");
            shunt.Visibility = setting == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (setting != 0)
                Canvas.SetLeft(shunt, (anchors[$"{key}.{setting}"].X + anchors[$"{key}.{setting + 1}"].X) / 2 - 10.5);
            for (int n = 0; n < items.Count; n++) items[n].IsChecked = n == setting;
        }
        for (int i = 0; i < options.Length; i++)
        {
            int setting = i;
            var item = new MenuItem { Header = options[i], IsCheckable = true };
            item.Click += (_, _) =>
            {
                lock (model.Gate) model.Jumpers[index] = setting;
                Update();
                ReadBoard();
            };
            items.Add(item);
            menu.Items.Add(item);
        }
        button.Click += (_, _) =>
        {
            if (explore.IsChecked == true)
            {
                portTitle.Text = $"Steckbrücke {key}";
                portDescription.Text = explanation;
                ShowMessage($"Steckt aktuell auf {options[model.Jumpers[index]]}.");
                return;
            }
            menu.IsOpen = true;
        };
        Update();
        Place(board, button, point.X - 27, point.Y - 11, 2);
    }

    private void BuildPot(int index)
    {
        string key = $"R{index + 6}";
        Point point = anchors[key];
        var indicator = new Line { X1 = 1, Y1 = 6, X2 = 13, Y2 = 6, Stroke = PhotoInk, StrokeThickness = 2,
            Width = 14, Height = 12, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
        Place(board, indicator, point.X - 7, point.Y - 6, 2);
        var caption = Legend("", point.X - 36, point.Y + 19, 11, Theme("StageInk"), 72, true);
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = model.Pots[index],
            SmallChange = .01, LargeChange = .10, Width = 70, Height = 19,
            ToolTip = $"Potentiometer {key} · Stellung ändern",
            IsMoveToPointEnabled = true };
        AutomationProperties.SetName(slider, $"Potentiometer {key}");
        void Update()
        {
            indicator.RenderTransform = new RotateTransform(-135 + 270 * slider.Value);
            caption.Text = $"{key} · {slider.Value:P0}";
        }
        slider.ValueChanged += (_, _) =>
        {
            lock (model.Gate) model.Pots[index] = slider.Value;
            Update();
        };
        Update();
        Place(board, slider, point.X - 35, point.Y + 33, 2);
    }

    private void BuildBoardButton(int index)
    {
        string key = $"T{index}";
        Point point = anchors[key];
        var toggle = new ToggleButton { Content = key, IsChecked = model.Buttons[index], Width = 32, Height = 35,
            Foreground = PhotoInk, Style = (Style)FindResource("BoardButton"),
            ToolTip = $"{key} legt DIO{index} auf +5 V." };
        AutomationProperties.SetName(toggle, $"Taster {key}, DIO{index}");
        toggle.Click += (_, _) => { lock (model.Gate) model.Buttons[index] = toggle.IsChecked == true; };
        Place(board, toggle, point.X - 16, point.Y - 13, 2);
    }

    private Ellipse Indicator(Point point, double size)
    {
        var mark = new Ellipse { Width = size, Height = size, IsHitTestVisible = false, Visibility = Visibility.Hidden };
        Place(board, mark, point.X - size / 2, point.Y - size / 2, 2);
        return mark;
    }

    private TextBlock Legend(string text, double x, double y, double size, Brush foreground,
        double? width = null, bool bold = false)
    {
        var legend = new TextBlock { Text = text, Foreground = foreground, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, IsHitTestVisible = false };
        if (width.HasValue) { legend.Width = width.Value; legend.TextAlignment = TextAlignment.Center; }
        Place(board, legend, x, y, 2);
        return legend;
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y, int z = 0)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        Panel.SetZIndex(element, z);
        canvas.Children.Add(element);
    }

    private void ReadBoard()
    {
        if (!Monitor.TryEnter(model.Gate)) return;
        try
        {
            Circuit circuit = model.Snapshot(model.Clock.Elapsed.TotalSeconds);
            power.Visibility = model.Usb ? Visibility.Visible : Visibility.Hidden;
            power.Fill = PowerLed;
            for (int i = 0; i < leds.Length; i++)
            {
                bool on = model.Usb && model.Adapter && model.Jumpers[6] != 0 &&
                    circuit.Value($"DIO{i}") - circuit.Value("DGND") >= 2;
                leds[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
                leds[i].Fill = GlowingLed;
                leds[i].Stroke = LedReflection;
            }
            bool good = model.Usb && model.Adapter && !circuit.Overloaded;
            status.Text = !model.Usb ? "USB getrennt" : circuit.Overloaded ? "Elektrischer Konflikt" :
                !model.Adapter ? "Adapter abgezogen" : "Steckwerkbank bereit";
            status.Foreground = Theme(good ? "Ready" : "Warning");
            statePill.Background = Theme(good ? "ReadySurface" : "WarningSurface");
            lastRequest.Text = server.LastRequest ?? "Wartet auf eine Anfrage aus LabVIEW";
        }
        finally { Monitor.Exit(model.Gate); }
    }

    private void RedrawWires()
    {
        Cable[] cables;
        lock (model.Gate) cables = model.Cables.ToArray();
        var connected = cables.SelectMany(cable => new[] { cable.A.Id, cable.B.Id }).ToHashSet();
        foreach (var (key, pin) in sockets)
            pin.Tag = key == selected || key == inspected ? "selected" : connected.Contains(key) ? "connected" : null;
        wires.Children.Clear();
        for (int i = 0; i < cables.Length; i++)
        {
            var cable = cables[i];
            Point a = anchors[cable.A.Id], b = anchors[cable.B.Id];
            bool active = i == cableList.SelectedIndex;
            double bow = Math.Clamp((b - a).Length / 5, 25, 85);
            bool lift = Math.Max(a.Y, b.Y) > BoardHeight - 65 || i % 2 == 1;
            if (lift) bow = -bow;
            var curve = new PathFigure { StartPoint = a };
            curve.Segments.Add(new BezierSegment(new Point(a.X, a.Y + bow), new Point(b.X, b.Y + bow), b, true));
            var geometry = new PathGeometry([curve]);
            var color = ColorOf(CableColors[i % CableColors.Length]);
            bool faulted = cable.Fault != CableFault.Intact;
            wires.Children.Add(new Path { Data = geometry, Stroke = CableShadow, StrokeThickness = active ? 9 : 7,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            wires.Children.Add(new Path { Data = geometry, Stroke = faulted ? FaultLine : color,
                StrokeThickness = active ? 5 : 3.5, StrokeDashArray = faulted ? [4, 2.5] : null,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            foreach (Point point in new[] { a, b })
                Place(wires, new Ellipse { Width = 9, Height = 9, Fill = faulted ? FaultLine : color,
                    Stroke = CableShadow, StrokeThickness = 1.5 }, point.X - 4.5, point.Y - 4.5);
        }
    }

    private static string Describe(Socket socket)
    {
        if (socket.Id.StartsWith("Widerstand", StringComparison.Ordinal))
            return "Ein Ende des Prüfwiderstands. Der Widerstand ist zwischen A und B angeschlossen.";
        if (socket.Id.StartsWith("DMM", StringComparison.Ordinal))
            return socket.Signal switch
            {
                "DMM+" => "Buchse für die positive Messleitung bei der Widerstandsmessung.",
                "DMM-" => "Gemeinsamer Bezug des Digitalmultimeters für die zweite Messleitung.",
                _ => "Buchse für die Strommessung. Für den Widerstand verwendest du VΩ+ und COM."
            };
        if (socket.Shield is not null)
            return socket.Signal switch
            {
                "EXT0" => "BNC-Eingang für AI0. Der Innenleiter führt zu J4.1, der Schirm zu AI0−.",
                "EXT1" => "BNC-Eingang für AI1. Der Innenleiter führt zu J5.1, der Schirm zu AI1−.",
                _ => $"BNC-Analogausgang {socket.Signal}. Das Ausgangssignal liegt zwischen Innenleiter und analoger Masse am Schirm."
            };
        if (socket.Signal.StartsWith("DIO", StringComparison.Ordinal))
        {
            string detail = socket.Signal[^1] <= '3' ? $" Diese Leitung ist auch als PFI{socket.Signal[^1]} adressierbar." : "";
            return $"Digitalleitung {socket.Signal}. LabVIEW kann sie lesen oder als Ausgang schalten.{detail}";
        }
        if (socket.Signal.StartsWith("AO", StringComparison.Ordinal)) return "Analogausgang. LabVIEW gibt hier einen Spannungsverlauf aus.";
        if (socket.Signal.StartsWith("AI", StringComparison.Ordinal))
            return socket.Signal.EndsWith('+') ? "Plusklemme eines Analogeingangs. Der Eingang misst zwischen Plus und Minus." :
                "Minusklemme eines Analogeingangs. Sie dient der Messung als elektrischer Bezug.";
        return socket.Signal switch
        {
            "AGND" => "Bezugsmasse der analogen Schaltung.",
            "DGND" => "Bezugsmasse der digitalen Schaltung.",
            "EXT0" => "Externer Signalweg am Pin J4.1 für den Analogeingang AI0.",
            "EXT1" => "Externer Signalweg am Pin J5.1 für den Analogeingang AI1.",
            _ => $"Versorgungsspannung {socket.Signal} V."
        };
    }
}
