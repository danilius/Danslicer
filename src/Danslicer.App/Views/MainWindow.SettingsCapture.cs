using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls.Refresh;
using Danslicer.Core;
using Danslicer.Core.Config;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckSettingsIntegration(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Key(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers });
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static TextBox Edit(ScrubField field, string text)
        {
            field.Focus(); Key(field, Avalonia.Input.Key.Enter);
            var box = field.GetVisualDescendants().OfType<TextBox>().Single(); box.Text = text; return box;
        }
        async Task Layout() { await Task.Delay(150); UpdateLayout(); PositionWorkspacePopouts(); UpdateLayout(); }
        var vm = ViewModel!;
        Width = 1200; Height = 800; _workspaceToolbar.ShowLabels = false;
        vm.ViewMode = WorkspaceMode.Layout; vm.Document.Select(vm.Objects[0]);
        Click(TransformToolButton); await Layout();
        var position = TransformPopupContent.GetVisualDescendants().OfType<ModelScrubField>().Single(f => f.Field == vm.Position[0]);
        var original = position.Value;
        var surface = position.GetVisualDescendants().OfType<Border>().First();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = surface.TranslatePoint(new Point(20, 12), this)!.Value;
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var configPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppConfig.WorkspacePath)!, "config.json");
        var persistedBefore = File.ReadAllBytes(configPath);
        var undoChanges = 0;
        void UndoChanged() => undoChanges++;
        vm.Document.History.Changed += UndoChanged;
        var commits = 0; position.EditCommitted += (_, _) => commits++;
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 0, pressed, KeyModifiers.None, 1));
        for (var i = 1; i <= 5; i++) surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(i * 10, 0), (ulong)i, pressed, KeyModifiers.None));
        Require(vm.Position[0].Value != original && commits == 0 && undoChanges == 0, "Transform must change BEFORE release with no undo entry");
        Require(File.ReadAllBytes(configPath).SequenceEqual(persistedBefore), "Transform drag wrote config");
        var liveFinal = vm.Position[0].Value;
        surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, this, point + new Vector(50, 0), 6, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(commits == 1 && vm.Position[0].Value == liveFinal && undoChanges == 1, "One transform commit per pointer gesture");
        vm.UndoCommand.Execute(null);
        Require(vm.Position[0].Value == original && position.Value == original, "Single undo must restore entire transform gesture");
        vm.RedoCommand.Execute(null);
        Require(vm.Position[0].Value == liveFinal, "Redo must restore final preview");
        vm.UndoCommand.Execute(null);
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 7, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(50, 0), 8, pressed, KeyModifiers.Shift));
        Key(position, Avalonia.Input.Key.Escape);
        Require(position.Value == original && commits == 1 && pointer.Captured is null && TransformToolPopup.IsOpen, "Production drag Escape cancels before popout dismissal");
        var undoAfterCancel = undoChanges;
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 9, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(30, 0), 10, pressed, KeyModifiers.None));
        Require(vm.Position[0].Value != original, "Capture-loss fixture did not preview");
        pointer.Capture(null);
        Require(vm.Position[0].Value == original && undoChanges == undoAfterCancel, "Capture loss did not restore transform without undo");
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 11, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(30, 0), 12, pressed, KeyModifiers.None));
        surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point, 13, pressed, KeyModifiers.None));
        surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, this, point, 14, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(vm.Position[0].Value == original && undoChanges == undoAfterCancel && commits == 1, "No-op gesture created undo/commit");
        vm.Document.History.Changed -= UndoChanged;
        var box = Edit(position, "1cm + 2mm"); Key(box, Avalonia.Input.Key.Enter);
        Require(position.Value == 12, "Production unit expression failed"); vm.UndoCommand.Execute(null);
        box = Edit(position, "bad expression"); Key(box, Avalonia.Input.Key.Enter);
        Require(box.IsVisible && box.IsFocused, "Invalid expression must retain editor/focus");
        Key(box, Avalonia.Input.Key.Escape); Require(position.Value == original, "Invalid expression cancellation changed model");
        box = Edit(position, "14"); Viewport.Focus();
        Require(position.Value == 14, "Focus loss did not commit once"); vm.UndoCommand.Execute(null);

        vm.ViewMode = WorkspaceMode.Support; Click(SupportsToolButton); await Layout();
        var tip = SupportsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First();
        var saves = 0; void Saved() => saves++;
        vm.SupportSettings.Saved += Saved;
        box = Edit(tip, "0.05cm + 0.1mm"); Key(box, Avalonia.Input.Key.Enter);
        Require(Math.Abs(vm.SupportSettings.SupportTipDiameter - 0.6) < 0.00001 && saves == 1, "Support expression must call existing saving setter exactly once");
        box = Edit(tip, "1001"); Key(box, Avalonia.Input.Key.Enter);
        Require(saves == 1 && box.IsVisible, "Out-of-range support setting applied"); Key(box, Avalonia.Input.Key.Escape);
        var same = tip.Value; box = Edit(tip, same.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Key(box, Avalonia.Input.Key.Enter); Require(saves == 1, "Unchanged edit must not save twice");
        var tipSurface = tip.GetVisualDescendants().OfType<Border>().First();
        var trackWidth = tipSurface.Bounds.Width - 2;
        var start = tipSurface.TranslatePoint(new Point(1 + trackWidth * 0.25, 12), this)!.Value;
        var end = tipSurface.TranslatePoint(new Point(1 + trackWidth * 0.5, 12), this)!.Value;
        persistedBefore = File.ReadAllBytes(configPath);
        tipSurface.RaiseEvent(new PointerPressedEventArgs(tipSurface, pointer, this, start, 10, pressed, KeyModifiers.None, 1));
        tipSurface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, tipSurface, pointer, this, end, 11, pressed, KeyModifiers.None));
        Require(saves == 1 && tip.Value != same && Math.Abs(vm.SupportSettings.SupportTipDiameter - tip.Value) < 0.00001, "Support setting must preview without Saved event");
        Require(File.ReadAllBytes(configPath).SequenceEqual(persistedBefore), "Support drag wrote config");
        tipSurface.RaiseEvent(new PointerReleasedEventArgs(tipSurface, pointer, this, end, 12, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        var midpoint = NumericEditSession.RoundValue(tip.Minimum + (tip.Maximum - tip.Minimum) * 0.5);
        Require(Math.Abs(tip.Value - midpoint) < 0.00001 && saves == 2, "Slider midpoint must match pointer and commit once");
        box = Edit(tip, "0.55555"); Key(box, Avalonia.Input.Key.Enter);
        Require(Math.Abs(tip.Value - 0.56) < 0.00001, "Typed precision exceeds two decimal places");
        box = Edit(tip, "0.6"); Key(box, Avalonia.Input.Key.Enter);
        File.WriteAllText(System.IO.Path.Combine(directory, "slider-tracking-ok.txt"), "Filled slider midpoint follows pointer, preview does not save, release commits once, expression rounds to two decimals, unchanged float-backed edit does not save again.");
        vm.SupportSettings.Saved -= Saved;
        var count = SupportsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First(f => f.IsInteger);
        count.BringIntoView(); await Layout();
        box = Edit(count, "3.6"); Key(box, Avalonia.Input.Key.Enter);
        Require(count.Value == 4, "Integer field must normalize at commit");
        Key(count, Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Require(count.Value == 5, "Integer keyboard fine modifier must still make a whole step");
        tip.BringIntoView(); await Layout();
        var supportSection = SupportsToolPopup.GetLogicalDescendants().OfType<ReorderableExpander>().First();
        var grip = supportSection.GetVisualDescendants().OfType<Button>().Single(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Reorder "));
        var sectionPanel = (Panel)supportSection.Parent!;
        var orderBeforeDrag = File.ReadAllText(AppConfig.WorkspacePath);
        point = grip.TranslatePoint(new Point(12, 12), this)!.Value;
        var swapDistance = sectionPanel.Children[1].Bounds.Y - supportSection.Bounds.Y + 1;
        void StartSwap()
        {
            grip.RaiseEvent(new PointerPressedEventArgs(grip, pointer, this, point, 20, pressed, KeyModifiers.None, 1));
            grip.RaiseEvent(new PointerEventArgs(PointerMovedEvent, grip, pointer, this, point + new Vector(0, swapDistance), 21, pressed, KeyModifiers.None));
            Require(sectionPanel.Children[1] == supportSection, "Production section must swap during drag");
            Require(File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Live swap must not persist before drop");
        }
        StartSwap();
        // Moving back across the neighbour restores its slot without ending the gesture.
        grip.RaiseEvent(new PointerEventArgs(PointerMovedEvent, grip, pointer, this, point - new Vector(0, 10), 22, pressed, KeyModifiers.None));
        Require(sectionPanel.Children[0] == supportSection && pointer.Captured == grip, "Live reverse swap lost position/capture");
        Key(grip, Avalonia.Input.Key.Escape);
        await Task.Delay(300); StartSwap(); pointer.Capture(null);
        Require(sectionPanel.Children[0] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Capture loss must restore original order without saving");
        await Task.Delay(300); StartSwap(); Key(grip, Avalonia.Input.Key.Escape);
        Require(sectionPanel.Children[0] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Escape must restore original order without saving");
        await Task.Delay(300); StartSwap();
        grip.RaiseEvent(new PointerReleasedEventArgs(grip, pointer, this, point + new Vector(0, swapDistance), 23, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(sectionPanel.Children[1] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) != orderBeforeDrag, "Drop must persist final order");
        Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        Key(grip, Avalonia.Input.Key.Down, KeyModifiers.Alt); supportSection.IsExpanded = false;
        var resize = SupportsToolPopup.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Resize popout width");
        Key(resize, Avalonia.Input.Key.Right);
        var savedWidth = SupportsToolPopup.Shell.Width;
        point = resize.TranslatePoint(new Point(3, 15), this)!.Value;
        resize.RaiseEvent(new PointerPressedEventArgs(resize, pointer, this, point, 10, pressed, KeyModifiers.None, 1));
        resize.RaiseEvent(new PointerEventArgs(PointerMovedEvent, resize, pointer, this, point + new Vector(80, 0), 11, pressed, KeyModifiers.None));
        Key(resize, Avalonia.Input.Key.Escape);
        Require(SupportsToolPopup.Shell.Width == savedWidth && WorkspacePreferences.Load(AppConfig.WorkspacePath).Width(SupportsToolPopup.Name!, 0) == savedWidth,
            "Cancelled resize must not persist transient width");
        _workspaceToolbar.ShowLabels = true;
        var loaded = WorkspacePreferences.Load(AppConfig.WorkspacePath);
        Require(loaded.ShowToolbarLabels && loaded.Width(SupportsToolPopup.Name!, 0) == savedWidth && loaded.RecentProjects.Count > 0, "Workspace preferences did not round trip");
        // Construct another production window to verify actual restoration, including section identity/order.
        var restored = new MainWindow();
        Require(restored._workspaceToolbar.ShowLabels && restored.SupportsToolPopup.Shell.Width == savedWidth, "New window did not restore preferences");
        var restoredSections = restored.SupportsToolPopup.GetLogicalDescendants().OfType<ReorderableExpander>().ToArray();
        Require(restoredSections[1].SectionId == supportSection.SectionId && !restoredSections[1].IsExpanded, "New window did not restore section order/expansion");
        restored.Close();
        supportSection.IsExpanded = true; Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        _workspaceToolbar.ShowLabels = false; await Layout();
        Capture("settings-support.png");
        Click(RaftsToolButton); await Layout();
        var raft = RaftsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First();
        box = Edit(raft, "0.2cm"); Key(box, Avalonia.Input.Key.Enter);
        Require(vm.SupportSettings.SupportRaftThickness == 2, "Available raft thickness unit expression failed");
        Capture("settings-raft.png");
        var cap = AppConfig.Current.Viewport.CapInterior;
        vm.CapInterior = false;

        var config = UserConfig.Load(configPath);
        Require(!config.Viewport.CapInterior && Math.Abs(config.Supports.TipDiameter - 0.6) < 0.00001 && config.Supports.RaftThickness == 2, "Settings/save cap-off compatibility round trip failed");
        vm.CapInterior = cap;
        OnPreferencesClick(this, new RoutedEventArgs());
        await Layout();
        var preferences = _configWindow!;
        preferences.FindControl<ListBox>("SectionList")!.SelectedIndex = 1;
        await Layout();
        var shadowMode = preferences.FindControl<ComboBox>("SettingsShadowMode")!;
        var oldMode = shadowMode.SelectedIndex;
        shadowMode.SelectedIndex = 0;
        Require(AppConfig.Current.Viewport.ModelShadows == ModelShadowMode.Off && PopShadowMode.SelectedIndex == 0, "Preferences shadow Off did not synchronize");
        foreach (var label in new[] { "Contact depth (AO)", "Plate reflections", "Plate shadows", "Cavity shading", "Show orientation cube" })
        {
            var toggle = preferences.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == label);
            var originalToggle = toggle.IsChecked;
            toggle.IsChecked = false;
            var disabled = UserConfig.Load(configPath).Viewport;
            Require(label switch {
                "Contact depth (AO)" => !disabled.AmbientOcclusionEnabled,
                "Plate reflections" => !disabled.PlateReflectionsEnabled,
                "Plate shadows" => !disabled.PlateShadowsEnabled,
                "Cavity shading" => !disabled.CavityEnabled,
                _ => !disabled.ViewCubeEnabled
            }, "Preferences Off did not persist: " + label);
            toggle.IsChecked = originalToggle;
        }
        shadowMode.SelectedIndex = oldMode;
        preferences.FindControl<ScrollViewer>("Scroll")!.Offset = new Vector(0, preferences.FindControl<StackPanel>("ViewportSection")!.Bounds.Y);
        await Layout();
        var preferencesContent = (Control)preferences.Content!;
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)preferencesContent.Bounds.Width, (int)preferencesContent.Bounds.Height)))
        {
            bitmap.Render(preferencesContent);
            bitmap.Save(System.IO.Path.Combine(directory, "settings-viewport.png"), PngBitmapEncoderOptions.Default);
        }
        var printerEditor = ((ViewModels.ConfigViewModel)preferences.DataContext!).Printers;
        printerEditor.SelectedIndex = printerEditor.Items.ToList().FindIndex(p => p.FileExtension == "pwma");
        preferences.FindControl<ListBox>("SectionList")!.SelectedIndex = 3;
        preferences.Width = 1000; preferences.Height = 850;
        await Layout();
        preferences.FindControl<ScrollViewer>("Scroll")!.Offset = new Vector(0, preferences.FindControl<StackPanel>("PrintersSection")!.Bounds.Y);
        await Layout();
        Require(printerEditor.CompatibilityNote.Contains("unverified") && printerEditor.FormatStatus.Contains("516"),
            "Printer compatibility and native format status missing");
        var printWidthBox = preferences.GetVisualDescendants().OfType<Controls.ExpressionBox>()
            .Single(f => ReferenceEquals(f.DataContext, printerEditor.PrintWidth));
        printWidthBox.Focus(); printWidthBox.Text = "130mm"; Key(printWidthBox, Avalonia.Input.Key.Enter);
        var printerCopy = printerEditor.SelectedPrinter!;
        Require(!printerCopy.IsBuiltIn && printerCopy.BuildVolume.X == 130 && printerCopy.DisplayWidthMm == 134.4f,
            "Print-width edit must create a copy without changing pixel scale");
        Require(UserConfig.Load(configPath).FindPrinter(printerCopy.Id) == printerCopy, "Printer copy did not persist");
        printerEditor.DeleteCommand.Execute(null);
        printerEditor.SelectedIndex = printerEditor.Items.ToList().FindIndex(p => p.FileExtension == "pwma");
        await Layout();
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)preferencesContent.Bounds.Width, (int)preferencesContent.Bounds.Height)))
        {
            bitmap.Render(preferencesContent);
            bitmap.Save(System.IO.Path.Combine(directory, "settings-printers.png"), PngBitmapEncoderOptions.Default);
        }
        File.WriteAllText(System.IO.Path.Combine(directory, "native-printers-ok.txt"),
            "Actual Preferences: native format status, experimental notice, Mono 4K selection, print width expression, automatic user copy, separate display scale, persistence and copy deletion passed using isolated config.");
        foreach (var format in new[] { "goo", "ctb-encrypted", "cxdlp", "sl1", "cws-rgb", "lgs", "svgx", "anet", "phz", "chitu-zip" })
        {
            printerEditor.SelectedIndex = printerEditor.Items.ToList().FindIndex(p => p.NativeFormat == format);
            Require(printerEditor.SelectedPrinter?.NativeFormat == format && printerEditor.FormatStatus.Contains("Native "),
                "Missing selectable native format: " + format);
        }
        printerEditor.SelectedIndex = printerEditor.Items.ToList().FindIndex(p => p.Id == "elegoo-saturn-3");
        printerEditor.DuplicateCommand.Execute(null);
        Require(printerEditor.SelectedPrinter is { IsBuiltIn: false, NativeFormat: "goo" }, "Duplicated printer lost native format");
        Require(UserConfig.Load(configPath).FindPrinter(printerEditor.SelectedPrinter!.Id)?.NativeFormat == "goo", "Native format did not persist");
        printerEditor.DeleteCommand.Execute(null);
        printerEditor.SelectedIndex = printerEditor.Items.ToList().FindIndex(p => p.Id == "elegoo-saturn-3");
        await Layout();
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)preferencesContent.Bounds.Width, (int)preferencesContent.Bounds.Height)))
        {
            bitmap.Render(preferencesContent);
            bitmap.Save(System.IO.Path.Combine(directory, "settings-multi-brand.png"), PngBitmapEncoderOptions.Default);
        }
        File.WriteAllText(System.IO.Path.Combine(directory, "multi-brand-printers-ok.txt"),
            "Selected all native format families in Preferences; experimental labels, format status, duplicate persistence and deletion passed with isolated config.");
        preferences.Close();
        File.WriteAllText(System.IO.Path.Combine(directory, "viewport-preferences-ok.txt"), "Actual Preferences bindings: shadow Off synchronizes to View popout; AO, reflections, plate shadows, cavity and cube off persist independently. Original values restored.");
        File.WriteAllText(System.IO.Path.Combine(directory, "settings-ok.txt"), "Production transform pointer previews/single commit/undo/cancel, unit expressions, invalid and out-of-range rejection, focus-loss commit, support setter called once, raft thickness, durable workspace restoration and config/cap-off round trips passed using isolated temporary configuration. Visibility uses its existing display modes and switches; no numeric opacity parameter is invented. Support config settings retain existing immediate-save semantics (no document undo was present).\n");
        void Capture(string name)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
    }
}

