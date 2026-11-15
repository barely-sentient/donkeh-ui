using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Donkeh.Windowing.Browser;

/// <summary>Bootstraps a Donkeh application in a Blazor WebAssembly host.</summary>
/// <remarks>
/// The Blazor host is used only for its WebAssembly runtime and JavaScript
/// interop. No Blazor components are rendered; UILab owns the tree and
/// <see cref="DomHost"/> mirrors it into native DOM elements.
/// </remarks>
public static class DonkehBrowser
{
    /// <summary>Runs the application until the browser page is closed.</summary>
    /// <param name="args">Host arguments supplied by the platform entry point.</param>
    /// <returns>A task that completes when the WebAssembly host stops.</returns>
    public static async Task RunAsync(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.Services.AddSingleton<DomHost>();

        var host = builder.Build();
        await host.Services.GetRequiredService<DomHost>().StartAsync();
        await host.RunAsync();
    }
}
