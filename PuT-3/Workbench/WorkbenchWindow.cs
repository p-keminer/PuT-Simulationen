using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PuT3;

internal sealed partial class WorkbenchWindow : Window
{
    private static readonly string[] CableColors = ["#FFCE73", "#A2E9D4", "#A9C9FF", "#F0AAD0"];
    private static readonly (CableFault Fault, string Label)[] Faults =
    [
        (CableFault.Intact, "Intakt"), (CableFault.Open, "Kabelbruch"),
        (CableFault.ScreenOpen, "BNC-Schirm unterbrochen"), (CableFault.Intermittent, "Wackelkontakt"),
        (CableFault.Short, "BNC-Kurzschluss")
    ];
    private const string IdleHelp = "Zum Stecken klickst du nacheinander auf beide Anschlüsse.";

    private readonly Bench model;
    private readonly DaqServer server;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Canvas board = new() { Width = BoardWidth, Height = BoardHeight, ClipToBounds = true };
    private readonly Canvas wires = new() { Width = BoardWidth, Height = BoardHeight, Focusable = true };
    private readonly Dictionary<string, Button> sockets = [];
    private readonly ListBox cableList = new() { MaxHeight = 124, BorderThickness = new Thickness(0) };
    private readonly ComboBox fault = new();
    private readonly RadioButton explore = new() { Content = "Erkunden", GroupName = "WorkbenchMode", Name = "ExploreMode" };
    private readonly RadioButton wireMode = new() { Content = "Verdrahten", GroupName = "WorkbenchMode", IsChecked = true };
    private readonly Border statePill = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 5, 11, 5),
        HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock status = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly Border electricalNotice = new() { Name = "ElectricalNotice", Visibility = Visibility.Collapsed };
    private readonly TextBlock electricalMessage = new() { Name = "ElectricalMessage", TextWrapping = TextWrapping.Wrap };
    private readonly Button rearm = new() { Content = "Schutz zurücksetzen", Name = "ResetProtection" };
    private ElectricalIssue? displayedIssue;
    private readonly TextBlock lastRequest = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right,
        MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock outputState = new() { Name = "AnalogOutputState", TextWrapping = TextWrapping.Wrap };
    private readonly Button[] stopAnalog = [new(), new()];
    private readonly TextBlock lastAnalogRead = new() { Name = "LastAnalogRead", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock portTitle = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock portDescription = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, MinHeight = 47 };
    private readonly TextBlock instruction = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, MinHeight = 37 };
    private readonly TextBlock cableCount = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button remove = new() { Content = "Leitung entfernen", Name = "RemoveCable", IsEnabled = false };
    private readonly StackPanel cableActions = new();
    private readonly TextBlock emptyCables = new() { Text = "Noch keine Leitungen gesteckt.", TextWrapping = TextWrapping.Wrap };
    private readonly Grid workspace = new();
    private readonly Grid station = new();
    private Border sidebar = null!;
    private string? selected;
    private string? inspected;
    private bool refreshing;

    public WorkbenchWindow(Bench bench, DaqServer transport)
    {
        model = bench;
        server = transport;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = PackUri("WorkbenchTheme.xaml") });
        Title = "PuT-3 · Virtuelle Steckwerkbank";
        Width = Math.Min(1520, SystemParameters.WorkArea.Width);
        Height = Math.Min(965, SystemParameters.WorkArea.Height);
        MinWidth = 850;
        MinHeight = 600;
        Background = Theme("Paper");
        Foreground = Theme("Ink");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;

        var root = new DockPanel { Background = Theme("Paper") };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(BuildElectricalNotice(), Dock.Top);
        root.Children.Add(electricalNotice);
        KeyboardNavigation.SetIsTabStop(wires, false);

        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.Margin = new Thickness(22, 16, 22, 20);
        BuildStation();
        workspace.Children.Add(station);
        sidebar = BuildSidebar();
        workspace.Children.Add(sidebar);
        LayoutStation(Width);
        var scroll = new ScrollViewer { Content = workspace, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SizeChanged += (_, e) => LayoutStation(e.NewSize.Width);
        root.Children.Add(scroll);
        Content = root;

        SetPortHelp(null);
        RefreshCables();
        ReadBoard();
        timer.Tick += (_, _) => ReadBoard();
        timer.Start();
        Closed += (_, _) => timer.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && (wires.IsKeyboardFocused || cableList.IsKeyboardFocusWithin))
            {
                e.Handled = RemoveSelectedCable();
                return;
            }
            if (e.Key != Key.Escape || selected is null) return;
            ResetSelection();
            ShowMessage("Anschlusswahl aufgehoben.");
            e.Handled = true;
        };
    }

    private UIElement BuildHeader()
    {
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "PuT‑3  /  STECKWERKBANK", FontSize = 11,
            FontWeight = FontWeights.SemiBold, Foreground = Theme("Ready"), Margin = new Thickness(0, 0, 0, 2) });
        left.Children.Add(new TextBlock { Text = "NI myDAQ & miniAdap‑1", FontSize = 26,
            FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        statePill.Child = status;
        lastRequest.Foreground = Theme("Quiet");
        lastRequest.Margin = new Thickness(0, 4, 0, 0);
        right.Children.Add(statePill);
        right.Children.Add(lastRequest);
        var content = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        content.Children.Add(left);
        content.Children.Add(right);
        return new Border { Background = Theme("Card"), BorderBrush = Theme("Rule"),
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(24, 12, 24, 12), Child = content };
    }

    private Border BuildElectricalNotice()
    {
        electricalNotice.Background = Theme("WarningSurface");
        electricalNotice.BorderBrush = Theme("Rule");
        electricalNotice.BorderThickness = new Thickness(0, 0, 0, 1);
        electricalNotice.Padding = new Thickness(24, 9, 24, 9);
        electricalMessage.Foreground = Theme("Warning");
        electricalMessage.FontWeight = FontWeights.SemiBold;
        electricalMessage.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(electricalMessage, AutomationLiveSetting.Assertive);
        rearm.Style = (Style)FindResource("Action");
        rearm.Margin = new Thickness(12, 0, 0, 0);
        rearm.Click += (_, _) =>
        {
            lock (model.Gate)
            {
                server.ClearElectricalIssue();
                model.ResetProtection(model.Clock.Elapsed.TotalSeconds);
            }
            ReadBoard();
        };
        var row = new DockPanel();
        DockPanel.SetDock(rearm, Dock.Right);
        row.Children.Add(rearm);
        row.Children.Add(electricalMessage);
        electricalNotice.Child = row;
        return electricalNotice;
    }

    private void BuildStation()
    {
        station.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        station.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        wireMode.Style = explore.Style = (Style)FindResource("Mode");
        modes.Children.Add(wireMode);
        modes.Children.Add(explore);
        var segment = new Border { Background = Theme("Soft"), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(3), Child = modes };
        toolbar.Children.Add(segment);
        station.Children.Add(toolbar);
        wireMode.Checked += (_, _) => ChangeMode();
        explore.Checked += (_, _) => ChangeMode();

        BuildBoard();
        var view = new Viewbox { Child = board, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        var drawing = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Child = view };
        drawing.SizeChanged += (_, _) => drawing.Clip = new RectangleGeometry(
            new Rect(drawing.RenderSize), 10, 10);
        Grid.SetRow(drawing, 1);
        station.Children.Add(drawing);
    }

    private void LayoutStation(double width)
    {
        bool stacked = width < 1210;
        workspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        workspace.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 325);
        Grid.SetRow(sidebar, stacked ? 1 : 0);
        Grid.SetColumn(sidebar, stacked ? 0 : 1);
        sidebar.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(14, 0, 0, 0);
    }

    private Border BuildSidebar()
    {
        var side = new StackPanel();
        var connector = new StackPanel();
        connector.Children.Add(SectionHeading("Anschluss"));
        portTitle.Margin = new Thickness(0, 4, 0, 3);
        connector.Children.Add(portTitle);
        portDescription.Foreground = Theme("Quiet");
        connector.Children.Add(portDescription);
        instruction.Foreground = Theme("Ready");
        AutomationProperties.SetLiveSetting(instruction, AutomationLiveSetting.Polite);
        connector.Children.Add(new Border { Background = Theme("Soft"), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 6, 9, 2), Margin = new Thickness(0, 5, 0, 0), Child = instruction });
        side.Children.Add(connector);
        side.Children.Add(Separator());
        side.Children.Add(BuildLabViewStatus());
        side.Children.Add(Separator());

        var cables = new DockPanel();
        DockPanel.SetDock(cableCount, Dock.Right);
        cables.Children.Add(cableCount);
        cables.Children.Add(SectionHeading("Leitungen"));
        side.Children.Add(cables);
        emptyCables.Foreground = Theme("Quiet");
        emptyCables.Margin = new Thickness(0, 6, 0, 0);
        side.Children.Add(emptyCables);
        cableList.Name = "CableList";
        cableList.Margin = new Thickness(0, 5, 0, 0);
        AutomationProperties.SetName(cableList, "Gesteckte Leitungen");
        side.Children.Add(cableList);
        fault.Name = "CableFault";
        AutomationProperties.SetName(fault, "Zustand der ausgewählten Leitung");
        cableActions.Children.Add(new TextBlock { Text = "Zustand der Leitung", Margin = new Thickness(0, 6, 0, 3) });
        cableActions.Children.Add(fault);
        remove.Style = (Style)FindResource("Action");
        remove.Margin = new Thickness(0, 6, 0, 0);
        cableActions.Children.Add(remove);
        side.Children.Add(cableActions);
        cableList.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            if (cableList.SelectedIndex >= 0 && selected is not null)
            {
                selected = null;
                inspected = null;
                SetPortHelp(null);
            }
            SelectCable();
        };
        fault.SelectionChanged += (_, _) => ChangeCableFault();
        remove.ToolTip = "Entfernt nur die ausgewählte Leitung. In der Liste oder auf dem Kabel geht auch Entf.";
        remove.Click += (_, _) => RemoveSelectedCable();
        side.Children.Add(Separator());

        double[] values = [1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 15000];
        var resistors = new ComboBox { ItemsSource = values.Select(value =>
            $"{value / 1000:0} kΩ" + (value > 8000 ? " · Zusatz" : "")), SelectedIndex = Array.IndexOf(values, model.Resistance),
            Margin = new Thickness(0, 6, 0, 6) };
        resistors.SelectionChanged += (_, _) =>
        {
            if (resistors.SelectedIndex < 0) return;
            double resistance = values[resistors.SelectedIndex];
            lock (model.Gate) model.Resistance = resistance;
            UpdateResistorBands(resistance);
        };
        AutomationProperties.SetName(resistors, "Prüfwiderstand");
        side.Children.Add(SectionHeading("Prüfwiderstand"));
        side.Children.Add(resistors);

        var deviceControls = new StackPanel();
        var usb = new CheckBox { Content = "USB angeschlossen", IsChecked = model.Usb };
        var adapter = new CheckBox { Content = "miniAdap‑1 aufgesteckt", IsChecked = model.Adapter };
        usb.Click += (_, _) =>
        {
            lock (model.Gate) { model.Usb = usb.IsChecked == true; if (!model.Usb) model.ResetOutputs(); }
            ReadBoard();
        };
        adapter.Click += (_, _) =>
        {
            lock (model.Gate) model.Adapter = adapter.IsChecked == true;
            ReadBoard();
        };
        deviceControls.Children.Add(usb);
        deviceControls.Children.Add(adapter);
        var reset = new Button { Content = "Alle Ausgänge stoppen", Style = (Style)FindResource("Action"),
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Setzt die Analogausgänge zurück und schaltet die Digitalanschlüsse auf Eingang." };
        reset.Click += (_, _) =>
        {
            lock (model.Gate)
            {
                model.ResetOutputs();
                server.ClearElectricalIssue();
            }
            ReadBoard();
            ShowMessage("Analogausgänge zurückgesetzt. Digitalanschlüsse stehen auf Eingang.");
        };
        deviceControls.Children.Add(reset);
        side.Children.Add(new Expander { Header = "Gerätezustand", Content = deviceControls,
            Margin = new Thickness(0, 6, 0, 2), IsExpanded = true });
        side.Children.Add(Separator());
        side.Children.Add(BuildGuide());
        return new Border { Background = Theme("Card"), BorderBrush = Theme("Rule"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 12, 13, 13), Child = side,
            VerticalAlignment = VerticalAlignment.Top };
    }

    private StackPanel BuildLabViewStatus()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("LabVIEW"));
        panel.Children.Add(new TextBlock { Text = "Adresse im Feld „Physikalischer Kanal“ eingeben.",
            FontSize = 12, Foreground = Theme("Quiet"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 5) });
        var channelList = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (string address in new[] { "myDAQ1/ao0", "myDAQ1/ai0", "myDAQ1/ao1", "myDAQ1/ai1" })
        {
            channelList.Children.Add(new TextBlock { Text = address, FontFamily = new FontFamily("Consolas"),
                FontSize = 13, Margin = new Thickness(0, 1, 0, 2) });
        }
        panel.Children.Add(channelList);
        outputState.Margin = new Thickness(0, 6, 0, 2);
        outputState.FontWeight = FontWeights.SemiBold;
        lastAnalogRead.FontSize = 12;
        lastAnalogRead.Foreground = Theme("Quiet");
        panel.Children.Add(outputState);
        var stops = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 3, 0, 6) };
        for (int index = 0; index < stopAnalog.Length; index++)
        {
            int channel = index;
            Button stop = stopAnalog[channel];
            stop.Content = $"AO{channel} stoppen";
            stop.Name = $"StopAo{channel}";
            stop.Style = (Style)FindResource("Action");
            stop.Margin = channel == 0 ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            stop.Click += (_, _) =>
            {
                lock (model.Gate) model.Analog[channel] = null;
                ReadBoard();
            };
            stops.Children.Add(stop);
        }
        panel.Children.Add(stops);
        panel.Children.Add(lastAnalogRead);
        return panel;
    }

    private Expander BuildGuide()
    {
        string[] parts = ["5.1 · Aufbau und Signalweg", "5.2 · Widerstand", "5.3 · Frequenz über Zähler",
            "5.4 · Signalanalyse", "5.5 · Poti und LEDs"];
        string[] help = [
            "Finde die BNC-Buchsen und Steckbrücken im Bestückungsplan. Prüfe den Signalweg mit Funktionsgenerator und Graph in LabVIEW.",
            "Finde die Buchsen für die Widerstandsmessung und verbinde beide Enden des Prüfwiderstands. Starte die Messung in LabVIEW.",
            "Suche am Schaltbild den Weg zum Zählereingang. Wähle in LabVIEW den Zähler und die zugehörige PFI-Klemme.",
            "Erfasse einen Block am Analogeingang. Abtastrate und Auswertung legst du im eigenen LabVIEW-VI fest.",
            "Verstelle ein Poti und beobachte die Messung in LabVIEW. Die LEDs auf der Platine zeigen die Zustände an den Digitalanschlüssen."
        ];
        var content = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var select = new ComboBox { ItemsSource = parts, SelectedIndex = 0 };
        AutomationProperties.SetName(select, "Versuchsteil");
        var explanation = new TextBlock { Text = help[0], Foreground = Theme("Quiet"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        select.SelectionChanged += (_, _) => { if (select.SelectedIndex >= 0) explanation.Text = help[select.SelectedIndex]; };
        content.Children.Add(select);
        content.Children.Add(explanation);
        return new Expander { Header = "Orientierung zum Versuch", Content = content };
    }

    private void ChangeMode()
    {
        ResetSelection();
        SetPortHelp(null);
    }

    private void SetPortHelp(Socket? socket)
    {
        if (socket is null)
        {
            portTitle.Text = "Wähle einen Anschluss";
            portDescription.Text = "Klicke auf eine Buchse oder Klemme. Hier siehst du, was der Anschluss am Gerät macht.";
            instruction.Text = explore.IsChecked == true ? "Im Modus Erkunden bleibt die Verdrahtung unverändert." : IdleHelp;
            return;
        }
        portTitle.Text = socket.Id;
        portDescription.Text = Describe(socket);
        instruction.Text = explore.IsChecked == true ? "Zum Stecken wechselst du zu Verdrahten." :
            socket.Shield is null ? "Wähle jetzt den zweiten Anschluss. Esc bricht die Auswahl ab." :
            "Wähle eine zweite BNC-Buchse. Die Leitung verbindet Innenleiter und Schirm.";
    }

    private void ShowMessage(string text)
    {
        instruction.Text = text;
        (UIElementAutomationPeer.FromElement(instruction) ?? UIElementAutomationPeer.CreatePeerForElement(instruction))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Plug(Socket socket)
    {
        if (explore.IsChecked == true || selected is null)
        {
            inspected = socket.Id;
            if (explore.IsChecked != true) selected = socket.Id;
            SetPortHelp(socket);
            RedrawWires();
            return;
        }
        if (selected == socket.Id)
        {
            ResetSelection();
            SetPortHelp(null);
            return;
        }
        Socket other = Bench.Sockets[selected];
        if ((other.Shield is null) != (socket.Shield is null))
        {
            ShowMessage("Für eine BNC-Leitung brauchst du zwei BNC-Buchsen. Wähle eine andere Buchse.");
            return;
        }

        int index;
        lock (model.Gate)
        {
            if (model.Cables.Any(c => (c.A == other && c.B == socket) || (c.B == other && c.A == socket)))
            {
                ShowMessage("Diese Leitung ist bereits gesteckt.");
                return;
            }
            if (socket.Shield != null && model.Cables.Any(c => c.A == socket || c.B == socket || c.A == other || c.B == other))
            {
                ShowMessage("Eine der BNC-Buchsen ist belegt. Entferne die vorhandene Leitung oder wähle eine freie Buchse.");
                return;
            }
            model.Cables.Add(new(other, socket));
            server.ClearElectricalIssue();
            index = model.Cables.Count - 1;
        }
        selected = null;
        inspected = socket.Id;
        SetPortHelp(socket);
        RefreshCables(index);
        ReadBoard();
        ShowMessage("Leitung gesteckt. Klicke auf ein Kabel oder wähle es unter Leitungen.");
    }

    private void ResetSelection()
    {
        selected = null;
        inspected = null;
        RedrawWires();
    }

    private bool RemoveSelectedCable()
    {
        int index = cableList.SelectedIndex;
        if (index < 0) return false;
        lock (model.Gate)
        {
            model.Cables.RemoveAt(index);
            server.ClearElectricalIssue();
        }
        RefreshCables();
        ReadBoard();
        ShowMessage("Ausgewählte Leitung entfernt.");
        return true;
    }

    private void SelectCableOnBoard(int index)
    {
        selected = null;
        inspected = null;
        SetPortHelp(null);
        if (cableList.SelectedIndex == index)
            SelectCable();
        else
            cableList.SelectedIndex = index;
        cableList.ScrollIntoView(cableList.Items[index]);
        wires.Focus();
    }

    private void RefreshCables(int index = -1)
    {
        Cable[] cables;
        lock (model.Gate) cables = model.Cables.ToArray();
        refreshing = true;
        cableList.Items.Clear();
        for (int i = 0; i < cables.Length; i++)
        {
            Cable cable = cables[i];
            string condition = Faults.Single(entry => entry.Fault == cable.Fault).Label;
            var item = new DockPanel { ToolTip = $"{cable} · {condition}" };
            var marker = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = ColorOf(CableColors[i % CableColors.Length]), BorderBrush = Theme("Quiet"),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 6, 0),
                VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(marker, Dock.Left);
            item.Children.Add(marker);
            item.Children.Add(new TextBlock { Text = $"{cable}" + (cable.Fault == CableFault.Intact ? "" : $" · {condition}"),
                TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var row = new ListBoxItem { Content = item, Padding = new Thickness(3, 5, 3, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(row, $"{cable}, {condition}");
            cableList.Items.Add(row);
        }
        cableList.SelectedIndex = index;
        emptyCables.Visibility = cables.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        cableList.Visibility = cables.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        cableCount.Text = cables.Length.ToString();
        refreshing = false;
        SelectCable();
    }

    private void SelectCable()
    {
        int index = cableList.SelectedIndex;
        cableActions.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
        remove.IsEnabled = index >= 0;
        refreshing = true;
        if (index >= 0)
        {
            Cable cable;
            lock (model.Gate) cable = model.Cables[index];
            var options = Faults.Where(item => cable.A.Shield is not null ||
                item.Fault is not (CableFault.ScreenOpen or CableFault.Short)).ToArray();
            fault.ItemsSource = options.Select(item => item.Label);
            fault.SelectedIndex = Array.FindIndex(options, item => item.Fault == cable.Fault);
        }
        refreshing = false;
        RedrawWires();
    }

    private void ChangeCableFault()
    {
        int index = cableList.SelectedIndex;
        if (refreshing || index < 0 || fault.SelectedItem is not string label) return;
        CableFault next = Faults.Single(item => item.Label == label).Fault;
        lock (model.Gate)
        {
            model.Cables[index] = model.Cables[index] with { Fault = next };
            server.ClearElectricalIssue();
        }
        RefreshCables(index);
        ReadBoard();
        ShowMessage($"Leitungszustand geändert · {label}.");
    }

    private TextBlock SectionHeading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold };

    private Border Separator() => new() { Height = 1, Background = Theme("Rule"), Margin = new Thickness(0, 11, 0, 10) };

    private Brush Theme(string key) => (Brush)FindResource(key);

    private static Brush ColorOf(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static Uri PackUri(string path) => new($"/PuT3;component/{path}", UriKind.Relative);
}
