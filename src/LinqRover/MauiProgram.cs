using LinqRover.Core.Providers;
using LinqRover.Core.Storage;
using LinqRover.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;

namespace LinqRover;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if WINDOWS
		builder.ConfigureLifecycleEvents(events =>
			events.AddWindows(windows => windows.OnWindowCreated(window => ApplyVsCodeTitleBar(window))));
#endif

		// Estado de la barra de actividades y de estado (una sola ventana)
		builder.Services.AddSingleton<WorkbenchState>();

		// Servicios de datos (Core)
		builder.Services.AddSingleton<DatabaseProviders>(_ => DatabaseProviders.Create());
		builder.Services.AddSingleton<DefaultConnectionSettings>(_ => DefaultConnectionSettings.Load());
		builder.Services.AddSingleton<ProfileStore>(_ => new ProfileStore(
			Path.Combine(FileSystem.AppDataDirectory, "connections.json"),
			new DpapiSecretProtector()));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

#if WINDOWS
	/// <summary>
	/// Pinta la barra de título nativa de Windows con los colores de VS Code «Dark Modern»
	/// (titleBar.activeBackground #181818), para que encaje con el resto de la interfaz.
	/// </summary>
	private static void ApplyVsCodeTitleBar(Microsoft.UI.Xaml.Window window)
	{
		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

		static Windows.UI.Color Rgb(byte r, byte g, byte b) => new() { A = 255, R = r, G = g, B = b };

		var titleBar = appWindow.TitleBar;
		titleBar.BackgroundColor = Rgb(0x18, 0x18, 0x18);
		titleBar.InactiveBackgroundColor = Rgb(0x18, 0x18, 0x18);
		titleBar.ButtonBackgroundColor = Rgb(0x18, 0x18, 0x18);
		titleBar.ButtonInactiveBackgroundColor = Rgb(0x18, 0x18, 0x18);
		titleBar.ButtonHoverBackgroundColor = Rgb(0x2a, 0x2d, 0x2e);
		titleBar.ButtonPressedBackgroundColor = Rgb(0x3c, 0x3c, 0x3c);
		titleBar.ButtonForegroundColor = Rgb(0xcc, 0xcc, 0xcc);
		titleBar.ButtonInactiveForegroundColor = Rgb(0x9d, 0x9d, 0x9d);
		titleBar.ButtonHoverForegroundColor = Rgb(0xff, 0xff, 0xff);
		titleBar.ButtonPressedForegroundColor = Rgb(0xff, 0xff, 0xff);
	}
#endif
}
