using System;
using System.Windows;

namespace SoundCloudClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private SplashScreen? _splash;

        private async void App_OnStartup(object sender, StartupEventArgs e)
        {
            // Глобальный обработчик непойманных исключений — предотвращает краш
            DispatcherUnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[CRASH] {args.Exception}");
                MessageBox.Show($"Ошибка: {args.Exception.Message}\n\n{args.Exception.StackTrace}", "CRASH", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    System.Diagnostics.Debug.WriteLine($"[CRASH Domain] {ex}");
            };

            // Показываем splash screen
            _splash = new SplashScreen();
            _splash.Show();

            // Создаём главное окно (но не показываем)
            var mainWindow = new MainWindow();

            // Даём splash-анимации отыграть минимум 3.5 секунды
            await Task.Delay(3500);

            // Плавно закрываем splash
            _splash.FadeOut();

            // Ждём завершения fade-out
            await Task.Delay(400);

            // Показываем главное окно
            mainWindow.Show();
        }
    }
}
