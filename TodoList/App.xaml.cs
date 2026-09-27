using Microsoft.UI.Xaml;

namespace TodoList;

public partial class App : Application
{
    private const string MutexName = @"Local\TodoList_SingleInstance";
    private static System.Threading.Mutex? _instanceMutex;

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例：多开会让各自的 store 互相覆盖数据文件
        _instanceMutex = new System.Threading.Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            Environment.Exit(0);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
