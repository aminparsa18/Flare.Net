namespace ExampleApp.Maui;

public partial class SlowPage : ContentPage
{
    public SlowPage()
    {
        InitializeComponent();
        // Stands in for real work on the navigation path (a heavy layout, a synchronous load).
        Thread.Sleep(700);
    }
}
