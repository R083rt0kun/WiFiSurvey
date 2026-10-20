namespace WiFiSurvey;

/// <summary>
/// Wi-Fi Survey - Site survey and heatmap generation
/// CosmicBytez IT Operations
/// </summary>
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
