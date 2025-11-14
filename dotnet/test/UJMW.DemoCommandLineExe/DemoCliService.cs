using System;

namespace UJMW.DemoCommandLineExe {

  public interface IDemoCliService {
    void Run();
    void Run1(int count);
    void Run2(string message, bool uppercase = false);
    void Run3(double value, out string result);
    int Add(int a, int b);
    string[] GetArray();
    string GetUmlaute();
  }

  


  public class DemoCliService : IDemoCliService {
    private static string _ExpensiveCache = null;
    private string GetExpensiveCache() {
      if (_ExpensiveCache == null) {
        // Simulate expensive operation
        System.Threading.Thread.Sleep(10000);
        _ExpensiveCache = "Expensive Data";
      }
      return _ExpensiveCache;
    }
    public void Run() {
      Console.WriteLine(GetExpensiveCache());
      //Console.WriteLine("DemoService is running.");
    }

    public void Run1(int count) {
      //Console.WriteLine($"DemoService is running with count: {count}");
    }

    public void Run2(string message, bool uppercase = false) {
      if (uppercase) {
        message = message.ToUpper();
      }
      //Console.WriteLine($"DemoService says: {message}");
    }

    public void Run3(double value, out string result) {
      result = $"Value is {value}. {GetExpensiveCache()}";
      //Console.WriteLine(result);
    }

    public int Add(int a, int b) {
      //Console.WriteLine("Adding numbers.");
      int result = a + b;
      //Console.WriteLine($"Result: {result}");
      return a + b;
    }

    public string[] GetArray() {
      Console.WriteLine("Kauderwelsch");
      //throw new Exception("Kauderwelsch");
      return new string[] { "Hi", "there!" };
    }
    public string GetUmlaute() {
      return "ÄÖÜäöüß";
    }
  }
}
