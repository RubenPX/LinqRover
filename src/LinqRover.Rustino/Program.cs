using Microsoft.Extensions.DependencyInjection;
using LinqRover.Core.Providers;
using LinqRover.Core.Storage;
using LinqRover.Services;
using Rustino.Blazor;

// Mismo sitio que la app de MAUI, para que las conexiones guardadas sigan visibles.
var profilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "User Name", "com.companyname.linqrover", "Data", "connections.json");
Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);

var builder = RustinoBlazorAppBuilder.CreateDefault(args);
var services = builder.Services;

services.AddSingleton<WorkbenchState>();
services.AddSingleton<DatabaseProviders>(_ => DatabaseProviders.Create());
services.AddSingleton<DefaultConnectionSettings>(_ => DefaultConnectionSettings.Load());
services.AddSingleton<ProfileStore>(_ => new ProfileStore(profilePath, new DpapiSecretProtector()));

builder.RootComponents.Add<LinqRover.Components.Routes>("#app");

var app = builder.Build();
app.MainWindow.SetTitle("LinqRover").SetSize(1440, 900).SetDevToolsEnabled(true);
app.Run();
