using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using PokerCompanion.Core;
using PokerCompanion.Core.Recognition;
using PokerCompanion.App.Services;

namespace PokerCompanion.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ChartLookupService _lookup = new();
    public IReadOnlyList<Chart> Charts { get; }
    private Chart? _selectedChart;
    private string _hand = "", _stack = "100", _action = "", _explanation = "";
    private string _recognizedCards = "No current hand", _recognitionStatus = "Start preview to find the bottom hero cards automatically.", _coverage = "";
    private string? _lastRecognizedHand;
    private bool _ante = true, _icm, _useDetectedHand = true, _automaticHeroCards = true;
    private bool _automaticPosition = true;
    private string _scenario = "RFI", _opponent = "", _position = "", _positionStatus = "Waiting for seats and dealer button.";
    private Chart? _manualChart;
    public IReadOnlyList<string> Scenarios => PositionChartSelector.Scenarios;
    public IReadOnlyList<string> OpponentPositions => PositionChartSelector.Opponents;
    public bool ManualChartSelection => !AutomaticPosition;
    public bool AutomaticPosition
    {
        get => _automaticPosition;
        set
        {
            if (_automaticPosition == value) return;
            _automaticPosition = value; Notify(); Notify(nameof(ManualChartSelection));
            if (value) { _manualChart = SelectedChart; ResolveChart(); }
            else SelectedChart = _manualChart ?? (Charts.Count > 0 ? Charts[0] : null);
        }
    }
    public string Scenario { get => _scenario; set { _scenario = value; Notify(); ResolveChart(); } }
    public string OpponentPosition { get => _opponent; set { _opponent = value; Notify(); ResolveChart(); } }
    public string PositionStatus { get => _positionStatus; private set { _positionStatus = value; Notify(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        Charts = new ChartRepository().Load();
        _manualChart = Charts.Count > 0 ? Charts[0] : null;
        Update();
    }
    public Chart? SelectedChart { get => _selectedChart; set { _selectedChart = value; Notify(); Notify(nameof(ChartImage)); Update(); } }
    public string? ChartImage => SelectedChart is null ? null : Path.Combine(BundledDataStore.Root, SelectedChart.ImagePath);
    public string Hand { get => _hand; set { _hand = value; Notify(); Update(); } }
    public string Stack { get => _stack; set { _stack = value; Notify(); Update(); } }
    public bool Ante { get => _ante; set { _ante = value; Notify(); Update(); } }
    public bool Icm { get => _icm; set { _icm = value; Notify(); Update(); } }
    public bool UseDetectedHand
    {
        get => _useDetectedHand;
        set { _useDetectedHand = value; Notify(); if (value) Hand = _lastRecognizedHand ?? ""; Update(); }
    }
    public bool AutomaticHeroCards
    {
        get => _automaticHeroCards;
        set
        {
            if (_automaticHeroCards == value) return;
            _automaticHeroCards = value; Notify();
            ApplyObservation(new(false, "", "", value ? "Finding the bottom hero cards automatically." : "Manual card calibration selected."));
        }
    }
    public string Action { get => _action; private set { _action = value; Notify(); } }
    public string Explanation { get => _explanation; private set { _explanation = value; Notify(); } }
    public string RecognizedCards { get => _recognizedCards; private set { _recognizedCards = value; Notify(); } }
    public string RecognitionStatus { get => _recognitionStatus; private set { _recognitionStatus = value; Notify(); } }
    public string Coverage { get => _coverage; set { _coverage = value; Notify(); } }

    public void ApplyPosition(PositionObservation observation, bool staticPreview = false)
    {
        _position = observation.Success && !staticPreview ? observation.Position : "";
        PositionStatus = (staticPreview ? "Static preview: " : "") + observation.Status;
        ResolveChart();
    }
    private void ResolveChart()
    {
        if (!AutomaticPosition) return;
        SelectedChart = PositionChartSelector.Select(Charts, _position, Scenario, OpponentPosition);
    }

    public void ApplyObservation(HandObservation observation)
    {
        _lastRecognizedHand = observation.Success ? observation.HandClass : null;
        RecognizedCards = observation.Success ? $"{observation.Cards} → {observation.HandClass}" : "No current hand";
        RecognitionStatus = observation.Status;
        if (UseDetectedHand) Hand = _lastRecognizedHand ?? "";
    }
    public void ApplyStaticObservation(HandObservation observation)
    {
        ApplyObservation(new(false, "", "", "Static frame: " + observation.Status));
        if (observation.Success) RecognizedCards = $"{observation.Cards} → {observation.HandClass} (static preview)";
    }
    private void Update()
    {
        if (string.IsNullOrWhiteSpace(Hand))
        {
            Action = "No current hand";
            Explanation = UseDetectedHand ? "Waiting for reliable card recognition. Select the action scenario and effective stack; street remains manual."
                : "Enter a hand class such as AA, AKs or AKo.";
            return;
        }
        if (AutomaticPosition && SelectedChart is null)
        {
            Action = _position == "" ? "Waiting for position" : "No matching range";
            Explanation = _position == "" ? PositionStatus : $"Hero {_position}: select the action scenario and opponent position. There is no BB opening chart. No range is substituted when the scenario is unavailable.";
            return;
        }
        if (!decimal.TryParse(Stack, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal bb) || bb <= 0)
        { Action = "Invalid stack"; Explanation = "Enter a positive effective stack in big blinds."; return; }
        ChartResult result = _lookup.Lookup(SelectedChart, Hand, bb, Ante, Icm);
        Action = result.Action; Explanation = result.Explanation;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
