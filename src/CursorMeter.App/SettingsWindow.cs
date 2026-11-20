using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CursorMeter.App.Windows;

namespace CursorMeter.App;

/// <summary>
/// What the settings panel needs from the rest of the app: the switches that live outside
/// <c>settings.txt</c> (starting with the session, hiding the floating bars) and the floating position.
/// </summary>
internal sealed record SettingsHooks(MenuEntry? Startup, MenuEntry? FloatingHidden, Action? ResetFloatingPosition);

/// <summary>
/// The settings panel, laid out like Start11's: sections on the left, one card per setting on the
/// right and a live preview of the bars on top. Every change is saved and applied at once.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly IBrush CardFill = Solid("#222222");
    private static readonly IBrush CardEdge = Solid("#33FFFFFF");
    private static readonly IBrush TitleText = Solid("#ECEAE2");
    private static readonly IBrush SecondaryText = Solid("#B0ACA0");

    private readonly SettingsHooks _hooks;
    private readonly bool _bars = OperatingSystem.IsWindows();
    private readonly ListBox _sections = new() { Width = 190, Background = Brushes.Transparent };
    private readonly ScrollViewer _page = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Border _previewBox = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 14), Margin = new Thickness(0, 0, 0, 12) };
    private readonly QuotaBars _preview = new()
    {
        Width = TaskbarLayout.PreferredWidth,
        Height = TaskbarLayout.PreferredHeight,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly List<(string Name, bool Preview, Func<Control> Build)> _pages = [];

    public SettingsWindow(SettingsHooks hooks)
    {
        _hooks = hooks;
        Title = "Definições do CursorMeter";
        Icon = AppIcon.Create();
        Width = 860;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Solid("#181818");
        FontSize = 13;

        if (_bars)
        {
            _pages.Add(("Aspeto", true, AppearancePage));
            _pages.Add(("Cores", true, ColorsPage));
            _pages.Add(("Utilização e alertas", true, UsagePage));
            _pages.Add(("Barras flutuantes", true, FloatingPage));
        }
        else
        {
            _pages.Add(("Aspeto", false, PanelAppearancePage));
            _pages.Add(("Utilização", false, UsagePage));
        }

        _pages.Add(("Geral", false, GeneralPage));

        foreach (var (name, _, _) in _pages)
            _sections.Items.Add(new ListBoxItem { Content = name, Padding = new Thickness(12, 9) });
        _sections.SelectionChanged += (_, _) => ShowPage();

        _previewBox.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#3B5B8F"), 0),
                new GradientStop(Color.Parse("#7A4E8C"), 0.55),
                new GradientStop(Color.Parse("#D98A5B"), 1),
            ],
        };
        _previewBox.Child = _preview;

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(8, 20, 20, 16) };
        Grid.SetRow(_previewBox, 0);
        Grid.SetRow(_page, 1);
        right.Children.Add(_previewBox);
        right.Children.Add(_page);

        var title = new TextBlock
        {
            Text = "Definições",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = TitleText,
            Margin = new Thickness(20, 20, 0, 14),
        };
        var left = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(title, Dock.Top);
        left.Children.Add(title);
        left.Children.Add(_sections);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        layout.Children.Add(left);
        layout.Children.Add(right);
        Content = layout;

        _sections.SelectedIndex = 0;
        Opened += (_, _) => UpdatePreview();
    }

    private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static AppSettings Settings => SettingsStore.Current;

    private static void Change(Func<AppSettings, AppSettings> change)
    {
        SettingsStore.Update(change);
    }

    private void ShowPage()
    {
        int index = Math.Max(0, _sections.SelectedIndex);
        var (_, preview, build) = _pages[index];
        _previewBox.IsVisible = preview;
        _page.Content = build();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var settings = Settings;
        var frame = BarFrame.Resolve(settings, null, false, RenderScaling);
        _preview.ApplyStyle(BarStyle.From(settings));
        _preview.ShowFrame(frame.FillBrush, frame.OutlinePen, frame.Radius, frame.Margin, frame.Inset);
        _preview.ShowQuotas(new UsageQuotas(42, 78, null));
    }

    // ---- pages -----------------------------------------------------------------------------

    private Control AppearancePage()
    {
        var theme = Combo(
            ["Automático", "Escuro", "Transparente", "Seguir o Start11"],
            (int)Settings.Theme,
            index => Change(s => s with { Theme = (BarTheme)index }));

        var opacity = Slide(0, 100, Settings.BackgroundOpacity, "{0}%", value => Change(s => s with { BackgroundOpacity = value }));
        var opacityCard = Card(
            "Opacidade do fundo",
            "Quanto o fundo deixa ver o que está atrás. O tema Transparente ignora este valor.",
            opacity);

        var outline = Toggle(Settings.Outline, on => Change(s => s with { Outline = on }));

        var radius = Slide(0, 16, Settings.Radius >= 0 ? Settings.Radius : 6, "{0}", value => Change(s => s with { Radius = value }));
        var radiusSlider = (Slider)radius.Children[0];
        var automatic = Toggle(Settings.Radius < 0, on => Change(s => s with { Radius = on ? -1 : (int)radiusSlider.Value }));
        radius.IsEnabled = Settings.Radius >= 0;
        automatic.IsCheckedChanged += (_, _) => radius.IsEnabled = automatic.IsChecked != true;

        var scale = Slide(80, 110, Settings.TextScale, "{0}%", value => Change(s => s with { TextScale = value }));

        var pools = Combo(
            ["As duas", "Só Cursor Models", "Só Other Models"],
            (int)Settings.Pools,
            index => Change(s => s with { Pools = (PoolView)index }));

        var remaining = Toggle(Settings.ShowRemaining, on => Change(s => s with { ShowRemaining = on }));

        return Page(
            Note("Valem para as barras fora do modo ilha: a caixa junto ao relógio e as barras flutuantes. Quando são uma ilha da barra de tarefas, seguem sempre as cores do Start11."),
            Card("Tema", "Automático: caixa escura, ou o aspeto do Start11 quando as barras flutuam. Transparente: sem fundo. Seguir o Start11: integradas na barra, sem caixa, e com o aspeto das ilhas quando flutuam.", theme, IslandsBadge()),
            opacityCard,
            Card("Contorno", "Linha à volta da caixa. A cor escolhe-se em Cores.", outline),
            Card("Cantos automáticos", "Seguem o Start11 quando as barras flutuam e são retos na caixa junto ao relógio.", automatic),
            Card("Raio dos cantos", "Em pixels. Só vale com os cantos automáticos desligados.", radius),
            Card("Tamanho do texto", "Em relação ao tamanho normal.", scale),
            Card("Mostrar", "As duas pools, ou só uma.", pools),
            Card("Mostrar o que resta", "A percentagem e a barra passam a mostrar o que ainda tens disponível, em vez do que usaste.", remaining));
    }

    /// <summary>Where there are no bars of ours to preview: how the GNOME Shell extension draws them in the taskbar.</summary>
    private Control PanelAppearancePage()
    {
        var scale = Slide(80, 110, Settings.TextScale, "{0}%", value => Change(s => s with { TextScale = value }));
        var spacing = Slide(0, 16, Settings.BarSpacing, "{0} px", value => Change(s => s with { BarSpacing = value }));

        return Page(
            Note("Vale para as barras que a extensão do GNOME Shell desenha na barra de tarefas do Zorin."),
            Card("Tamanho do texto", "Em relação ao tamanho normal. As barras seguem a altura da barra de tarefas.", scale),
            Card("Espaço entre as barras", "Em pixels. Se a barra de tarefas for baixa, o espaço encolhe para as duas barras caberem.", spacing),
            ColorCard("Cor do texto", "Os nomes das pools e as percentagens. Vazio é a cor original.", s => s.TextColor, (s, c) => s with { TextColor = c }, Palette.Text));
    }

    private Control ColorsPage()
    {
        return Page(
            Note("Vazio é a cor original. O texto num fundo transparente lê-se melhor com uma cor clara ou escura conforme a tua barra de tarefas."),
            ColorCard("Texto", "Os nomes das pools e as percentagens.", s => s.TextColor, (s, c) => s with { TextColor = c }, Palette.Text),
            ColorCard("Texto secundário", "As mensagens de estado, como \"A ler…\" ou \"Abre o Cursor\".", s => s.MutedColor, (s, c) => s with { MutedColor = c }, Palette.Muted),
            ColorCard("Cursor Models", "A barra do pool Cursor.", s => s.CursorColor, (s, c) => s with { CursorColor = c }, Palette.CursorPool),
            ColorCard("Other Models", "A barra do pool Other.", s => s.OtherColor, (s, c) => s with { OtherColor = c }, Palette.OtherPool),
            ColorCard("Aviso", "A barra quando passa do limiar de aviso.", s => s.WarnColor, (s, c) => s with { WarnColor = c }, Palette.Warn),
            ColorCard("Crítico", "A barra e o texto quando passa do limiar crítico.", s => s.HotColor, (s, c) => s with { HotColor = c }, Palette.Hot),
            ColorCard("Contorno", "A linha à volta da caixa, quando o contorno está ligado.", s => s.OutlineColor, (s, c) => s with { OutlineColor = c }, Palette.Border),
            ResetButton("Repor as cores", () => Change(s => s with
            {
                TextColor = null, MutedColor = null, CursorColor = null, OtherColor = null,
                WarnColor = null, HotColor = null, OutlineColor = null,
            })));
    }

    private Control UsagePage()
    {
        var refresh = Combo(
            ["De 1 em 1 minuto", "De 3 em 3 minutos", "De 5 em 5 minutos", "De 10 em 10 minutos"],
            Array.IndexOf(RefreshChoices, Settings.RefreshMinutes) is var found and >= 0 ? found : 1,
            index => Change(s => s with { RefreshMinutes = RefreshChoices[index] }));

        var cards = new List<Control>
        {
            Card("Atualizar a utilização", "Com que frequência a app lê a quota ao Cursor. Também podes atualizar a partir do menu.", refresh),
        };
        if (_bars)
        {
            cards.Add(Card("Aviso a partir de", "A barra muda para a cor de aviso quando a utilização chega a este valor.",
                Number(1, 98, Settings.WarnAt, value => Change(s => s with { WarnAt = value }))));
            cards.Add(Card("Crítico a partir de", "A barra e o texto ficam na cor crítica a partir deste valor.",
                Number(2, 100, Settings.HotAt, value => Change(s => s with { HotAt = value }))));
        }

        return Page(cards.ToArray());
    }

    private Control FloatingPage()
    {
        // Hidden means the user turned the floating bars off. The other choices only apply while they are on.
        bool on = _hooks.FloatingHidden?.IsChecked?.Invoke() != true;

        var faded = Slide(10, 90, Settings.FadedOpacity, "{0}%", value => Change(s => s with { FadedOpacity = value }));
        var opacity = Card("Opacidade em repouso", "Quanto se veem as barras flutuantes enquanto o rato não está por cima.", faded);
        opacity.IsEnabled = on;

        Border? position = null;
        if (_hooks.ResetFloatingPosition is { } reset)
        {
            var button = new Button { Content = "Repor posição", MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => reset();
            position = Card("Posição", "Volta a pôr as barras flutuantes no canto, imediatamente acima da barra de tarefas.", button);
            position.IsEnabled = on;
        }

        var cards = new List<Control>();
        if (_hooks.FloatingHidden is { } hidden)
        {
            var toggle = FloatingToggle(hidden, enabled =>
            {
                opacity.IsEnabled = enabled;
                if (position is not null)
                    position.IsEnabled = enabled;
            });
            cards.Add(Card(
                "Barras flutuantes",
                "Quando a ilha das janelas ocupa o espaço, as barras ficam imediatamente acima da barra de tarefas.",
                toggle));
        }

        cards.Add(opacity);
        if (position is not null)
            cards.Add(position);
        return Page(cards.ToArray());
    }

    /// <summary>
    /// The floating-bars switch. The menu entry is "hide", so the switch is on when they are not hidden.
    /// </summary>
    private static ToggleSwitch FloatingToggle(MenuEntry hidden, Action<bool> changed)
    {
        bool Shown() => hidden.IsChecked?.Invoke() != true;
        var toggle = new ToggleSwitch { IsChecked = Shown(), OnContent = "Ligado", OffContent = "Desligado" };
        toggle.IsCheckedChanged += (_, _) =>
        {
            bool wanted = toggle.IsChecked == true;
            if (Shown() != wanted)
                hidden.Run();
            bool actual = Shown();
            if (actual != wanted)
                toggle.IsChecked = actual;
            changed(actual);
        };
        return toggle;
    }

    private Control GeneralPage()
    {
        var cards = new List<Control>();
        if (_hooks.Startup is { } startup)
            cards.Add(Card(startup.Label, "Abre o CursorMeter quando inicias sessão.", EntryToggle(startup)));
        if (_bars)
        {
            cards.Add(Card(
                "Esconder em ecrã inteiro",
                "Esconde as barras enquanto um jogo, um vídeo ou outra aplicação ocupa o ecrã todo e a barra de tarefas desaparece. Voltam quando a aplicação sai do ecrã inteiro.",
                Toggle(Settings.HideInFullscreen, on => Change(s => s with { HideInFullscreen = on }))));
        }
        cards.Add(Card("Ficheiro das definições", SettingsStore.FilePath, new TextBlock()));
        cards.Add(ResetButton("Repor todas as definições", () => SettingsStore.Reset()));
        return Page(cards.ToArray());
    }

    // ---- building blocks -------------------------------------------------------------------

    private static readonly int[] RefreshChoices = [1, 3, 5, 10];

    private static Control Page(params Control[] cards)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 12, 0) };
        foreach (var card in cards)
            panel.Children.Add(card);
        return panel;
    }

    /// <summary>
    /// A badge under a card saying the setting above only shows with another Start11 configuration.
    /// Start11 can be changed while this window is open, so it is checked again when the window is activated.
    /// </summary>
    private Control IslandsBadge()
    {
        var icon = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Solid("#4C9AFF"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "i",
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = Solid("#0B1F36"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = "O Start11 está no estilo Ilhas", Foreground = TitleText, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = "Aqui o tema só se vê quando as barras flutuam. Para o ver sempre, muda o estilo da barra de tarefas no Start11 para Arredondado ou Sólido.",
            Foreground = SecondaryText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(icon);
        row.Children.Add(text);
        text.MaxWidth = 520;

        var badge = new Border
        {
            Background = Solid("#1B2B3F"),
            BorderBrush = Solid("#334C6E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = row,
            IsVisible = Start11Theme.IslandsStyle(),
        };
        EventHandler refresh = (_, _) => badge.IsVisible = Start11Theme.IslandsStyle();
        Activated += refresh;
        badge.DetachedFromVisualTree += (_, _) => Activated -= refresh;
        return badge;
    }

    private static Control Note(string text) => new TextBlock
    {
        Text = text,
        Foreground = SecondaryText,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 0, 8),
    };

    /// <param name="footer">Shown inside the card, under the title and the control, across its full width.</param>
    private static Border Card(string title, string? description, Control control, Control? footer = null)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, Foreground = TitleText, FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(new TextBlock { Text = description, Foreground = SecondaryText, TextWrapping = TextWrapping.Wrap, FontSize = 12 });

        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(18, 0, 0, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        if (footer is not null)
        {
            grid.RowDefinitions = new RowDefinitions("Auto,Auto");
            footer.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(footer, 1);
            Grid.SetColumnSpan(footer, 2);
            grid.Children.Add(footer);
        }

        return new Border
        {
            Background = CardFill,
            BorderBrush = CardEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12),
            Child = grid,
        };
    }

    private static ComboBox Combo(string[] options, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Width = 210, ItemsSource = options, SelectedIndex = Math.Clamp(selected, 0, options.Length - 1) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
                changed(combo.SelectedIndex);
        };
        return combo;
    }

    private static ToggleSwitch Toggle(bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = on, OnContent = "Ligado", OffContent = "Desligado" };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    /// <summary>A switch for something kept outside the settings file, such as the startup shortcut.</summary>
    private static ToggleSwitch EntryToggle(MenuEntry entry)
    {
        var toggle = new ToggleSwitch { IsChecked = entry.IsChecked?.Invoke() == true, OnContent = "Ligado", OffContent = "Desligado" };
        toggle.IsCheckedChanged += (_, _) =>
        {
            bool wanted = toggle.IsChecked == true;
            if ((entry.IsChecked?.Invoke() == true) != wanted)
                entry.Run();
            bool actual = entry.IsChecked?.Invoke() == true;
            if (actual != wanted)
                toggle.IsChecked = actual;
        };
        return toggle;
    }

    private static StackPanel Slide(int min, int max, int value, string format, Action<int> changed)
    {
        var label = new TextBlock
        {
            Width = 46,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryText,
            Text = string.Format(CultureInfo.CurrentCulture, format, value),
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 200,
            SmallChange = 1,
            LargeChange = 5,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            int next = (int)Math.Round(e.NewValue);
            label.Text = string.Format(CultureInfo.CurrentCulture, format, next);
            changed(next);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(slider);
        row.Children.Add(label);
        return row;
    }

    private static NumericUpDown Number(int min, int max, int value, Action<int> changed)
    {
        var box = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Increment = 1,
            FormatString = "0",
            Width = 130,
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } number)
                changed((int)number);
        };
        return box;
    }

    private Border ColorCard(
        string title,
        string description,
        Func<AppSettings, string?> get,
        Func<AppSettings, string?, AppSettings> set,
        IImmutableSolidColorBrush fallback)
        => Card(title, description, ColorField(get, set, fallback.Color));

    private Control ColorField(Func<AppSettings, string?> get, Func<AppSettings, string?, AppSettings> set, Color fallback)
    {
        string fallbackHex = Hex(fallback);
        var current = get(Settings);
        var syncing = false;

        var swatch = new Button
        {
            Width = 34,
            Height = 26,
            Padding = new Thickness(0),
            BorderBrush = CardEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        var box = new TextBox { Width = 100, Watermark = fallbackHex, MaxLength = 7, Text = current ?? "" };
        var clear = new Button { Content = "Repor", MinWidth = 64, HorizontalContentAlignment = HorizontalAlignment.Center };
        var picker = new ColorView { IsAlphaEnabled = false, IsAlphaVisible = false };
        swatch.Flyout = new Flyout { Content = picker };

        void Show(string? hex)
        {
            var color = BarStyle.ColorOf(hex) ?? fallback;
            swatch.Background = new SolidColorBrush(color);
            syncing = true;
            picker.Color = color;
            box.Text = hex ?? "";
            syncing = false;
        }

        void Save(string? hex)
        {
            Change(s => set(s, hex));
            UpdatePreview();
        }

        picker.ColorChanged += (_, e) =>
        {
            if (syncing)
                return;
            string hex = Hex(e.NewColor);
            swatch.Background = new SolidColorBrush(Color.FromRgb(e.NewColor.R, e.NewColor.G, e.NewColor.B));
            syncing = true;
            box.Text = hex;
            syncing = false;
            Save(hex);
        };
        box.TextChanged += (_, _) =>
        {
            if (syncing)
                return;
            var text = box.Text ?? "";
            if (text.Length == 0)
            {
                Show(null);
                Save(null);
            }
            else if (AppSettings.NormalizeColor(text) is { } hex)
            {
                var color = BarStyle.ColorOf(hex) ?? fallback;
                swatch.Background = new SolidColorBrush(color);
                syncing = true;
                picker.Color = color;
                syncing = false;
                Save(hex);
            }
        };
        clear.Click += (_, _) =>
        {
            Show(null);
            Save(null);
        };

        Show(current);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(box);
        row.Children.Add(clear);
        return row;
    }

    private Control ResetButton(string text, Action reset)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            reset();
            ShowPage();
        };
        return button;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
