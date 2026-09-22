using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EpicCottonGame;
using EpicCottonGame.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// The client reads static JSON configuration from the same site.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// One shared game-state service for the current browser tab.
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddScoped<LocalSaveService>();

await builder.Build().RunAsync();
