
namespace System.Web.UJMW {

  public class DynamicUjmwControllerOptions {

    public string ControllerRoute { get; set; } = "[controller]";

    /// <summary> build the '_' property within the json message wrapper of requests to transport ambient information </summary>
    public bool EnableRequestSidechannel { get; set; } = true;

    /// <summary> build the '_' property within the json message wrapper of responses to transport ambient information </summary>
    public bool EnableResponseSidechannel { get; set; } = true;

    public Type AuthAttribute { get; set; } = null;
    public object[] AuthAttributeConstructorParams { get; set; } = new object[] { };

  }

}
