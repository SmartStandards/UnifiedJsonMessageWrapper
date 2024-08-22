﻿
using Microsoft.Extensions.Options;
using System.Linq;

namespace System.Web.UJMW {

  public class DynamicUjmwControllerOptions {

    /// <summary>
    /// Controller-Route
    /// (you can use {0} {1} {...} to address type-names from generic arguments} OR "[Controller]" to address the controller name)
    /// </summary>
    public string ControllerRoute { get; set; } = "[controller]";

    /// <summary>
    /// Controller-Title
    /// (you can use {0} {1} {...} to address type-names from generic arguments}
    /// </summary>
    public string ControllerTitle { get; set; } = null;

    /// <summary>
    /// Use this to discriminate the name of your controller and autogenerated ujmw-dtos to
    /// resolve collisions by duplicate controller-names and/or method-names
    /// (you can use {0} {1} {...} to address type-names from generic arguments}
    /// </summary>
    public string ClassNameDiscriminator { get; set; } = null;

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

    public Type AuthAttribute { get; set; } = null;
    public object[] AuthAttributeConstructorParams { get; set; } = new object[] { };

  }

}
