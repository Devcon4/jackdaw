using BenchmarkDotNet.Running;

namespace Jackdaw.Benchmark.Tests;

public class Program
{
  public static void Main(string[] args)
  {
    var summary = BenchmarkRunner.Run(typeof(Program).Assembly, args: args);
  }
}
