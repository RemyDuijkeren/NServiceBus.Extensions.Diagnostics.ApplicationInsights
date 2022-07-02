using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;

namespace NServiceBus.Extensions.Diagnostics.ApplicationInsights;

public class NServiceBusTelemetryModule : ITelemetryModule
{
    static readonly object LockObject = new();
    static bool IsInitialized;
    TelemetryClient? _client;

    public void Initialize(TelemetryConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        _client = new TelemetryClient(configuration);

        // Protect against multiple listeners if Initialize is called multiple times
        if (!IsInitialized)
        {
            lock (LockObject)
            {
                if (!IsInitialized)
                {
                    ActivitySource.AddActivityListener(new ActivityListener
                    {
                        ShouldListenTo = source => source.Name.StartsWith("NServiceBus.Extensions.Diagnostics"),
                        ActivityStopped = activity =>
                        {
                            GenerateOperationNameTag(activity);
                            
                            switch (activity.Kind)
                            {
                                case ActivityKind.Consumer:
                                    TrackRequestTelemetry(activity);
                                    break;
                                case ActivityKind.Producer:
                                    TrackDependencyTelemetry(activity);
                                    break;
                            }
                        },
                        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
                    });

                    IsInitialized = true;
                }
            }
        }
    }

    // track Request https://docs.microsoft.com/en-us/azure/azure-monitor/app/data-model-request-telemetry
    void TrackRequestTelemetry(Activity activity)
    {
        var telemetry = ActivityToTelemetry<RequestTelemetry>(activity);
        _client?.Track(telemetry);
    }

    // // track Dependency https://docs.microsoft.com/en-us/azure/azure-monitor/app/data-model-dependency-telemetry
    void TrackDependencyTelemetry(Activity activity)
    {
        var telemetry = ActivityToTelemetry<DependencyTelemetry>(activity);
        _client?.Track(telemetry);
    }

    static T ActivityToTelemetry<T>(Activity activity) where T : OperationTelemetry, new()
    {
        Debug.Assert(activity.Id != null, "Activity must be started prior calling this method");

        var telemetry = new T
        {
            Name = activity.DisplayName, // = <messaging.destination> <messaging.operation>
            Timestamp = activity.StartTimeUtc,
            Duration = activity.Duration,
            Success = activity.Status != ActivityStatusCode.Error
        };

        OperationContext operationContext = telemetry.Context.Operation;
        if (activity.IdFormat == ActivityIdFormat.W3C)
        {
            operationContext.Id = activity.TraceId.ToHexString();
            telemetry.Id = activity.SpanId.ToHexString();

            if (string.IsNullOrEmpty(operationContext.ParentId) && activity.ParentSpanId != default)
            {
                operationContext.ParentId = activity.ParentSpanId.ToHexString();
            }
        }
        else
        {
            operationContext.Id = activity.RootId;
            operationContext.ParentId = activity.ParentId;
            telemetry.Id = activity.Id;
        }

        foreach (var item in activity.Baggage)
        {
            if (!telemetry.Properties.ContainsKey(item.Key))
            {
                telemetry.Properties.Add(item);
            }
        }

        // TODO: Set these properties
        //requestTelemetry.Url // https://docs.microsoft.com/en-us/azure/azure-monitor/app/data-model-request-telemetry#url
        //requestTelemetry.ResponseCode // https://docs.microsoft.com/en-us/azure/azure-monitor/app/data-model-request-telemetry#response-code
        //dependencyTelemetry.ResultCode // https://docs.microsoft.com/en-us/azure/azure-monitor/app/data-model-dependency-telemetry#result-code
        foreach (var tag in activity.Tags)
        {
            switch (tag.Key)
            {
                case "OperationName":
                    telemetry.Context.Operation.Name = tag.Value;
                    break;
                case "messaging.nservicebus.originatingendpoint" when telemetry is RequestTelemetry requestTelemetry:
                    requestTelemetry.Source = tag.Value;
                    break;
                case "messaging.nservicebus.enclosedmessagetypes" when telemetry is DependencyTelemetry dependencyTelemetry:
                    dependencyTelemetry.Data = tag.Value; // $"{intent} {msgType}";
                    break;
                case "messaging.destination" when telemetry is DependencyTelemetry dependencyTelemetry:
                    dependencyTelemetry.Target = tag.Value; 
                    break;
                case "messaging.destination_kind" when telemetry is DependencyTelemetry dependencyTelemetry:
                    dependencyTelemetry.Type = tag.Value; // or NServiceBus
                    break;
            }
            
            if (!telemetry.Properties.ContainsKey(tag.Key))
            {
                telemetry.Properties.Add(tag);
            }
        }

        return telemetry;
    }

    static void GenerateOperationNameTag(Activity activity)
    {
        var intent = activity.Tags.FirstOrDefault(tag => tag.Key == "messaging.nservicebus.messageintent").Value;
        var operation = activity.Tags.FirstOrDefault(pair => pair.Key == "messaging.operation").Value;
        
        var enclosedMessageTypes = activity.Tags.FirstOrDefault(pair => pair.Key == "messaging.nservicebus.enclosedmessagetypes").Value ?? string.Empty;
        var messageTypes = string.Join(" | ", enclosedMessageTypes.Split(';').Select(type => type.Split(',').First()));
        
        switch (activity.Kind)
        {
            case ActivityKind.Producer: // Dependency OperationName: Publish MessageTypes, Send MessageTypes
                activity.AddTag("OperationName", $"{intent} {messageTypes}");
                break;
            case ActivityKind.Consumer: // Request OperationName: Process command MessageTypes, Process event MessageTypes, Process message MessageTypes
                switch (intent)
                {
                    case "Publish":
                        activity.AddTag("OperationName", $"{operation} event {messageTypes}");
                        break;
                    case "Send":
                        activity.AddTag("OperationName", $"{operation} command {messageTypes}");
                        break;
                    case "Reply":
                        activity.AddTag("OperationName", $"{operation} reply {messageTypes}");
                        break;
                    default:
                        activity.AddTag("OperationName", $"{operation} message {messageTypes}");
                        break;
                }
                break;
            default:
                activity.AddTag("OperationName", $"{operation} {intent} {messageTypes}");
                break;
        }
    }
}
