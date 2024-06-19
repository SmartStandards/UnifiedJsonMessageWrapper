using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace System.Web.UJMW {

  internal class CustomizedWebHttpBehaviourForJson : WebHttpBehavior {

    private bool _RequestWrapperContainsUnderline;
    private OutgoingResponseSideChannelConfiguration _OutgoingResponseSideChannelConfig;

    public CustomizedWebHttpBehaviourForJson(bool requestWrapperContainsUnderline, OutgoingResponseSideChannelConfiguration outgoingResponseSideChannelConfig) {
      _RequestWrapperContainsUnderline = requestWrapperContainsUnderline;
      _OutgoingResponseSideChannelConfig = outgoingResponseSideChannelConfig;

      this.DefaultOutgoingRequestFormat = System.ServiceModel.Web.WebMessageFormat.Json;
      this.DefaultOutgoingResponseFormat = System.ServiceModel.Web.WebMessageFormat.Json;
      this.DefaultBodyStyle = System.ServiceModel.Web.WebMessageBodyStyle.Wrapped;

    }

    protected override IDispatchMessageFormatter GetRequestDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
      return new CustomizedJsonFormatter(
        operationDescription, true, endpoint.ListenUri,
        _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
      );
    }

    protected override IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
      return new CustomizedJsonFormatter(
        operationDescription, false, endpoint.ListenUri,
        _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
      );
    }

    protected override IClientMessageFormatter GetRequestClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
      return new CustomizedJsonFormatter(
        operationDescription, true, endpoint.ListenUri,
        _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
      );
    }

    protected override IClientMessageFormatter GetReplyClientFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint) {
      return new CustomizedJsonFormatter(
        operationDescription, false, endpoint.ListenUri,
        _RequestWrapperContainsUnderline, _OutgoingResponseSideChannelConfig
      );
    }

    protected override void AddServerErrorHandlers(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher) {
      base.AddServerErrorHandlers(endpoint, endpointDispatcher);

      //TODO: TEST THIS ERROR HOOK
      //https://stackoverflow.com/questions/23212705/wcf-how-to-handle-errors-globally
      //endpointDispatcher.DispatchRuntime.ChannelDispatcher.ErrorHandlers.Add(new WebHttpErrorHandler());

      //IErrorHandler errorHandler = new CustomErrorHandler();
      //foreach (var channelDispatcher in serviceHostBase.ChannelDispatchers) {
      //  channelDispatcher.ErrorHandlers.Add(errorHandler);
      //}

    }

  }

}
