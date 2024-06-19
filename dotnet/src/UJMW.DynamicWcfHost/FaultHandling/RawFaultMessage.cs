//using System.ServiceModel.Channels;
//using System.Xml;

//using Message = System.ServiceModel.Channels.Message;

//namespace System.Web.UJMW {

//  //https://stackoverflow.com/questions/62474354/wcf-message-formatter-not-formatting-fault-message
//  internal class RawFaultMessage : Message {

//    private Message _RelatedMessage;
//    private string _FaultMessage;

//    public RawFaultMessage(Message relatedMessage, string faultMessage) {
//      _RelatedMessage = relatedMessage;
//      _FaultMessage = faultMessage;
//    }

//    public override bool IsFault {
//      get {
//        return false;
//      }
//    }

//    public override MessageHeaders Headers {
//      get {
//        return _RelatedMessage.Headers;
//      }
//    }

//    public override MessageProperties Properties {
//      get {
//        return _RelatedMessage.Properties;
//      }
//    }

//    public override MessageVersion Version {
//      get {
//        return _RelatedMessage.Version;
//      }
//    }

//    protected override void OnWriteBodyContents(XmlDictionaryWriter writer) {
//      writer.WriteRaw(_FaultMessage);
//    }

//  }

//}
