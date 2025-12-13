using Blazor_WebAssembly;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using Xrpl.Client;

internal class Program
{
    private static async global::System.Threading.Tasks.Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        var client = new XrplClient("wss://s2.ripple.com", new XrplClient.ClientOptions()
        {
            ApiVersion = 2,
            UseCustomPing = true,
            RequestPolicy = Xrpl.Client.RequestFailurePolicy.WaitForConnection,
            ConnectionAcquisitionTimeout = System.TimeSpan.FromSeconds(30),
            MaxReconnectAttempts = 4,
            StopAfterMaxAttempts = false,
        });
        builder.Services.AddSingleton<IXrplClient>(client);

        await builder.Build().RunAsync();
    }
}