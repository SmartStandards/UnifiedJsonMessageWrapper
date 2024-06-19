using System.ServiceModel.Channels;

namespace System.Web.UJMW {

  internal class CustomizedJsonContentTypeMapper : WebContentTypeMapper {

    public override WebContentFormat GetMessageFormatForContentType(string contentType) {
      return WebContentFormat.Raw;
    }

  }

}
