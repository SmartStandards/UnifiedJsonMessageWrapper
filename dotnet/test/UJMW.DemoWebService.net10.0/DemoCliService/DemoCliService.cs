using DistributedDataFlow;
using System;

namespace UJMW.DemoCommandLineExe {

  [HasDataFlowSideChannel("tenant-identifiers")]
  public interface IDemoCliService {
    void Run();
    void Run1(int count);
    void Run2(string message, bool uppercase = false);
    void Run3(double value, out string result);
    int Add(int a, int b);
    string[] GetArray();
    string GetUmlaute();
  } 
}
