using Microsoft.Extensions.DependencyInjection;
using LinqRover.Core.Providers;
using LinqRover.Core.Storage;
using LinqRover.Services;
using Rustino.Blazor;

// Perfiles de conexión: (carpeta del ejecutable)/profiles/(usuario de Windows).json
// Se usa la carpeta del ejecutable y no AppContext.BaseDirectory: en un archivo único BaseDirectory es una carpeta temporal.
var profilesDir = Path.Combine(
    Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory,
    "profiles");
var profilePath = Path.Combine(profilesDir, $"{Environment.UserName}.json");
Directory.CreateDirectory(profilesDir);

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

// Al cerrar la ventana, termina todo el proceso (no quedan hilos de la UI ni del host en segundo plano).
Environment.Exit(0);
