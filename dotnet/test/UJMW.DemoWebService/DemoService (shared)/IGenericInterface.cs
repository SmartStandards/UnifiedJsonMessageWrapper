
namespace Demo {

  public interface IGenericInterface<T1,T2> {

    T1 Test(T2 input);

  }

  public class Foo {
    public int Id { get; set; }
  }
  public class Bar {
    public string Key { get; set; }
  }

}

