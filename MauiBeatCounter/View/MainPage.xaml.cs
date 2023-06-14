using MauiBeatCounter.ViewModel;

namespace MauiBeatCounter;

public partial class MainPage : ContentPage
{
    public MainPage(CounterViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
    }
}

