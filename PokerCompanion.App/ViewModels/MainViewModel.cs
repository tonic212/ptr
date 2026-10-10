using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using PokerCompanion.Core;
using PokerCompanion.App.Services;

namespace PokerCompanion.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ChartLookupService _lookup = new();
    public IReadOnlyList<Chart> Charts { get; }
    private Chart? _selectedChart;
    private string _hand = "AKs", _stack = "100", _action = "", _explanation = "";
    private bool _ante = true, _icm;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        Charts = new ChartRepository().Load();
        SelectedChart = Charts.Count > 0 ? Charts[0] : null;
    }
    public Chart? SelectedChart { get => _selectedChart; set { _selectedChart = value; Notify(); Notify(nameof(ChartImage)); Update(); } }
    public string? ChartImage => SelectedChart is null ? null : Path.Combine(AppContext.BaseDirectory, SelectedChart.ImagePath);
    public string Hand { get => _hand; set { _hand = value; Notify(); Update(); } }
    public string Stack { get => _stack; set { _stack = value; Notify(); Update(); } }
    public bool Ante { get => _ante; set { _ante = value; Notify(); Update(); } }
    public bool Icm { get => _icm; set { _icm = value; Notify(); Update(); } }
    public string Action { get => _action; private set { _action = value; Notify(); } }
    public string Explanation { get => _explanation; private set { _explanation = value; Notify(); } }
    private void Update()
    {
        if (!decimal.TryParse(Stack, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal bb) || bb <= 0)
        {
            Action = "Invalid stack"; Explanation = "Enter a positive effective stack in big blinds."; return;
        }
        ChartResult result = _lookup.Lookup(SelectedChart, Hand, bb, Ante, Icm);
        Action = result.Action; Explanation = result.Explanation;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
