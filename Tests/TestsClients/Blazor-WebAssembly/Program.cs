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
        builder.Services.AddSingleton<IXrplClient, XrplClient>(c=>new XrplClient("wss://s2.ripple.com",new XrplClient.ClientOptions(){ApiVersion = 2}));
        await builder.Build().RunAsync();
    }
}