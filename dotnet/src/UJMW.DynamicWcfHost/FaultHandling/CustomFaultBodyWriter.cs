//using System.ServiceModel.Channels;

//namespace System.Web.UJMW {

//  internal class CustomFaultBodyWriter : BodyWriter {

//    private string _Message;

//    public CustomFaultBodyWriter(Exception e) : base(false) {
//      _Message = e.Message;
//    }

//    protected override void OnWriteBodyContents(System.Xml.XmlDictionaryWriter writer) {
//      //HACK: WCF PITA required (at elast for faultbody) to have an XML ROOT NODE before
//      //writing raw - OH MY GOD!!!
//      writer.WriteStartElement("UJMW");
//      writer.WriteRaw($"{{ \"fault\":\"{_Message}\" }}");
//      writer.WriteEndElement();
//      //because of this our standard needs to allow XML-encapsulated messages like
//      //<UJMW>{ ... }</UJMW>
//    }

//  }

//}
