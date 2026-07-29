namespace MistMapper.Widget;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WidgetForm());
    }
}
