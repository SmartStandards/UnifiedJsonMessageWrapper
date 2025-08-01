﻿using Microsoft.Extensions.Options;
using System.Linq;

namespace System.Web.UJMW {

  public class DynamicUjmwControllerOptions {

    /// <summary>
    /// Use this to custimize the route of your Endpoint.
    /// Therefore you can use Placeholders:
    /// {0} {1} {...} to address type-names from generic arguments, 
    /// '{Controller}' for the controller name (repecting the 'ControllerNamePattern') or
    /// '[Controller]' for the original controller name)
    /// </summary>
    public string ControllerRoute { get; set; } = null;

    /// <summary>
    /// Use this to custimize the Title of your Endpoint (as shown in Swagger).
    /// Therefore you can use Placeholders:
    /// {0} {1} {...} to address type-names from generic arguments, 
    /// '{Controller}' for the controller name (repecting the 'ControllerNamePattern') or
    /// '[Controller]' for the original controller name)
    /// </summary>
    public string ControllerTitle { get; set; } = null;

    /// <summary>
    /// Use this to discriminate the name of your controller and autogenerated ujmw-dtos to
    /// resolve collisions by duplicate controller-names and/or method-names.
    /// Therefore you can use Placeholders:
    /// {0} {1} {...} to address type-names from generic arguments, 
    /// '{Controller}' for the controller name (repecting the 'ControllerNamePattern') or
    /// '[Controller]' for the original controller name)
    /// </summary>
    [Obsolete("The 'ClassNameDiscriminator' affects the controller class name and wrapper class names in the same way - this is deprecated! Please use 'ControllerNamePattern' and/or 'WrapperNamePattern' instead!")]
    public string ClassNameDiscriminator { get; set; } = null;

    /// <summary>
    /// Use this to discriminate the name of your controller to resolve collisions by duplicate controller-names.
    /// Therefore you can use Placeholders:
    /// {0} {1} {...} to address type-names from generic arguments, 
    /// '{Controller}' for the controller name (repecting the 'ControllerNamePattern') or
    /// '[Controller]' for the original controller name)
    /// </summary>
    public string ControllerNamePattern { get; set; } = null;

    /// <summary>
    /// Use this to discriminate the name of your request-/response-wrapper classes to
    /// resolve collisions by duplicate method-names comming from multiple controllers.
    /// Therefore you can use Placeholders:
    /// {0} {1} {...} to address type-names from generic arguments, 
    /// '{Controller}' for the controller name (repecting the 'ControllerNamePattern') or
    /// '[Controller]' for the original controller name) or
    /// '[Method]' for the Method-Name
    /// </summary>
    public string WrapperNamePattern { get; set; } = null;

    /// <summary> build the '_' property within the json message wrapper of requests to transport ambient information </summary>
    public bool EnableRequestSidechannel { get; set; } = true;

    /// <summary> build the '_' property within the json message wrapper of responses to transport ambient information </summary>
    public bool EnableResponseSidechannel { get; set; } = true;

    /// <summary>
    /// When true, an 'IAsyncActionFilter' will be applied to the dynamic controller internally,
    /// to redirect the Http-Authorization headers to the 'UjmwHostConfiguration.AuthHeaderEvaluator'-Method.
    /// You can set this to false to increase performance, if no authorization is needed or is applied in another way.
    /// </summary>
    public bool EnableAuthHeaderEvaluatorHook { get; set; } = true;

    /// <summary>
    /// If enabled, any user can access the controller via http-GET (without authentication) and will see an info-site.
    /// </summary>
    public bool EnableInfoSite { get; set; } = false;

    public Type AuthAttribute { get; set; } = null;
    public object[] AuthAttributeConstructorParams { get; set; } = new object[] { };

  }

}
