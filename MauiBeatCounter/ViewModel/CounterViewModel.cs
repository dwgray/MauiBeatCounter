using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MauiBeatCounter.ViewModel;

/*
 * I'm managing state and handling "business logic" such as it is in a ViemModel class - this won't scale
 *  beyond a fairly simple application, but seems like a reasonably clean solution for this very small application
 * 
 *  I'm making heavy use of custom setters and getters to supply a public interface on top of private state.
 * 
 *  The public interface is documented inline
 * 
 *  The internal state for the application is as follows
 *    - User setabble options - the actual variables are record that include the enum value and human readable name
 *      - _currentMethod - Does the user want to count by beats of by measures
 *      - _currentMeter - beat (no meter), 2/4 (double), 3/4 (waltz) or 4/4 (common)
 *    - Internal state
 *      -  _state - This is a simple state machine to track whether the user has started counting, etc.
 *          this is used to manage the rest of the internal state and helps to compute the title of the click button
 *      - _lastClick - Timestamp of the last click
 *      - _intervals - the last 10 intervals between click s in ticks (which may be rescaled based on meter/method)
 *      - Cpm - counts per minute computed from _intervals
 */

/*
 * The states for the core state machine
 *  - initial = no data (initial state or timer reset without getting past firstClick state)
 *  - firstClick = the user has clicked once after inital/done state
 *  - counting = second - infinite continuous clicking without ever pausing for _maxTime
 *  - done = user has paused for _maxtime after clicking at least twice 
 */
public enum ClickState { Initial, FirstClick, Counting, Done }

/*
 * Defines the possible meters - this was a bit of a stretch, since I was looking for
 * single word synonyms for 2/4, 3/4 and 4/4.  "none" is included to make the rest of the
 * values indices line up with their beats per measure.  "beat" isn't really a meter, but
 * an indiciation that the user just wants to count beats and not worry about meter
 */

public enum Meter { Beat = 1, Double, Waltz, Common }

/*
 * By defining a record for MeterOption, we can define the options and bind to the
 * XAML picker to structure, which allows us to define the human readable names
 */
public readonly record struct MeterOption(Meter meter, string name);

/*
 * Simple enum to define whether the user wants to click once per beat or once per measure
 */
public enum CountMethod { Beat, Measure }

public readonly record struct CountOption(CountMethod method, string name);

public partial class CounterViewModel : ObservableObject
{
    const int MaxWait = 5000;

    public CounterViewModel()
    {
        _state = ClickState.Initial;

        MeterOptions = new List<MeterOption>(new MeterOption[] {
            new (Meter.Beat, "Beat"),
            new (Meter.Double, "2/4"),
            new (Meter.Waltz, "3/4"),
            new (Meter.Common, "4/4")
        });

        CurrentMeter = MeterOptions[3];

        MethodOptions = new List<CountOption>(new CountOption[]
        {
            new (CountMethod.Beat, "Beat"),
            new (CountMethod.Measure, "Measure"),
        });

        CurrentMethod = MethodOptions[1];
    }

    public Meter Meter => CurrentMeter.meter;
    public int Numerator => (int)Meter;

    public IReadOnlyList<MeterOption> MeterOptions { get; private set; }

    private MeterOption _currentMeter;

    public MeterOption CurrentMeter
    {
        get => _currentMeter;
        set
        {
            var oldMeter = _currentMeter.meter;
            if (SetProperty(ref _currentMeter, value))
            {
                ConvertIntervals(oldMeter, value.meter);
                OnPropertyChanged(nameof(Method));
                OnPropertyChanged(nameof(Numerator));
                OnPropertyChanged(nameof(ShowMeasures));
                OnPropertyChanged(nameof(ClickLabel));
            }
        }
    }

    public bool ShowMeasures => Meter != Meter.Beat;

    public CountMethod Method => Meter == Meter.Beat ? CountMethod.Beat : CurrentMethod.method;

    public IReadOnlyList<CountOption> MethodOptions { get; private set; }

    private CountOption _currentMethod;

    public CountOption CurrentMethod
    {
        get => _currentMethod;
        set
        {
            if (SetProperty(ref _currentMethod, value))
            {
                switch (value.method)
                {
                    case CountMethod.Beat:
                        ConvertIntervals(Meter, Meter.Beat);
                        break;
                    case CountMethod.Measure:
                        ConvertIntervals(Meter.Beat, Meter);
                        break;
                }
                OnPropertyChanged(nameof(ClickLabel));
            }
        }
    }

    [ObservableProperty]
    private ClickState _state;

    private long _lastClick;
    private readonly List<int> _intervals = new();
    private Timer _timeout;

    /*
     * Handle the click event on the counting button - this updates the click state
     * to manage the core internal state machine and resets the timeout
     * 
     * Note the use of the RelayCommand attribute, this avoids a bunch of boilerplate
     * code to pipe the click command defined in the Xaml to this function.
     */
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

        // Here, we're manually letting the system know which properties
        //  are potentially updated when this method is invoked

        OnPropertyChanged(nameof(Mpm));
        OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(ClickLabel));
        _timeout = new Timer(OnTimeout, null, MaxWait, Timeout.Infinite);
    }


    /*
     * Timeout handler that sets the state to done or initial once the user has stopped clicking
     * for _maxWait milliseconds
     */
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

        OnPropertyChanged(nameof(ClickLabel));
    }

    // Clicks per minute - computed from the last ten intevals between clicks
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

    // Beats per minute calculated for clicks per minute and meter
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

    //  Measures per minute calculated from clicks per minute and meter
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

    // The label to show on the main button, reactive based on changes of state
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
        OnPropertyChanged(nameof(Mpm));
    }

}
