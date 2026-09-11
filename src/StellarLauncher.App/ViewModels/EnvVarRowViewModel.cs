using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class EnvVarRowViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Action<EnvVarRowViewModel> _onRemove;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _value;

    public EnvVarRowViewModel(EnvVar seed, Action onChanged, Action<EnvVarRowViewModel> onRemove)
    {
        _name = seed.Name;
        _value = seed.Value;
        _onChanged = onChanged;
        _onRemove = onRemove;
    }

    public EnvVar ToModel() => new() { Name = Name, Value = Value };

    partial void OnNameChanged(string value) => _onChanged();
    partial void OnValueChanged(string value) => _onChanged();

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
