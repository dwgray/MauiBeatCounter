using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MauiBeatCounter.ViewModel;

public enum ClickState { Initial, FirstClick, Counting, Done }

public enum Meter { Beat = 1, Double, Waltz, Common }

public struct MeterOption
{
    private readonly Meter _meter;
    private readonly string _name;

    public MeterOption(Meter meter, string name)
    {
        _meter = meter;
        _name = name;
    }

    public Meter Meter => _meter;
    public string Name => _name;
}

public enum CountMethod { Beat, Measure }

public struct CountOption
{
    private readonly CountMethod _method;
    private readonly string _name;

    public CountOption(CountMethod method, string name)
    {
        _method = method;
        _name = name;
    }

    public CountMethod Method => _method;
    public string Name => _name;
}


public partial class CounterViewModel : ObservableObject
{
    const int MaxWait = 5000;

    public CounterViewModel()
    {
        _state = ClickState.Initial;

        MeterOptions = new List<MeterOption>(new[] {
            new MeterOption(Meter.Beat, "Beat"),
            new MeterOption(Meter.Double, "2/4"),
            new MeterOption(Meter.Waltz, "3/4"),
            new MeterOption(Meter.Common, "4/4")
        });

        CurrentMeter = MeterOptions[3];

        MethodOptions = new List<CountOption>(new[]
        {
            new CountOption(CountMethod.Beat, "Beat"),
            new CountOption(CountMethod.Measure, "Measure"),
        });

        CurrentMethod = MethodOptions[1];
    }

    public Meter Meter => CurrentMeter.Meter;
    public int Numerator => (int)Meter;

    public IReadOnlyList<MeterOption> MeterOptions { get; private set; }

    private MeterOption _currentMeter;

    public MeterOption CurrentMeter
    {
        get => _currentMeter;
        set
        {
            var oldMeter = _currentMeter.Meter;
            if (SetProperty(ref _currentMeter, value))
            {
                ConvertIntervals(oldMeter, value.Meter);
                OnPropertyChanged(nameof(Meter));
                OnPropertyChanged(nameof(Method));
                OnPropertyChanged(nameof(Numerator));
                OnPropertyChanged(nameof(Mpm));
                OnPropertyChanged(nameof(ClickLabel));
                OnPropertyChanged(nameof(ShowMeasures));
            }
        }
    }

    public bool ShowMeasures => Meter != Meter.Beat;

    public CountMethod Method => Meter == Meter.Beat ? CountMethod.Beat : CurrentMethod.Method;

    public IReadOnlyList<CountOption> MethodOptions { get; private set; }

    private CountOption _currentMethod;

    public CountOption CurrentMethod
    {
        get => _currentMethod;
        set
        {
            if (SetProperty(ref _currentMethod, value))
            {
                switch (value.Method)
                {
                    case CountMethod.Beat:
                        ConvertIntervals(Meter, Meter.Beat);
                        break;
                    case CountMethod.Measure:
                        ConvertIntervals(Meter.Beat, Meter);
                        break;
                }
                OnPropertyChanged(nameof(Method));
                OnPropertyChanged(nameof(Mpm));
                OnPropertyChanged(nameof(ClickLabel));
            }
        }
    }

    [ObservableProperty]
    private ClickState _state;

    private long _lastClick;
    private readonly List<int> _intervals = new();
    private Timer _timeout;

    [RelayCommand]
    private void CounterClick()
    {
        int now = (int)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
        _timeout?.Dispose();

        switch (State)
        {
            case ClickState.Initial:
            case ClickState.Done:
                _intervals.Clear();
                _lastClick = now;
                State = ClickState.Counting;
                break;
            case ClickState.FirstClick:
            case ClickState.Counting:
                State = ClickState.Counting;
                int delta = (int)(now - _lastClick);
                _lastClick = now;
                _intervals.Add(delta);
                if (_intervals.Count > 10)
                {
                    _intervals.RemoveAt(0);
                }
                break;
        }

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Mpm));
        OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(ClickLabel));
        _timeout = new Timer(OnTimeout, null, MaxWait, Timeout.Infinite);
    }

    private void OnTimeout(Object _)
    {
        switch (State)
        {
            case ClickState.Initial:
            case ClickState.FirstClick:
                State = ClickState.Initial;
                _intervals.Clear();
                _lastClick = 0;
                break;
            case ClickState.Counting:
            case ClickState.Done:
                State = ClickState.Done;
                break;
        }

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Mpm));
        OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(ClickLabel));
    }

    private double Cpm {
        get
        {
            if (!_intervals.Any())
            {
                return 0;
            }
            var avg = _intervals.Average();
            return (60 * 1000) / avg;

        }
    }

    public double Mpm { 
        get
        {
            switch (Method)
            {
                case CountMethod.Beat:
                    return Cpm / (int)Meter;
                case CountMethod.Measure:
                    return Cpm;
                default:
                    throw new NotImplementedException();
            }
        }
    }

    public double Bpm
    {
        get
        {
            switch (Method)
            {
                case CountMethod.Beat:
                    return Cpm;
                case CountMethod.Measure:
                    return Cpm * (int)Meter;
                default:
                    throw new NotImplementedException();
            }
        }
    }

    public string ClickLabel
    {
        get
        {
            switch (State)
            {
                case ClickState.Initial:
                case ClickState.Done:
                    switch (Method)
                    {
                        case CountMethod.Beat:
                            return "Click on each beat";
                        default:
                            return $"Click on downbeat of {(int)Meter}/4 measure";
                    }
                case ClickState.FirstClick:
                case ClickState.Counting:
                    return "Again";
                default:
                    throw new NotImplementedException();
            }
        }
    }
    // Convert intervals to the new meter, keeping bmp constant
    private void ConvertIntervals(Meter oldMeter, Meter newMeter)
    {
        for (var i = 0; i < _intervals.Count; i++)
        {
            var beat = _intervals[i] / (int)oldMeter;
            _intervals[i] = beat * (int)newMeter;
        }
    }

}
