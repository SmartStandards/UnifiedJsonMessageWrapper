using MyBusinessNamespace;
using System;
using System.Collections.Generic;

namespace MyBusinessNamespace.WebApi {
  
  /// <summary>
  /// Contains arguments for calling 'Foooo'.
  /// </summary>
  public class FooooRequest {
  
    /// <summary> Required Argument for 'Foooo' (String) </summary>
    public String a { get; set; }
  
  }
  
  /// <summary>
  /// Contains results from calling 'Foooo'.
  /// </summary>
  public class FooooResponse {
  
    /// <summary> Out-Argument of 'Foooo' (Int32) </summary>
    public Int32 b { get; set; }
  
    /// <summary> Return-Value of 'Foooo' (Boolean) </summary>
    public Boolean @return { get; set; }
  
  }
  
  /// <summary>
  /// Contains arguments for calling 'Kkkkkk'.
  /// </summary>
  public class KkkkkkRequest {
  
  }
  
  /// <summary>
  /// Contains results from calling 'Kkkkkk'.
  /// </summary>
  public class KkkkkkResponse {
  
    /// <summary> Return-Value of 'Kkkkkk' (TestModel): MMMMMMMMMMMMMMMMMMM </summary>
    public TestModel @return { get; set; }
  
  }
  
  /// <summary>
  /// Contains arguments for calling 'AVoid'.
  /// Method: WRONG
  /// </summary>
  public class AVoidRequest {
  
    /// <summary> Required Argument for 'AVoid' (TestModel): YEEEEDS </summary>
    public TestModel errorCode { get; set; }
  
  }
  
  /// <summary>
  /// Contains results from calling 'AVoid'.
  /// Method: WRONG
  /// </summary>
  public class AVoidResponse {
  
  }
  
}
