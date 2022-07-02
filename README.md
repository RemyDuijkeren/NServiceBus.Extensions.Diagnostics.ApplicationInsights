# NServiceBus.Extensions.Diagnostics.ApplicationInsights

![CI](https://github.com/AutomateValue/NServiceBus.Extensions.Diagnostics.ApplicationInsights/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/dt/NServiceBus.Extensions.Diagnostics.ApplicationInsights.svg)](https://www.nuget.org/packages/NServiceBus.Extensions.Diagnostics.ApplicationInsights)
[![NuGet](https://img.shields.io/nuget/vpre/NServiceBus.Extensions.Diagnostics.ApplicationInsights.svg)](https://www.nuget.org/packages/NServiceBus.Extensions.Diagnostics.ApplicationInsights)

## Usage

The `NServiceBus.Extensions.Diagnostics.ApplicationInsights` package generates telemetry information for [Application Insights](https://azure.microsoft.com/en-us/services/monitor/)
that is being exposed by [NServiceBus.Extensions.Diagnostics](https://www.nuget.org/packages/NServiceBus.Extensions.Diagnostics).

To use `NServiceBus.Extensions.Diagnostics.ApplicationInsights`, simply reference the package and register the `ITelemetryModule` 
with the `NServiceBusTelemetryModule` as a singleton service.

An example with a Worker Service is shown here:

```csharp
    services.AddSingleton<ITelemetryModule, NServiceBusTelemetryModule>();
    services.AddApplicationInsightsTelemetryWorkerService(opt =>
    {
#if DEBUG
        opt.DeveloperMode = true;
#endif
    });
```

## Application Insights

### Request Telemetry and Operations support

TODO: Explain how to use the Application Insights telemetry.

![Performance operations](docs/appinsights-performance-operations.png)

### Dependency Telemetry support

TODO: Explain dependency telemetry

## Activity

The `NServiceBus.Extensions.Diagnostics` will add the NServiceBus context to `Activity`, like incoming headers into `Activity.Baggage`.

If you would like to add additional correlation context, inside your handler you can add additional baggage:

```csharp
Activity.Current.AddBaggage("mykey", "myvalue");
```

The additional correlation context will be added to the generated telemetry for Application Insights. Common usage for correlation context
are user IDs, session IDs, conversation IDs, and anything you might want to search traces to triangulate specific traces.

### Enriching Activities

To enrich an Activity in a behavior or handler, the current executing NServiceBus activity is set in a `ICurrentActivity` extension value. In a handler or behavior you may retrieve this value and modify the `Activity`:

```csharp
public Task Handle(Message message, IMessageHandlerContext context)
{
    var currentActivity = context.Extensions.Get<ICurrentActivity>();

    currentActivity.Current?.AddBaggage("cart.operation.id", message.Id.ToString());

    // rest of method
}
```

### Configuring

In order to limit potentially sensitive information, the message contents are not passed through to the `ActivitySource` by default. To enable this, configure the `InstrumentationOptions` setting in your `EndpointConfiguration`:

```csharp
var settings = endpointConfiguration.GetSettings();

settings.Set(new NServiceBus.Extensions.Diagnostics.InstrumentationOptions
{
    CaptureMessageBody = true
});
```

This will set a `messaging.message_payload` tag with the UTF8-decoded message body.