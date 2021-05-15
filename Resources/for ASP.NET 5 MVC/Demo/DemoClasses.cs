using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace MyBusinessNamespace {

  public interface IFoo {

    bool Foooo(string a, out int b);

    TestModel Kkkkkk();

    /// <summary>
    /// Meth
    /// </summary>
    /// <param name="errorCode"> Bbbbbb </param>
    void AVoid(TestModel errorCode);

  }


  /// <summary>
  /// MMMMMMMMMMMMMMMMMMM
  /// </summary>
  public class TestModel {

    /// <summary>
    /// jfjfj
    /// </summary>
    [Required()]
    public String FooBar { get; set; } = "default";

  }




}
