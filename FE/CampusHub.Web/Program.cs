using Dev4u.CampusHub.Web;
using Dev4u.CampusHub.Web.Api;
using Dev4u.CampusHub.Web.Demo;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(TimeProvider.System);

// The views only know the ViewModels, the ViewModels only know the ports: the backend is chosen here.
var isDemo = string.Equals(builder.Configuration["Api:Mode"], "Demo", StringComparison.OrdinalIgnoreCase);
builder.Services.AddSingleton(new AppMode(isDemo));
if (isDemo)
{
    builder.Services.AddDemoBackend();
}
else
{
    // Same origin as the gateway that serves the app: the session cookie goes along, no token in the browser.
    builder.Services.AddGatewayBackend(new Uri(builder.HostEnvironment.BaseAddress));
}

// One ViewModel instance per page visit. They are not IDisposable on purpose (the container would keep every
// transient disposable alive): their clean-up runs in Deactivate, called by the page when it is disposed.
builder.Services.AddTransient<CatalogViewModel>();
builder.Services.AddTransient<MyEnrollmentsViewModel>();
builder.Services.AddTransient<TeachingViewModel>();
builder.Services.AddTransient<NotificationsViewModel>();
builder.Services.AddTransient<ShellViewModel>();

await builder.Build().RunAsync();
