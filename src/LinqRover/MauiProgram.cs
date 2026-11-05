using LinqRover.Core.Providers;
using LinqRover.Core.Storage;
using LinqRover.Services;
using Microsoft.Extensions.Logging;
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
}
