using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.App.ViewModels;

/// <summary>Editable view of the effective window keymap.</summary>
public sealed class KeymapViewModel : ViewModelBase
{
    private readonly UserConfig _config;
    private KeymapBindingViewModel? _capturing;
    private string _searchText = "";
    private string _validationMessage = "";

    public KeymapViewModel(UserConfig config)
    {
        _config = config;
        Bindings = WindowKeymap.Actions
            .Select(action => new KeymapBindingViewModel(this, action))
            .ToArray();
        ResetAllCommand = new RelayCommand(ResetAll, () => _config.KeymapOverrides.Count > 0);
    }

    public IReadOnlyList<KeymapBindingViewModel> Bindings { get; }
    public IRelayCommand ResetAllCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? "";
            if (_searchText == normalized) return;
            _searchText = normalized;
            OnPropertyChanged();
            foreach (var binding in Bindings) binding.RefreshVisibility();
            OnPropertyChanged(nameof(HasVisibleBindings));
        }
    }

    public bool HasVisibleBindings => Bindings.Any(binding => binding.IsVisible);

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (_validationMessage == value) return;
            _validationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationMessage));
        }
    }

    public bool HasValidationMessage => ValidationMessage.Length > 0;
    public bool IsCapturing => _capturing is not null;

    public event Action? Changed;

    internal void BeginCapture(KeymapBindingViewModel binding)
    {
        if (_capturing == binding)
        {
            CancelCapture();
            return;
        }
        _capturing?.SetCapturing(false);
        _capturing = binding;
        binding.SetCapturing(true);
        ValidationMessage = "Press a shortcut. Escape cancels; modifier-only keys are ignored.";
        OnPropertyChanged(nameof(IsCapturing));
    }

    public void CancelCapture()
    {
        if (_capturing is null) return;
        _capturing.SetCapturing(false);
        _capturing = null;
        ValidationMessage = "";
        OnPropertyChanged(nameof(IsCapturing));
    }

    public bool Capture(KeyGesture gesture)
    {
        if (_capturing is null) return false;
        var target = _capturing;
        if (!WindowKeymap.TrySetGesture(_config, target.Action.Id, gesture, out var conflict))
        {
            ValidationMessage = $"{WindowKeymap.Format(gesture)} is already bound to {conflict!.DisplayName}.";
            return false;
        }

        target.SetCapturing(false);
        _capturing = null;
        ValidationMessage = "";
        PersistAndRefresh();
        OnPropertyChanged(nameof(IsCapturing));
        return true;
    }

    internal void Reset(KeymapBindingViewModel binding)
    {
        WindowKeymap.Reset(_config, binding.Action.Id);
        if (_capturing == binding)
        {
            _capturing = null;
            OnPropertyChanged(nameof(IsCapturing));
        }
        ValidationMessage = "";
        PersistAndRefresh();
    }

    private void ResetAll()
    {
        WindowKeymap.ResetAll(_config);
        _capturing?.SetCapturing(false);
        _capturing = null;
        ValidationMessage = "";
        PersistAndRefresh();
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void PersistAndRefresh()
    {
        AppConfig.Save();
        foreach (var binding in Bindings) binding.Refresh();
        ResetAllCommand.NotifyCanExecuteChanged();
        Changed?.Invoke();
    }

    internal bool MatchesSearch(WindowKeymapAction action)
    {
        var query = SearchText.Trim();
        return query.Length == 0 ||
               action.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               action.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               WindowKeymap.GetGestureText(_config, action.Id)
                   .Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    internal bool IsModified(string actionId) => _config.KeymapOverrides.ContainsKey(actionId);
    internal string GestureText(string actionId) => WindowKeymap.GetGestureText(_config, actionId);
}

public sealed class KeymapBindingViewModel : ViewModelBase
{
    private readonly KeymapViewModel _owner;
    private bool _isCapturing;
    private bool _isVisible = true;

    internal KeymapBindingViewModel(KeymapViewModel owner, WindowKeymapAction action)
    {
        _owner = owner;
        Action = action;
        BeginCaptureCommand = new RelayCommand(() => _owner.BeginCapture(this));
        ResetCommand = new RelayCommand(() => _owner.Reset(this), () => IsModified);
    }

    public WindowKeymapAction Action { get; }
    public string ActionName => Action.DisplayName;
    public string ActionId => Action.Id;
    public string Gesture => _owner.GestureText(Action.Id);
    public string CaptureButtonText => IsCapturing ? "Press a key…" : Gesture;
    public bool IsModified => _owner.IsModified(Action.Id);
    public IRelayCommand BeginCaptureCommand { get; }
    public IRelayCommand ResetCommand { get; }

    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (_isCapturing == value) return;
            _isCapturing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptureButtonText));
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    internal void SetCapturing(bool value) => IsCapturing = value;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Gesture));
        OnPropertyChanged(nameof(CaptureButtonText));
        OnPropertyChanged(nameof(IsModified));
        ResetCommand.NotifyCanExecuteChanged();
        RefreshVisibility();
    }

    internal void RefreshVisibility() => IsVisible = _owner.MatchesSearch(Action);
}
