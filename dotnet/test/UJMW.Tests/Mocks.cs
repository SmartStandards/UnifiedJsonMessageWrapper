using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Web.UJMW {

  public interface IMyService {

    void ThisIsAVoid();

    int Calculate(int foo, int bar);

    void ThisThrowsAnEx();

    BeautifulPropertyBag GetABag();

    bool ProcessABag(BeautifulPropertyBag aBag);

    bool ProcessAParamarray(string foo, params int[] bars);

    string OverloadedVariant(string foo);
    string OverloadedVariant(int foo);

    bool OptionalStringIsDefault(int foo, string bar = "xxx");

    bool NullableIntWasNull(Nullable<int> theInt);

    bool OptionalIntIsDefault(int foo, int bar = 123);

    bool OptionalObjIsDefault(int foo, BeautifulPropertyBag bar = null);

    void ABagByRef(ref BeautifulPropertyBag aBag);
    void IntByRef(ref int foo);

    void ABagOut(out BeautifulPropertyBag aBag);
    void IntOut(out int foo);

  }

  public class BeautifulPropertyBag {
    public string Foo { get; set; }
    public int Bar { get; set; }
  }

  public class MyServiceImplementation : IMyService {

    public int Calculate(int foo, int bar) {
      return foo + bar;
    }

    public BeautifulPropertyBag GetABag() {
      return new BeautifulPropertyBag { Bar = 42, Foo = "good" }; 
    }
    public void ABagOut(out BeautifulPropertyBag aBag) {
      aBag = new BeautifulPropertyBag { Bar = 42, Foo = "good" };
    }
    public void ABagByRef(ref BeautifulPropertyBag aBag) {
      aBag.Bar = 4711;
    }

    public void IntByRef(ref int foo) {
      foo++;
    }

    public void IntOut(out int foo) {
      foo = 4711;
    }

    public bool NullableIntWasNull(int? theInt) {
      return theInt.HasValue;
    }

    public bool OptionalIntIsDefault(int foo, int bar = 123) {
      return bar == 123;
    }

    public bool OptionalObjIsDefault(int foo, BeautifulPropertyBag bar = null) {
      return bar == null;
    }

    public bool OptionalStringIsDefault(int foo, string bar = "xxx") {
      return bar == "xxx";
    }

    public string OverloadedVariant(string foo) {
      return "STRING";
    }

    public string OverloadedVariant(int foo) {
      return "INT";
    }

    public bool ProcessABag(BeautifulPropertyBag aBag) {
      return aBag != null;
    }

    public bool ProcessAParamarray(string foo, params int[] bars) {
      return bars.Length > 0;
    }

    public void ThisIsAVoid() {
    }

    public void ThisThrowsAnEx() {
      throw new ApplicationException("BOOM");
    }

  }

}
