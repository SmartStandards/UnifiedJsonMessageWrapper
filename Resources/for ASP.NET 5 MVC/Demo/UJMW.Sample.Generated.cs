using MyBusinessNamespace;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MyBusinessNamespace.WebApi {
  
  /// <summary>
  /// Contains arguments for calling 'Foooo'.
  /// </summary>
  public class FooooRequest {
  
    /// <summary> Required Argument for 'Foooo' (String) </summary>
    [Required]
    public String a { get; set; }
  
  }
  
  /// <summary>
  /// Contains results from calling 'Foooo'.
  /// </summary>
  public class FooooResponse {
  
    /// <summary> Out-Argument of 'Foooo' (Int32) </summary>
    [Required]
    public Int32 b { get; set; }
  
    /// <summary> Return-Value of 'Foooo' (Boolean) </summary>
    [Required]
    public Boolean @return { get; set; }
  
  }
  
  /// <summary>
  /// Contains arguments for calling 'Kkkkkk'.
  /// </summary>
  public class KkkkkkRequest {
  
    /// <summary> Optional Argument for 'Kkkkkk' (Int32?) </summary>
    public Int32? optParamA { get; set; } = null;
  
    /// <summary> Optional Argument for 'Kkkkkk' (String) </summary>
    public String optParamB { get; set; }
  
  }
  
  /// <summary>
  /// Contains results from calling 'Kkkkkk'.
  /// </summary>
  public class KkkkkkResponse {
  
    /// <summary> Return-Value of 'Kkkkkk' (TestModel): MMMMMMMMMMMMMMMMMMM </summary>
    [Required]
    public TestModel @return { get; set; }
  
  }
  
  /// <summary>
  /// Contains arguments for calling 'AVoid'.
  /// Method: Meth
  /// </summary>
  public class AVoidRequest {
  
    /// <summary> Required Argument for 'AVoid' (TestModel): Bbbbbb </summary>
    [Required]
    public TestModel errorCode { get; set; }
  
  }
  
  /// <summary>
  /// Contains results from calling 'AVoid'.
  /// Method: Meth
  /// </summary>
  public class AVoidResponse {
  
  }
  
}
