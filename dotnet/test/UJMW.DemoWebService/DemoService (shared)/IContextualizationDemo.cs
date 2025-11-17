using DistributedDataFlow;
using System.Threading;

namespace Demo {

  [HasDataFlowSideChannel("tenant-identifiers")]
  public interface IContextualizationDemo {

    string DoAnything1(string regularParam, int dtHandle);

    string DoAnything2(string regularParam);

  }

  public class ContextualizationDemo : IContextualizationDemo {

    private static AmbientField _DataTransactionHandle = new AmbientField("dtHandle", true);

    public string DoAnything1(string regularParam, int dtHandle) {
      return $"Ambient '{_DataTransactionHandle.Value}' (param: {dtHandle})";
    }

    public string DoAnything2(string regularParam) {
      return $"Ambient '{_DataTransactionHandle.Value}'";
    }

  }

}
