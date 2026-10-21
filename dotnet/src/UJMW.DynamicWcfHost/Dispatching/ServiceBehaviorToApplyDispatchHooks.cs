using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

using static System.Web.UJMW.CustomizedJsonFormatter;
using ServiceDescription = System.ServiceModel.Description.ServiceDescription;

namespace System.Web.UJMW {

  internal class ServiceBehaviorToApplyDispatchHooks : IServiceBehavior {

    private IncommingRequestSideChannelConfiguration _InboundSideChannelCfg;
    private OutgoingResponseSideChannelConfiguration _OutboundSideChannelCfg;
    private string _ApiGroupName;

    /// <summary></summary>
    /// <param name="inboundSideChannelCfg"></param>
    /// <param name="outboundSideChannelCfg"></param>
    /// <param name="apiGroupName">logical name of the endpoint (derived from the relative endpoint path, "" if unknown)</param>
    public ServiceBehaviorToApplyDispatchHooks(IncommingRequestSideChannelConfiguration inboundSideChannelCfg, OutgoingResponseSideChannelConfiguration outboundSideChannelCfg, string apiGroupName) {
      _InboundSideChannelCfg = inboundSideChannelCfg;
      _OutboundSideChannelCfg = outboundSideChannelCfg;
      _ApiGroupName = apiGroupName;
    }

    public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters) {
    }

    public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
      Type contractType = serviceDescription.Endpoints[0].Contract.ContractType;

      if (contractType == null) {
        throw new Exception($"ContractType for Service '{serviceDescription.Name}' was not found!");
      }

      foreach (ChannelDispatcher dispatcher in serviceHostBase.ChannelDispatchers) {
        foreach (EndpointDispatcher endpoint in dispatcher.Endpoints) {
          endpoint.DispatchRuntime.MessageInspectors.Add(new DispatchMessageInspector(_InboundSideChannelCfg, _OutboundSideChannelCfg, _ApiGroupName));
        }
      }

      IOperationBehavior loggingBehavior = new OperationBehaviorWhenDispatching(contractType);
      foreach (ServiceEndpoint endpoint in serviceDescription.Endpoints) {
        foreach (OperationDescription operation in endpoint.Contract.Operations) {
          if (!operation.Behaviors.Any(d => d is OperationBehaviorWhenDispatching)) {
            operation.Behaviors.Add(loggingBehavior);
          }
        }
      }

    }

    public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
    }

  }

}
